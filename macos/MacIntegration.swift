import AppKit
import ApplicationServices
import Carbon
import Combine
import SwiftUI

enum ConsolePage: String, CaseIterable, Identifiable {
    case brightness, displays, protection, layout, shortcuts, general, help
    var id: String { rawValue }
    var title: String {
        switch self {
        case .brightness: return "밝기"
        case .displays: return "모니터 환경"
        case .protection: return "화면 보호"
        case .layout: return "창 배치"
        case .shortcuts: return "단축키"
        case .general: return "일반"
        case .help: return "도움말"
        }
    }
    var symbol: String {
        switch self {
        case .brightness: return "sun.max"
        case .displays: return "display.2"
        case .protection: return "moon.zzz"
        case .layout: return "rectangle.split.2x2"
        case .shortcuts: return "keyboard"
        case .general: return "gearshape"
        case .help: return "questionmark.circle"
        }
    }
}

enum ShortcutAction: String, Codable, CaseIterable, Identifiable {
    case left, right, up, down, center, resize, dim, brighten, shadeFirst, shadeSecond, settings
    var id: String { rawValue }
    var title: String {
        switch self {
        case .left: return "왼쪽 배치"
        case .right: return "오른쪽 배치"
        case .up: return "위쪽 배치"
        case .down: return "아래쪽 배치"
        case .center: return "중앙 16:9"
        case .resize: return "크기 50 → 70 → 100%"
        case .dim: return "밝기 낮추기"
        case .brighten: return "밝기 높이기"
        case .shadeFirst: return "첫 번째 외부 화면 가리기"
        case .shadeSecond: return "두 번째 외부 화면 가리기"
        case .settings: return "설정 열기"
        }
    }
    var isWindowAction: Bool { [.left, .right, .up, .down, .center, .resize].contains(self) }
}

struct MacShortcut: Codable, Equatable, Identifiable {
    var action: ShortcutAction
    var key: UInt32
    var modifiers: UInt32
    var enabled = true
    var id: String { action.rawValue }
    static let modifierChoices: [(String, UInt32)] = [
        ("⌃ ⌥", UInt32(controlKey | optionKey)), ("⌃ ⌘", UInt32(controlKey | cmdKey)),
        ("⌥ ⌘", UInt32(optionKey | cmdKey)), ("⇧ ⌘", UInt32(shiftKey | cmdKey)),
        ("⌃ ⇧", UInt32(controlKey | shiftKey)), ("⌥", UInt32(optionKey)),
        ("⌃ ⌥ ⌘", UInt32(controlKey | optionKey | cmdKey)),
        ("⌃ ⇧ ⌥", UInt32(controlKey | shiftKey | optionKey))
    ]
    static let keyChoices: [(String, UInt32)] = [
        ("←",123),("→",124),("↑",126),("↓",125),("Delete ⌫",51),("Forward Delete ⌦",117),
        ("F1",122),("F2",120),("F3",99),("F4",118),("F5",96),("F6",97),
        ("F7",98),("F8",100),("F9",101),("F10",109),("F11",103),("F12",111),
        ("A",0),("B",11),("C",8),("D",2),("E",14),("F",3),("G",5),("H",4),
        ("I",34),("J",38),("K",40),("L",37),("M",46),("N",45),("O",31),("P",35),
        ("Q",12),("R",15),("S",1),("T",17),("U",32),("V",9),("W",13),("X",7),("Y",16),("Z",6)
    ]
    var label: String {
        let mods = [(controlKey,"⌃"),(optionKey,"⌥"),(shiftKey,"⇧"),(cmdKey,"⌘")]
            .filter { modifiers & UInt32($0.0) != 0 }.map { $0.1 }.joined()
        return mods + " " + (Self.keyChoices.first { $0.1 == key }?.0 ?? String(key))
    }
    static var defaults: [MacShortcut] {
        let window = UInt32(controlKey | optionKey)
        let mappings: [(ShortcutAction,UInt32,UInt32)] = [(.left,123,window),(.right,124,window),(.up,126,window),(.down,125,window),
                (.center,117,window),(.resize,51,window),(.dim,120,UInt32(optionKey)),
                (.brighten,99,UInt32(optionKey)),(.shadeFirst,101,window),(.shadeSecond,109,window),
                (.settings,103,UInt32(controlKey | optionKey | cmdKey))]
        return mappings.map { MacShortcut(action:$0.0,key:$0.1,modifiers:$0.2) }
    }
    static func validate(_ values: [MacShortcut]) throws {
        guard values.count == ShortcutAction.allCases.count,
              Set(values.map(\.action)).count == values.count else { throw DisplayError.message("단축키 목록을 확인하세요.") }
        let allowed = UInt32(controlKey | optionKey | cmdKey | shiftKey)
        var pairs = Set<String>()
        for value in values {
            guard value.modifiers & ~allowed == 0,
                  value.modifiers & UInt32(controlKey | optionKey | cmdKey) != 0,
                  keyChoices.contains(where:{$0.1 == value.key}) else { throw DisplayError.message("\(value.action.title)의 키 조합을 확인하세요.") }
            if value.enabled && !pairs.insert("\(value.modifiers):\(value.key)").inserted {
                throw DisplayError.message("같은 단축키를 두 동작에 지정할 수 없습니다.")
            }
        }
    }
}

@MainActor final class MacPreferences: ObservableObject {
    @Published var page = ConsolePage.brightness
    @Published var appearance: String { didSet { defaults.set(appearance,forKey:"consoleAppearance") } }
    @Published var blur: Bool { didSet { defaults.set(blur,forKey:"consoleBlur") } }
    @Published var startHidden: Bool { didSet { defaults.set(startHidden,forKey:"consoleStartHidden") } }
    @Published var centerPercent: Int { didSet { defaults.set(centerPercent,forKey:"consoleCenterPercent") } }
    @Published var brightnessStep: Int { didSet { defaults.set(brightnessStep,forKey:"consoleBrightnessStep") } }
    @Published var shortcuts: [MacShortcut]
    @Published var shortcutError = ""
    @Published var accessMessage = ""
    private let defaults: UserDefaults
    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        appearance = defaults.string(forKey:"consoleAppearance") ?? "auto"
        blur = defaults.object(forKey:"consoleBlur") as? Bool ?? true
        startHidden = defaults.object(forKey:"consoleStartHidden") as? Bool ?? true
        centerPercent = min(95,max(30,defaults.object(forKey:"consoleCenterPercent") as? Int ?? 65))
        brightnessStep = min(20,max(1,defaults.object(forKey:"consoleBrightnessStep") as? Int ?? 5))
        if let data = defaults.data(forKey:"consoleShortcuts"), let loaded = try? JSONDecoder().decode([MacShortcut].self,from:data), (try? MacShortcut.validate(loaded)) != nil {
            shortcuts = loaded
        } else { shortcuts = MacShortcut.defaults }
    }
    func saveShortcuts(_ values: [MacShortcut]) throws {
        try MacShortcut.validate(values)
        let data = try JSONEncoder().encode(values)
        defaults.set(data,forKey:"consoleShortcuts"); shortcuts = values
    }
}

struct WindowPlacement {
    static func rectangle(action: ShortcutAction, stage: Int, area: CGRect, centerPercent: Int) -> CGRect {
        let full = area
        let ratio = stage == 0 ? 0.5 : 2.0/3.0
        switch action {
        case .left, .right:
            if stage >= 2 { return full }
            let width = (area.width * ratio).rounded()
            return CGRect(x:action == .left ? area.minX : area.maxX-width,y:area.minY,width:width,height:area.height)
        case .up, .down:
            if stage >= 2 { return full }
            let height = (area.height * ratio).rounded()
            return CGRect(x:area.minX,y:action == .up ? area.minY : area.maxY-height,width:area.width,height:height)
        case .center:
            if stage >= 1 { return full }
            var width = area.width * CGFloat(min(95,max(30,centerPercent))) / 100
            var height = width * 9/16
            if height > area.height { height = area.height; width = height * 16/9 }
            return CGRect(x:area.midX-width/2,y:area.midY-height/2,width:width,height:height).integral
        case .resize:
            let amount: CGFloat = stage == 0 ? 0.5 : stage == 1 ? 0.7 : 1
            let width = (area.width*amount).rounded(), height = (area.height*amount).rounded()
            return CGRect(x:(area.midX-width/2).rounded(),y:(area.midY-height/2).rounded(),width:width,height:height)
        default: return full
        }
    }
    static func close(_ a: CGRect, _ b: CGRect) -> Bool {
        abs(a.minX-b.minX) <= 4 && abs(a.minY-b.minY) <= 4 && abs(a.width-b.width) <= 4 && abs(a.height-b.height) <= 4
    }
}

@MainActor final class WindowArranger {
    private var lastWindow: AXUIElement?
    private var lastAction: ShortcutAction?
    private var lastRectangle = CGRect.zero
    private var stage = 0
    func apply(_ action: ShortcutAction, centerPercent: Int) throws {
        guard AXIsProcessTrusted() else { throw DisplayError.message("일반 → 손쉬운 사용 권한에서 Monitor Shade를 허용하세요.") }
        guard let app = NSWorkspace.shared.frontmostApplication, app.processIdentifier != ProcessInfo.processInfo.processIdentifier else { return }
        let element = AXUIElementCreateApplication(app.processIdentifier)
        AXUIElementSetMessagingTimeout(element,0.3)
        var focused: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element,kAXFocusedWindowAttribute as CFString,&focused) == .success, let focused,
              CFGetTypeID(focused) == AXUIElementGetTypeID() else { throw DisplayError.message("배치할 작업 창을 선택하세요.") }
        let window = focused as! AXUIElement
        var fullscreen: CFTypeRef?
        if AXUIElementCopyAttributeValue(window,"AXFullScreen" as CFString,&fullscreen) == .success, fullscreen as? Bool == true {
            throw DisplayError.message("macOS 전체 화면에서 나온 뒤 창을 배치하세요.")
        }
        let current = try rectangle(window)
        let top = NSScreen.screens.first?.frame.maxY ?? 0
        let areas = NSScreen.screens.map { screen -> CGRect in
            let frame = screen.visibleFrame
            return CGRect(x:frame.minX,y:top-frame.maxY,width:frame.width,height:frame.height)
        }
        guard let area = areas.max(by:{ intersection($0,current) < intersection($1,current) }) else { return }
        let continuation = lastWindow.map { CFEqual($0,window) } == true && lastAction == action && WindowPlacement.close(current,lastRectangle)
        stage = continuation ? (stage+1) % (action == .center ? 2 : 3) : 0
        let desired = WindowPlacement.rectangle(action:action,stage:stage,area:area,centerPercent:centerPercent)
        do {
            try set(window,rect:desired)
            lastWindow = window; lastAction = action; lastRectangle = try rectangle(window)
            if !WindowPlacement.close(lastRectangle,desired) {
                lastAction = nil
                throw DisplayError.message("이 앱의 최소 창 크기 또는 크기 조절 제한이 적용됐습니다.")
            }
        } catch {
            try? set(window,rect:current)
            lastAction = nil
            throw error
        }
    }
    private func intersection(_ a: CGRect, _ b: CGRect) -> CGFloat { let r = a.intersection(b); return r.isNull ? 0 : r.width*r.height }
    private func rectangle(_ window: AXUIElement) throws -> CGRect {
        var p: CFTypeRef?, s: CFTypeRef?
        guard AXUIElementCopyAttributeValue(window,kAXPositionAttribute as CFString,&p) == .success,
              AXUIElementCopyAttributeValue(window,kAXSizeAttribute as CFString,&s) == .success,
              let p, let s, CFGetTypeID(p) == AXValueGetTypeID(), CFGetTypeID(s) == AXValueGetTypeID() else {
            throw DisplayError.message("작업 창의 위치와 크기를 읽을 수 없습니다.")
        }
        var position = CGPoint.zero, size = CGSize.zero
        guard AXValueGetValue(p as! AXValue,.cgPoint,&position), AXValueGetValue(s as! AXValue,.cgSize,&size) else {
            throw DisplayError.message("작업 창 정보를 확인하세요.")
        }
        return CGRect(origin:position,size:size)
    }
    private func set(_ window: AXUIElement, rect: CGRect) throws {
        var origin = rect.origin, size = rect.size
        guard let p = AXValueCreate(.cgPoint,&origin), let s = AXValueCreate(.cgSize,&size) else { return }
        guard AXUIElementSetAttributeValue(window,kAXSizeAttribute as CFString,s) == .success,
              AXUIElementSetAttributeValue(window,kAXPositionAttribute as CFString,p) == .success else {
            throw DisplayError.message("이 창의 위치 또는 크기를 변경할 수 없습니다.")
        }
        _ = AXUIElementSetAttributeValue(window,kAXSizeAttribute as CFString,s)
    }
}

@MainActor final class GlobalShortcuts {
    var onAction: ((ShortcutAction) -> Void)?
    private var refs: [EventHotKeyRef] = []
    private var actions: [UInt32:ShortcutAction] = [:]
    private var handler: EventHandlerRef?
    private var active: [MacShortcut] = []
    init() {
        var type = EventTypeSpec(eventClass:OSType(kEventClassKeyboard),eventKind:UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, context -> OSStatus in
            guard let event, let context else { return OSStatus(eventNotHandledErr) }
            var identifier = EventHotKeyID()
            let result = GetEventParameter(event,EventParamName(kEventParamDirectObject),EventParamType(typeEventHotKeyID),nil,MemoryLayout<EventHotKeyID>.size,nil,&identifier)
            guard result == noErr, identifier.signature == 0x4d534844 else { return OSStatus(eventNotHandledErr) }
            let keys = Unmanaged<GlobalShortcuts>.fromOpaque(context).takeUnretainedValue()
            let id = identifier.id
            DispatchQueue.main.async { if let action = keys.actions[id] { keys.onAction?(action) } }
            return noErr
        },1,&type,Unmanaged.passUnretained(self).toOpaque(),&handler)
    }
    func replace(_ values: [MacShortcut]) throws {
        try MacShortcut.validate(values)
        let before = active
        removeKeys()
        do { try register(values); active = values }
        catch { removeKeys(); try? register(before); active = before; throw error }
    }
    private func register(_ values: [MacShortcut]) throws {
        for (index,value) in values.enumerated() where value.enabled {
            let id = UInt32(index+1)
            var ref: EventHotKeyRef?
            let result = RegisterEventHotKey(value.key,value.modifiers,EventHotKeyID(signature:0x4d534844,id:id),GetApplicationEventTarget(),0,&ref)
            guard result == noErr, let ref else { throw DisplayError.message("\(value.action.title): \(value.label) 조합을 사용할 수 없습니다 (\(result)). 다른 조합을 지정하세요.") }
            refs.append(ref); actions[id] = value.action
        }
    }
    private func removeKeys() { refs.forEach { UnregisterEventHotKey($0) }; refs.removeAll(); actions.removeAll() }
    func stop() { removeKeys(); if let handler { RemoveEventHandler(handler) }; handler = nil }
}

@MainActor final class MacIntegration: ObservableObject {
    let store: MonitorStore
    let shortcuts = GlobalShortcuts()
    let arranger = WindowArranger()
    var showSettings: (() -> Void)?
    private var pendingBrightness = 0
    private var stepTask: Task<Void,Never>?
    init(store: MonitorStore) { self.store = store }
    func start() {
        guard !store.previewMode else { return }
        shortcuts.onAction = { [weak self] action in self?.perform(action) }
        do { try shortcuts.replace(store.preferences.shortcuts) }
        catch { store.preferences.shortcutError = error.localizedDescription }
    }
    func save(_ values: [MacShortcut]) {
        do {
            if !store.previewMode { try shortcuts.replace(values) }
            try store.preferences.saveShortcuts(values)
            store.preferences.shortcutError = ""
        } catch { store.preferences.shortcutError = error.localizedDescription }
    }
    func perform(_ action: ShortcutAction) {
        do {
            if action.isWindowAction {
                try arranger.apply(action,centerPercent:store.preferences.centerPercent)
            } else if action == .settings { showSettings?() }
            else if action == .dim || action == .brighten { stepBrightness(action == .dim ? -1 : 1) }
            else {
                let screens = store.externalMonitors.sorted { ($0.x,$0.y) < ($1.x,$1.y) }
                let index = action == .shadeFirst ? 0 : 1
                guard screens.indices.contains(index) else { throw DisplayError.message("가릴 외부 모니터가 연결되어 있지 않습니다.") }
                store.oled.toggleForced(screens[index].displayID)
            }
            store.preferences.accessMessage = ""
        } catch { store.preferences.accessMessage = error.localizedDescription; store.reportError(error.localizedDescription) }
    }
    func stepBrightness(_ direction: Int) {
        pendingBrightness = max(-20,min(20,pendingBrightness + direction))
        guard stepTask == nil else { return }
        stepTask = Task { [weak self] in
            guard let self else { return }
            while !Task.isCancelled && pendingBrightness != 0 {
                if store.busy || store.brightnessBusy || store.brightnessReading {
                    try? await Task.sleep(for:.milliseconds(100)); continue
                }
                let amount = pendingBrightness * store.preferences.brightnessStep; pendingBrightness = 0
                let targets = store.brightnessTargets
                let values = targets.compactMap { self.store.brightness[$0.uuid]?.percent }
                if !values.isEmpty {
                    let anchor = amount > 0 ? values.max()! : values.min()!
                    if store.linkedBrightness {
                        store.groupPercent = min(100,max(0,anchor + Double(amount)))
                        store.changeGroupBrightness()
                    } else if let monitor = targets.first(where:{ item in
                        NSScreen.screens.contains { screen in
                            (screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value == item.displayID && screen.frame.contains(NSEvent.mouseLocation)
                        }
                    }), let level = store.brightness[monitor.uuid]?.percent {
                        store.changeBrightness(monitor,percent:min(100,max(0,Int(level.rounded())+amount)))
                    }
                }
                try? await Task.sleep(for:.milliseconds(120))
            }
            stepTask = nil
        }
    }
    func requestAccessibility() {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        _ = AXIsProcessTrustedWithOptions([key:true] as CFDictionary)
        NSWorkspace.shared.open(URL(string:"x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!)
    }
    func stop() { stepTask?.cancel(); shortcuts.stop() }
}
