import AppKit
import CoreGraphics

struct ScreenProtectionSettings: Codable, Equatable {
    var enabled = false
    var seconds = 30
    var mode = "input"
    var ignoreWallpaper = true
    var selectedUUIDs: [String]?
    static func validSeconds(_ value: Int) -> Int { min(3600, max(1, value)) }
}
extension ScreenProtectionSettings {
    enum CodingKeys: String, CodingKey { case enabled, seconds, mode, ignoreWallpaper, selectedUUIDs }
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy:CodingKeys.self)
        enabled = try values.decodeIfPresent(Bool.self,forKey:.enabled) ?? false
        seconds = try values.decodeIfPresent(Int.self,forKey:.seconds) ?? 30
        // Existing Mac profiles keep the permission-free input-idle behavior.
        mode = try values.decodeIfPresent(String.self,forKey:.mode) ?? "input"
        ignoreWallpaper = try values.decodeIfPresent(Bool.self,forKey:.ignoreWallpaper) ?? true
        selectedUUIDs = try values.decodeIfPresent([String].self,forKey:.selectedUUIDs)
    }
}

// Use uptime so clock/time-zone changes cannot trigger or delay a blackout.
struct DisplayIdleTimers {
    private(set) var lastActivity: [CGDirectDisplayID: TimeInterval] = [:]
    private var keyboardDisplayID: CGDirectDisplayID?
    mutating func reset(_ ids: [CGDirectDisplayID], at now: TimeInterval) {
        lastActivity = Dictionary(uniqueKeysWithValues: ids.map { ($0, now) })
        keyboardDisplayID = nil
    }
    mutating func activity(on id: CGDirectDisplayID, at now: TimeInterval) {
        if lastActivity[id] != nil { lastActivity[id] = now }
    }
    mutating func pointerActivity(on id: CGDirectDisplayID?, clicked: Bool, at now: TimeInterval) {
        if clicked { keyboardDisplayID = id }
        if let id { activity(on: id, at: now) }
    }
    mutating func keyboardActivity(pointerDisplay: CGDirectDisplayID?, at now: TimeInterval) {
        // ponytail: use the last clicked display without inspecting other apps' windows or key contents.
        if let id = keyboardDisplayID ?? pointerDisplay { activity(on: id, at: now) }
    }
    func expired(at now: TimeInterval, after seconds: TimeInterval) -> Set<CGDirectDisplayID> {
        Set(lastActivity.filter { now - $0.value >= seconds }.keys)
    }
}

final class OLEDShieldPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

@MainActor
final class OLEDProtection {
    private(set) var settings = ScreenProtectionSettings()
    var enabled: Bool { settings.enabled }
    var seconds: Int { settings.seconds }
    func configure(_ settings: ScreenProtectionSettings) {
        guard self.settings != settings else { return }
        self.settings = settings
        reset()
    }
    var onChange: (() -> Void)?
    var blankedDisplayIDs: [CGDirectDisplayID] { shields.keys.sorted() }
    var protectedDisplayIDs: [CGDirectDisplayID] { enabled && !suspended ? screens.keys.sorted() : [] }
    var keyboardCounterAvailable: Bool { lastKeyboardEvents.contains { $0 > 0 } }
    var captureMessage: String { content.message }
    private let content = ContentChangeMonitor()
    private var forced = Set<CGDirectDisplayID>()
    private var needsPointerExit = Set<CGDirectDisplayID>()
    private var settingsVisible = false
    private var idle = DisplayIdleTimers()
    private var screens: [CGDirectDisplayID: NSScreen] = [:]
    private var shields: [CGDirectDisplayID: OLEDShieldPanel] = [:]
    private var timer: Timer?
    private var globalMonitor: Any?
    private var localMonitor: Any?
    private var observers: [(NotificationCenter, NSObjectProtocol)] = []
    private var lastPointer = NSPoint.zero
    private var suspended = false
    private var lastKeyboardEvents: [UInt32] = []

    func start() {
        guard timer == nil else { return }
        reset()
        content.onFrame = { [weak self] id, changed in
            guard let self, changed else { return }
            idle.activity(on:id,at:ProcessInfo.processInfo.systemUptime)
            if !forced.contains(id), let shield = shields.removeValue(forKey:id) { shield.close(); onChange?() }
        }
        content.onStatus = { [weak self] in self?.onChange?() }
        let mask: NSEvent.EventTypeMask = [.mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged,
            .leftMouseDown, .rightMouseDown, .otherMouseDown, .leftMouseUp, .rightMouseUp, .otherMouseUp, .scrollWheel]
        // Mouse events need no keyboard/accessibility monitor. Local and global scopes are complementary.
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: mask) { [weak self] event in self?.pointerActivity(event) }
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: mask) { [weak self] event in
            let wasShielded = event.window is OLEDShieldPanel
            self?.pointerActivity(event)
            // The first click/wheel wakes the screen without acting on invisible controls underneath.
            return wasShielded ? nil : event
        }
        observe(.default, NSApplication.didChangeScreenParametersNotification) { $0.reset() }
        let workspace = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.willSleepNotification, NSWorkspace.screensDidSleepNotification,
                     NSWorkspace.sessionDidResignActiveNotification] {
            observe(workspace, name) { $0.suspended = true; $0.revealAll() }
        }
        for name in [NSWorkspace.didWakeNotification, NSWorkspace.screensDidWakeNotification,
                     NSWorkspace.sessionDidBecomeActiveNotification] {
            observe(workspace, name) { $0.suspended = false; $0.reset() }
        }
        let timer = Timer(timeInterval: 0.25, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        timer.tolerance = 0.05
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }
    private func observe(_ center: NotificationCenter, _ name: Notification.Name,
                         action: @escaping @MainActor (OLEDProtection) -> Void) {
        let token = center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { if let self { action(self) } }
        }
        observers.append((center, token))
    }
    func stop() {
        content.stop()
        timer?.invalidate(); timer = nil
        if let globalMonitor { NSEvent.removeMonitor(globalMonitor) }
        if let localMonitor { NSEvent.removeMonitor(localMonitor) }
        globalMonitor = nil; localMonitor = nil
        for (center, token) in observers { center.removeObserver(token) }
        observers.removeAll()
        revealAll()
    }
    private func reset() {
        screens.removeAll()
        for screen in NSScreen.screens {
            guard let id = (screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value,
                  CGDisplayIsBuiltin(id) == 0, CGDisplayIsInMirrorSet(id) == 0 else { continue }
            if let selection = settings.selectedUUIDs {
                guard let uuid = CGDisplayCreateUUIDFromDisplayID(id)?.takeRetainedValue(),
                      selection.contains(CFUUIDCreateString(nil,uuid) as String) else { continue }
            }
            screens[id] = screen
        }
        revealAll()
    }
    func revealAll() {
        for shield in shields.values { shield.close() }
        shields.removeAll()
        forced.removeAll(); needsPointerExit.removeAll()
        idle.reset(Array(screens.keys), at: ProcessInfo.processInfo.systemUptime)
        lastPointer = NSEvent.mouseLocation
        lastKeyboardEvents = keyboardEvents()
        updateCapture()
        onChange?()
    }
    func setSettingsVisible(_ visible: Bool) {
        settingsVisible = visible
        if visible { revealAll() }
        else { idle.reset(Array(screens.keys),at:ProcessInfo.processInfo.systemUptime); updateCapture() }
    }
    private func updateCapture() {
        if enabled && settings.mode == "content" && !suspended && !settingsVisible {
            content.start(ids:Set(screens.keys),ignoreWallpaper:settings.ignoreWallpaper)
        } else { content.stop() }
    }
    func retryCapture() { content.stop(); updateCapture() }
    func toggleForced(_ id: CGDirectDisplayID) {
        guard !suspended, let screen = NSScreen.screens.first(where:{
            ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value == id
        }), CGDisplayIsBuiltin(id) == 0 else { return }
        if forced.remove(id) != nil {
            needsPointerExit.remove(id); shields.removeValue(forKey:id)?.close()
            idle.activity(on:id,at:ProcessInfo.processInfo.systemUptime)
        } else {
            forced.insert(id)
            if screen.frame.contains(NSEvent.mouseLocation) { needsPointerExit.insert(id) }
            cover(id,screen:screen)
        }
        onChange?()
    }
    private func cover(_ id: CGDirectDisplayID, screen: NSScreen) {
        guard shields[id] == nil else { return }
        let panel = OLEDShieldPanel(contentRect:screen.frame,styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false,screen:screen)
        panel.isReleasedWhenClosed = false
        panel.backgroundColor = .black; panel.isOpaque = true; panel.hasShadow = false
        panel.hidesOnDeactivate = false; panel.isFloatingPanel = true
        panel.level = .screenSaver; panel.animationBehavior = .none
        panel.collectionBehavior = [.canJoinAllSpaces,.fullScreenAuxiliary,.stationary,.ignoresCycle,.canJoinAllApplications]
        panel.acceptsMouseMovedEvents = true
        let view = NSView(frame:NSRect(origin:.zero,size:screen.frame.size))
        view.wantsLayer = true; view.layer?.backgroundColor = NSColor.black.cgColor
        panel.contentView = view; panel.setFrame(screen.frame,display:true); panel.orderFrontRegardless()
        shields[id] = panel
    }
    private func displayUnderPointer() -> CGDirectDisplayID? {
        NSScreen.screens.first(where:{$0.frame.contains(NSEvent.mouseLocation)})
            .flatMap { ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value }
    }
    private func keyboardEvents() -> [UInt32] {
        // Aggregate counters only: no keyboard event tap, key codes, or text are collected.
        [CGEventType.keyDown, .keyUp, .flagsChanged].map {
            CGEventSource.counterForEventType(.combinedSessionState, eventType:$0)
        }
    }
    private func pointerActivity(_ event: NSEvent? = nil) {
        guard enabled || !forced.isEmpty, !suspended else { return }
        let point = NSEvent.mouseLocation
        lastPointer = point
        let id = displayUnderPointer()
        for waiting in needsPointerExit where waiting != id { needsPointerExit.remove(waiting) }
        let clicked = event.map { [.leftMouseDown,.rightMouseDown,.otherMouseDown].contains($0.type) } ?? false
        idle.pointerActivity(on:id,clicked:clicked,at:ProcessInfo.processInfo.systemUptime)
        guard let id else { return }
        if forced.contains(id) {
            guard !needsPointerExit.contains(id) else { return }
            forced.remove(id)
        }
        if let shield = shields.removeValue(forKey: id) {
            shield.close(); onChange?()
        }
    }
    private func tick() {
        guard enabled || !forced.isEmpty, !suspended else { return }
        // Fallback for event-monitor gaps, including pointer warps between screens.
        if NSEvent.mouseLocation != lastPointer || NSEvent.pressedMouseButtons != 0 { pointerActivity() }
        let keyboard = keyboardEvents()
        if keyboard != lastKeyboardEvents {
            lastKeyboardEvents = keyboard
            idle.keyboardActivity(pointerDisplay:displayUnderPointer(),at:ProcessInfo.processInfo.systemUptime)
        }
        var expired = enabled && !settingsVisible ? idle.expired(at: ProcessInfo.processInfo.systemUptime, after: Double(seconds)) : []
        if let pointer = displayUnderPointer() { expired.remove(pointer) }
        if settings.mode == "content" { expired = expired.filter { content.isReceiving($0) } }
        var changed = false
        for id in Array(shields.keys) where !expired.contains(id) && !forced.contains(id) {
            shields.removeValue(forKey:id)?.close(); changed = true
        }
        for id in expired where shields[id] == nil {
            guard let screen = screens[id] else { continue }
            cover(id,screen:screen)
            changed = true
        }
        if changed { onChange?() }
    }
}
