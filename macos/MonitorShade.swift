import AppKit
import SwiftUI
import CoreGraphics
import ServiceManagement

struct Monitor: Codable, Identifiable, Equatable {
    var id: String { uuid }
    var uuid: String
    var name: String
    var vendor: UInt32
    var model: UInt32
    var serial: UInt32
    var alphaSerial: String
    var edidUUID: String
    var location: String
    var builtIn: Bool
    var width: Int
    var height: Int
    var pixelWidth: Int
    var pixelHeight: Int
    var refreshRate: Double
    var modeID: Int32
    var x: Int
    var y: Int
    var rotation: Int
    var mirrorUUID: String?
    var brightness: Int?
    var displayID: UInt32
    var isMain = false

    var resolution: String { "\(width) × \(height)" }
    var hz: String { refreshRate > 0 ? String(format: "%.2f", refreshRate).replacingOccurrences(of: #"\.?0+$"#, with: "", options: .regularExpression) + " Hz" : "가변 주사율" }
    var identityLabel: String { alphaSerial.isEmpty ? uuid : alphaSerial }
    func label(in monitors: [Monitor]) -> String {
        if isMain { return "주 모니터" }
        if builtIn { return "맥북 화면" }
        let external = monitors.filter { !$0.isMain && !$0.builtIn }.sorted {
            ($0.x,$0.y,$0.alphaSerial,$0.uuid) < ($1.x,$1.y,$1.alphaSerial,$1.uuid)
        }
        let index = external.firstIndex(where:{$0.uuid == uuid}) ?? 0
        let letters = Array("ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        return "외부 모니터 " + (index < letters.count ? String(letters[index]) : String(index+1))
    }
    func sameHardware(as other: Monitor) -> Bool {
        guard vendor == other.vendor && model == other.model else { return false }
        if !alphaSerial.isEmpty && !other.alphaSerial.isEmpty { return alphaSerial == other.alphaSerial }
        return uuid == other.uuid
    }
    func matching(_ candidates: [Monitor]) -> Monitor? {
        let modelMatches = candidates.filter { $0.vendor == vendor && $0.model == model && $0.builtIn == builtIn }
        if !alphaSerial.isEmpty {
            let strong = modelMatches.filter { $0.alphaSerial == alphaSerial }
            if strong.count == 1 { return strong[0] }
            if strong.count > 1 { return nil }
        }
        let compatible = modelMatches.filter { alphaSerial.isEmpty || $0.alphaSerial.isEmpty || $0.alphaSerial == alphaSerial }
        let exact = compatible.filter { $0.uuid == uuid }
        if exact.count == 1 { return exact[0] }
        // Placeholder serials are common on identical ASUS monitors; never guess their order.
        if serial != 0 && serial != 0x01010101 {
            let serials = compatible.filter { $0.serial == serial }
            if serials.count == 1 { return serials[0] }
        }
        return nil
    }
    func modeMatches(_ mode: CGDisplayMode) -> Bool {
        width == mode.width && height == mode.height && pixelWidth == mode.pixelWidth &&
        pixelHeight == mode.pixelHeight && abs(refreshRate - mode.refreshRate) < 0.6
    }
}
extension Monitor {
    enum CodingKeys: String, CodingKey { case uuid, name, vendor, model, serial, alphaSerial, edidUUID, location, builtIn, width, height, pixelWidth, pixelHeight, refreshRate, modeID, x, y, rotation, mirrorUUID, brightness, displayID, isMain }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy:CodingKeys.self)
        uuid = try c.decode(String.self,forKey:.uuid)
        name = try c.decode(String.self,forKey:.name)
        vendor = try c.decode(UInt32.self,forKey:.vendor)
        model = try c.decode(UInt32.self,forKey:.model)
        serial = try c.decode(UInt32.self,forKey:.serial)
        alphaSerial = try c.decode(String.self,forKey:.alphaSerial)
        edidUUID = try c.decode(String.self,forKey:.edidUUID)
        location = try c.decode(String.self,forKey:.location)
        builtIn = try c.decode(Bool.self,forKey:.builtIn)
        width = try c.decode(Int.self,forKey:.width)
        height = try c.decode(Int.self,forKey:.height)
        pixelWidth = try c.decode(Int.self,forKey:.pixelWidth)
        pixelHeight = try c.decode(Int.self,forKey:.pixelHeight)
        refreshRate = try c.decode(Double.self,forKey:.refreshRate)
        modeID = try c.decode(Int32.self,forKey:.modeID)
        x = try c.decode(Int.self,forKey:.x)
        y = try c.decode(Int.self,forKey:.y)
        rotation = try c.decode(Int.self,forKey:.rotation)
        mirrorUUID = try c.decodeIfPresent(String.self,forKey:.mirrorUUID)
        brightness = try c.decodeIfPresent(Int.self,forKey:.brightness)
        displayID = try c.decode(UInt32.self,forKey:.displayID)
        isMain = try c.decodeIfPresent(Bool.self,forKey:.isMain) ?? (x == 0 && y == 0 && mirrorUUID == nil)
    }
}
struct Profile: Codable, Identifiable {
    var id = UUID().uuidString
    var name = "기존 환경"
    var version = 1
    var savedAt = Date()
    var monitors: [Monitor]
    var screenProtection = ScreenProtectionSettings()
    func validate() throws {
        guard version == 1, (1...3600).contains(screenProtection.seconds), ["input","content"].contains(screenProtection.mode),
              screenProtection.selectedUUIDs.map({ $0.count <= 32 && Set($0).count == $0.count && $0.allSatisfy { UUID(uuidString:$0) != nil } }) ?? true,
              screenProtection.selectedUUIDs.map({ selected in selected.allSatisfy { id in monitors.contains { $0.uuid == id } } }) ?? true,
              UUID(uuidString:id) != nil,
              !name.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty, name.count <= 80,
              !monitors.isEmpty, monitors.count <= 32,
              monitors.filter(\.isMain).count == 1,
              monitors.filter(\.isMain).allSatisfy({$0.x == 0 && $0.y == 0 && $0.mirrorUUID == nil}),
              Set(monitors.map(\.uuid)).count == monitors.count,
              monitors.allSatisfy({ UUID(uuidString:$0.uuid) != nil && [0,90,180,270].contains($0.rotation) &&
                  (1...32768).contains($0.width) && (1...32768).contains($0.height) &&
                  (1...65536).contains($0.pixelWidth) && (1...65536).contains($0.pixelHeight) &&
                  (-100000...100000).contains($0.x) && (-100000...100000).contains($0.y) && $0.refreshRate.isFinite &&
                  (0...1000).contains($0.refreshRate) && ($0.brightness == nil || (0...100).contains($0.brightness!)) })
        else { throw DisplayError.message("저장된 프로필 형식이 올바르지 않습니다.") }
    }
}
extension Profile {
    enum CodingKeys: String, CodingKey { case id, name, version, savedAt, monitors, screenProtection }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy:CodingKeys.self)
        id = try c.decodeIfPresent(String.self,forKey:.id) ?? UUID().uuidString
        name = try c.decodeIfPresent(String.self,forKey:.name) ?? "기존 환경"
        version = try c.decode(Int.self,forKey:.version)
        savedAt = try c.decode(Date.self,forKey:.savedAt)
        monitors = try c.decode([Monitor].self,forKey:.monitors)
        screenProtection = try c.decodeIfPresent(ScreenProtectionSettings.self,forKey:.screenProtection) ?? ScreenProtectionSettings()
    }
    func matches(_ current: [Monitor]) -> Bool { (try? resolveMonitors(monitors,in:current)) != nil }
}
struct ProfileLibrary: Codable {
    var version = 2
    var profiles: [Profile]
    func validate() throws {
        guard (1...2).contains(version), profiles.count <= 100,
              Set(profiles.map(\.id)).count == profiles.count,
              Set(profiles.map { $0.name.lowercased() }).count == profiles.count else {
            throw DisplayError.message("저장 환경 목록이 올바르지 않습니다.")
        }
        for p in profiles { try p.validate() }
    }
    func matching(_ current: [Monitor]) -> Profile? {
        profiles.filter { $0.matches(current) }.max { ($0.savedAt,$0.id) < ($1.savedAt,$1.id) }
    }
    func removing(_ id: String) -> ProfileLibrary {
        ProfileLibrary(profiles:profiles.filter { $0.id != id })
    }
}
func monitorsInRoleOrder(_ monitors: [Monitor]) -> [Monitor] {
    monitors.sorted {
        if $0.isMain != $1.isMain { return $0.isMain }
        return ($0.x,$0.y,$0.uuid) < ($1.x,$1.y,$1.uuid)
    }
}
func resolveMonitors(_ saved: [Monitor], in current: [Monitor]) throws -> [(Monitor,Monitor)] {
    guard saved.count == current.count else {
        throw DisplayError.message("저장된 구성(\(saved.count)대)과 현재 연결(\(current.count)대)이 다릅니다.")
    }
    var used = Set<String>()
    return try saved.map { wanted in
        guard let actual = wanted.matching(current), used.insert(actual.uuid).inserted else {
            throw DisplayError.message("\(wanted.name) · \(wanted.identityLabel) 연결을 확인하세요.")
        }
        return (wanted,actual)
    }
}
func makingPrimary(_ id: String, in monitors: [Monitor]) throws -> [Monitor] {
    guard let main = monitors.first(where:{$0.uuid == id}), main.mirrorUUID == nil else {
        throw DisplayError.message("주 모니터로 사용할 화면을 확인하세요.")
    }
    return monitors.map { monitor in
        var updated = monitor
        updated.x -= main.x; updated.y -= main.y
        updated.isMain = updated.uuid == main.uuid
        return updated
    }
}
func brightnessRawValue(percent: Int, maximum: Int) -> Int {
    Int((Double(percent)*Double(maximum)/100).rounded())
}
func brightnessCapableMonitors(_ monitors: [Monitor], states: [String:BrightnessState]) -> [Monitor] {
    monitors.filter { states[$0.uuid]?.supported == true }
}
func resolvedProtection(_ profile: Profile, current: [Monitor]) -> ScreenProtectionSettings {
    var settings = profile.screenProtection
    if let selected = settings.selectedUUIDs, let pairs = try? resolveMonitors(profile.monitors,in:current) {
        settings.selectedUUIDs = pairs.filter { selected.contains($0.0.uuid) }.map { $0.1.uuid }.sorted()
    }
    return settings
}
enum DisplayError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case .message(let s) = self { return s }; return nil }
}

@MainActor
func connectedMonitors() -> [Monitor] {
    var ids = [CGDirectDisplayID](repeating:0,count:32)
    var count: UInt32 = 0
    guard CGGetOnlineDisplayList(32, &ids, &count) == .success else { return [] }
    return ids.prefix(Int(count)).compactMap { id -> Monitor? in
        guard let mode = CGDisplayCopyDisplayMode(id), let ref = CGDisplayCreateUUIDFromDisplayID(id) else { return nil }
        let uuid = CFUUIDCreateString(nil, ref.takeRetainedValue())! as String
        let metadata = DMDisplayMetadata(id)
        let screen = NSScreen.screens.first { ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value == id }
        let bounds = CGDisplayBounds(id)
        let mirror = CGDisplayMirrorsDisplay(id)
        let mirrorUUID = mirror != 0 ? CGDisplayCreateUUIDFromDisplayID(mirror).map { CFUUIDCreateString(nil,$0.takeRetainedValue())! as String } : nil
        return Monitor(uuid:uuid, name:screen?.localizedName ?? (metadata["productName"] as? String ?? "외부 모니터"),
            vendor:CGDisplayVendorNumber(id), model:CGDisplayModelNumber(id), serial:CGDisplaySerialNumber(id),
            alphaSerial:metadata["alphaSerial"] as? String ?? "", edidUUID:metadata["edidUUID"] as? String ?? "",
            location:metadata["location"] as? String ?? "", builtIn:CGDisplayIsBuiltin(id) != 0,
            width:mode.width, height:mode.height, pixelWidth:mode.pixelWidth, pixelHeight:mode.pixelHeight,
            refreshRate:mode.refreshRate, modeID:mode.ioDisplayModeID, x:Int(bounds.origin.x), y:Int(bounds.origin.y),
            rotation:Int(CGDisplayRotation(id).rounded()), mirrorUUID:mirrorUUID, brightness:nil, displayID:id,isMain:CGDisplayIsMain(id) != 0)
    }.sorted { ($0.x,$0.y,$0.uuid) < ($1.x,$1.y,$1.uuid) }
}

struct BrightnessState {
    var percent: Double = 50
    var maximum = 100
    var supported = false
    var busy = true
    var message = "밝기 확인 중…"
}

@MainActor
final class MonitorStore: ObservableObject {
    let oled = OLEDProtection()
    let preferences: MacPreferences
    let previewMode: Bool
    @Published var monitors: [Monitor] = []
    @Published var profiles: [Profile] = []
    @Published var selectedProfileID = ""
    @Published var showSaveDialog = false
    @Published var showSettings = false
    @Published var deletionTarget: Profile?
    @Published var saveName = ""
    @Published var saveError = ""
    @Published var unsavedScreenProtection = ScreenProtectionSettings()
    @Published var linkedBrightness = UserDefaults.standard.object(forKey:"linkedBrightness") as? Bool ?? true {
        didSet { if !previewMode { UserDefaults.standard.set(linkedBrightness,forKey:"linkedBrightness") }; updateGroupLevel() }
    }
    @Published var groupPercent: Double = 50
    @Published var brightnessBusy = false
    var profile: Profile? { profiles.first { $0.id == selectedProfileID } }
    var library: ProfileLibrary { ProfileLibrary(profiles:profiles) }
    var screenProtectionProfile: Profile? {
        if let profile, profile.matches(monitors) { return profile }
        return library.matching(monitors)
    }
    var screenProtectionSettings: ScreenProtectionSettings {
        screenProtectionProfile.map { resolvedProtection($0,current:monitors) } ?? unsavedScreenProtection
    }
    func setScreenProtection(_ desired: ScreenProtectionSettings) {
        var settings = desired; settings.seconds = ScreenProtectionSettings.validSeconds(settings.seconds)
        guard settings != screenProtectionSettings else { return }
        if let target = screenProtectionProfile, let index = profiles.firstIndex(where:{$0.id == target.id}) {
            do {
                var persisted = settings
                if let selection = settings.selectedUUIDs, let pairs = try? resolveMonitors(target.monitors,in:monitors) {
                    persisted.selectedUUIDs = pairs.filter { selection.contains($0.1.uuid) }.map { $0.0.uuid }.sorted()
                }
                var updated = profiles; updated[index].screenProtection = persisted
                try persist(updated); profiles = updated
                if target.matches(monitors) { oled.configure(settings) }
            } catch { reportError("화면 보호 설정 저장 실패: \(error.localizedDescription)") }
        } else {
            unsavedScreenProtection = settings; oled.configure(settings)
        }
        writeDiagnostics()
    }
    var externalMonitors: [Monitor] { monitors.filter { !$0.builtIn } }
    var brightnessTargets: [Monitor] { brightnessCapableMonitors(monitors,states:brightness) }
    var brightnessReading: Bool { monitors.contains { brightness[$0.uuid]?.busy != false } }

    @Published var brightness: [String:BrightnessState] = [:]
    @Published var status = "현재 모니터를 확인하고 있습니다."
    @Published var errorMessage: String?
    func reportError(_ message: String) { status = message; errorMessage = message }
    @Published var busy = false
    @Published var autoRestore: Bool = UserDefaults.standard.object(forKey:"autoRestore") as? Bool ?? true {
        didSet { UserDefaults.standard.set(autoRestore,forKey:"autoRestore"); if autoRestore { scheduleRestore() } else { pending?.cancel() } }
    }
    @Published var loginEnabled = false
    @Published var loginMessage = ""
    let directory: URL
    var profileURL: URL { directory.appendingPathComponent("profiles.json") }
    private let brightnessQueue = DispatchQueue(label:"haus.grey.MonitorShade.brightness",qos:.userInitiated)
    private var pending: Task<Void,Never>?
    private var topology = ""
    private var observers: [NSObjectProtocol] = []
    private var initializing = true
    private var profileLoadFailed = false

    init(preview: Bool = false) {
        previewMode = preview
        preferences = MacPreferences(defaults:preview ? UserDefaults(suiteName:"MonitorShade.preview.\(UUID().uuidString)")! : .standard)
        // Keep the existing storage location and bundle identifier so saved environments and preferences survive the rename.
        directory = preview ? FileManager.default.temporaryDirectory.appendingPathComponent("MonitorShade-preview-\(UUID().uuidString)")
            : FileManager.default.urls(for:.applicationSupportDirectory,in:.userDomainMask)[0].appendingPathComponent("DockMonitor",isDirectory:true)
        if preview { return }
        do {
            try FileManager.default.createDirectory(at:directory,withIntermediateDirectories:true)
            if FileManager.default.fileExists(atPath:profileURL.path) {
                let saved = try JSONDecoder().decode(ProfileLibrary.self,from:Data(contentsOf:profileURL))
                try saved.validate(); profiles = saved.profiles
                if saved.version < 2 { try persist(profiles) }
            } else {
                let legacy = directory.appendingPathComponent("profile.json")
                if FileManager.default.fileExists(atPath:legacy.path) {
                    let old = try JSONDecoder().decode(Profile.self,from:Data(contentsOf:legacy))
                    try old.validate()
                    try persist([old]); profiles = [old]
                }
            }
        } catch { profileLoadFailed = true; reportError("프로필을 읽지 못했습니다: \(error.localizedDescription)") }
    }
    func start() {
        if previewMode { MacVerification.populate(self); return }
        oled.onChange = { [weak self] in self?.objectWillChange.send(); self?.writeDiagnostics() }
        oled.start()
        refresh()
        detectEnvironment()
        topology = topologyKey(monitors)
        let callback: CGDisplayReconfigurationCallBack = { _,flags,context in
            guard !flags.contains(.beginConfigurationFlag), let context else { return }
            let store = Unmanaged<MonitorStore>.fromOpaque(context).takeUnretainedValue()
            DispatchQueue.main.async { store.displayChanged() }
        }
        CGDisplayRegisterReconfigurationCallback(callback,Unmanaged.passUnretained(self).toOpaque())
        for name in [NSWorkspace.didWakeNotification, NSWorkspace.screensDidWakeNotification] {
            observers.append(NSWorkspace.shared.notificationCenter.addObserver(forName:name,object:nil,queue:.main) { [weak self] _ in
                Task { @MainActor in self?.scheduleRestore() }
            })
        }
        loginEnabled = SMAppService.mainApp.status == .enabled
        if !UserDefaults.standard.bool(forKey:"loginAttempted") {
            UserDefaults.standard.set(true,forKey:"loginAttempted")
            setLogin(true)
        }
        Task {
            await readBrightness()
            initializing = false
            if autoRestore && !profiles.isEmpty { scheduleRestore() }
            else if !profileLoadFailed { status = "현재 모니터 환경에 이름을 정해 저장하세요." }
            writeDiagnostics()
        }
    }
    func topologyKey(_ displays: [Monitor]) -> String {
        displays.map { "\($0.uuid):\($0.alphaSerial)" }.sorted().joined(separator:"|")
    }
    func displayChanged() {
        guard !busy else { return }
        refresh()
        let key = topologyKey(monitors)
        if key != topology {
            topology = key
            detectEnvironment()
            scheduleRestore()
            Task { await readBrightness() }
        }
    }
    func refresh() {
        if previewMode { return }
        monitors = connectedMonitors()
        if monitors.isEmpty { status = "연결된 모니터를 찾지 못했습니다. 화면이 켜진 상태에서 다시 시도하세요." }
    }
    func reload() {
        guard !busy, !brightnessBusy else { return }
        errorMessage = nil
        refresh(); Task { await readBrightness(); writeDiagnostics() }
    }
    func setLogin(_ enabled: Bool) {
        if previewMode { return }
        do {
            if enabled { try SMAppService.mainApp.register() }
            else { try SMAppService.mainApp.unregister() }
            loginEnabled = SMAppService.mainApp.status == .enabled
            loginMessage = SMAppService.mainApp.status == .requiresApproval ? "시스템 설정 → 일반 → 로그인 항목에서 허용하세요." : ""
        } catch {
            loginEnabled = SMAppService.mainApp.status == .enabled
            loginMessage = "로그인 실행 등록: \(error.localizedDescription)"
        }
    }
    func detectEnvironment() {
        if let matched = library.matching(monitors) {
            selectedProfileID = matched.id
            oled.configure(resolvedProtection(matched,current:monitors))
            if !profileLoadFailed { status = "‘\(matched.name)’ 환경을 인식했습니다." }
        } else {
            selectedProfileID = ""
            unsavedScreenProtection = ScreenProtectionSettings()
            oled.configure(ScreenProtectionSettings())
            if !profileLoadFailed { status = "저장되지 않은 모니터 조합입니다. 이름을 정해 목록에 추가하세요." }
        }
    }
    func selectProfile(_ id: String) { selectedProfileID = id }
    func requestDelete() {
        guard !busy, !brightnessBusy, let selected = profile else { return }
        pending?.cancel()
        deletionTarget = selected
    }
    func deleteProfile(_ target: Profile) {
        guard !busy, !brightnessBusy, profiles.contains(where:{$0.id == target.id}) else { return }
        pending?.cancel()
        do {
            let deleted = directory.appendingPathComponent("Deleted Profiles",isDirectory:true)
            try FileManager.default.createDirectory(at:deleted,withIntermediateDirectories:true)
            let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted,.sortedKeys]
            try encoder.encode(target).write(to:deleted.appendingPathComponent("\(target.id)-\(UUID().uuidString).json"),options:.atomic)
            let updated = library.removing(target.id)
            try persist(updated.profiles)
            profiles = updated.profiles
            if selectedProfileID == target.id { selectedProfileID = updated.matching(monitors)?.id ?? "" }
            oled.configure(updated.matching(monitors).map { resolvedProtection($0,current:monitors) } ?? ScreenProtectionSettings())
            status = "‘\(target.name)’ 환경을 목록에서 삭제했습니다. 삭제한 설정은 저장 폴더에 백업했습니다."
        } catch { reportError("환경을 삭제하지 못했습니다: \(error.localizedDescription)") }
        deletionTarget = nil
        writeDiagnostics()
    }
    func requestSave(asNew: Bool = false) {
        guard !busy, !brightnessBusy else { return }
        pending?.cancel()
        saveName = asNew ? "" : (profile?.matches(monitors) == true ? profile!.name : "")
        saveError = ""; showSaveDialog = true
    }
    func saveCurrent() { requestSave() }
    func commitSave() {
        guard !busy, !brightnessBusy else { return }
        refresh()
        let name = saveName.trimmingCharacters(in:.whitespacesAndNewlines)
        guard !name.isEmpty, name.count <= 80 else { saveError = "환경 이름을 1~80자로 입력하세요."; return }
        guard !monitors.isEmpty else { saveError = "연결된 모니터가 없습니다."; return }
        let existing = profiles.first { $0.name.caseInsensitiveCompare(name) == .orderedSame }
        if let existing, !existing.matches(monitors) {
            saveError = "다른 모니터 조합에 사용한 이름입니다. 새 이름으로 저장하세요."; return
        }
        let values = monitors.map { monitor -> Monitor in
            var item = monitor
            item.brightness = library.matching(monitors)?.monitors.first(where:{$0.sameHardware(as:item)})?.brightness
            if let value = brightness[item.uuid], value.supported && !value.busy { item.brightness = Int(value.percent.rounded()) }
            return item
        }
        do {
            var p = Profile(name:name,monitors:values)
            p.screenProtection = oled.settings
            if let existing { p.id = existing.id }
            var updated = profiles.filter { $0.id != p.id }; updated.append(p)
            try persist(updated); profiles = updated; selectedProfileID = p.id
            oled.configure(p.screenProtection)
            showSaveDialog = false
            status = "‘\(p.name)’ 환경을 저장했습니다. 이 모니터 조합을 다시 연결하면 자동 복구합니다."
        } catch { saveError = "저장 실패: \(error.localizedDescription)" }
        writeDiagnostics()
    }
    func persist(_ saved: [Profile]) throws {
        let list = ProfileLibrary(profiles:saved); try list.validate()
        if previewMode { return }
        let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted,.sortedKeys]
        let data = try encoder.encode(list)
        if FileManager.default.fileExists(atPath:profileURL.path) {
            let previous = try Data(contentsOf:profileURL)
            try previous.write(to:directory.appendingPathComponent("profiles.previous.json"),options:.atomic)
        }
        try data.write(to:profileURL,options:.atomic)
    }
    func resolve(_ saved: [Monitor], in current: [Monitor]) throws -> [(Monitor,Monitor)] {
        try resolveMonitors(saved,in:current)
    }
    func scheduleRestore() {
        pending?.cancel()
        guard autoRestore, !profiles.isEmpty, !initializing, !showSaveDialog, deletionTarget == nil else { return }
        pending = Task { [weak self] in
            for delay in [2.5,3.0,5.0,8.0] {
                do { try await Task.sleep(for:.seconds(delay)) } catch { return }
                guard let self, !Task.isCancelled, autoRestore else { return }
                if busy || brightnessBusy || brightnessReading { continue }
                refresh()
                guard let saved = library.matching(monitors) else {
                    status = "일치하는 저장 환경을 기다리고 있습니다. 처음 연결한 조합이면 새로 저장하세요."
                    continue
                }
                selectedProfileID = saved.id
                if await restore() { return }
            }
        }
    }
    func restoreNow() { pending?.cancel(); Task { _ = await restore() } }
    func pause(_ seconds: Double) async throws { try await Task.sleep(for:.seconds(seconds)) }
    func apply(_ saved: [Monitor]) async throws {
        if previewMode { return }
        var pairs = try resolve(saved,in:connectedMonitors())
        var rotated = false
        for (wanted,actual) in pairs where wanted.rotation != actual.rotation {
            guard !actual.builtIn, DMRotationSupported(actual.displayID), DMSetRotation(actual.displayID,Int32(wanted.rotation)) else {
                throw DisplayError.message("\(actual.label(in:monitors))의 \(wanted.rotation)° 회전을 적용하지 못했습니다.")
            }
            rotated = true
        }
        if rotated {
            for attempt in 0..<20 {
                try await pause(0.5)
                pairs = try resolve(saved,in:connectedMonitors())
                if pairs.allSatisfy({ $0.0.rotation == $0.1.rotation }) { break }
                if attempt == 19 { throw DisplayError.message("모니터 회전 응답 시간이 초과되었습니다.") }
            }
        }
        // Look up modes again after rotation: mode numbers can change between connections.
        pairs = try resolve(saved,in:connectedMonitors())
        var selected: [(Monitor,Monitor,CGDisplayMode)] = []
        for (wanted,actual) in pairs {
            let options = [kCGDisplayShowDuplicateLowResolutionModes as String:true] as CFDictionary
            let modes = CGDisplayCopyAllDisplayModes(actual.displayID,options) as? [CGDisplayMode] ?? []
            guard let mode = modes.first(where: { wanted.modeMatches($0) && $0.ioDisplayModeID == wanted.modeID }) ?? modes.first(where:wanted.modeMatches) else {
                throw DisplayError.message("\(actual.label(in:monitors)): 저장된 \(wanted.resolution) · \(wanted.hz) 모드를 현재 연결에서 지원하지 않습니다.")
            }
            selected.append((wanted,actual,mode))
        }
        var config: CGDisplayConfigRef?
        guard CGBeginDisplayConfiguration(&config) == .success, let config else {
            throw DisplayError.message("디스플레이 설정 작업을 시작하지 못했습니다.")
        }
        func check(_ result: CGError) throws {
            if result != .success { throw DisplayError.message("macOS 디스플레이 설정 오류 (\(result.rawValue))") }
        }
        do {
            for (wanted,actual,mode) in selected.sorted(by:{$0.0.isMain && !$1.0.isMain}) {
                let mirror = wanted.mirrorUUID.flatMap { uuid in pairs.first(where:{$0.0.uuid == uuid})?.1.displayID } ?? 0
                if wanted.mirrorUUID != nil && mirror == 0 { throw DisplayError.message("복제 대상 모니터가 연결되지 않았습니다.") }
                if CGDisplayMirrorsDisplay(actual.displayID) != mirror {
                    try check(CGConfigureDisplayMirrorOfDisplay(config,actual.displayID,mirror))
                }
                if !wanted.modeMatches(CGDisplayCopyDisplayMode(actual.displayID) ?? mode) {
                    try check(CGConfigureDisplayWithDisplayMode(config,actual.displayID,mode,nil))
                }
                if actual.x != wanted.x || actual.y != wanted.y || wanted.isMain != actual.isMain {
                    try check(CGConfigureDisplayOrigin(config,actual.displayID,Int32(wanted.x),Int32(wanted.y)))
                }
            }
        } catch { CGCancelDisplayConfiguration(config); throw error }
        try check(CGCompleteDisplayConfiguration(config,.forSession))
        try await pause(0.8)
        let verified = try resolve(saved,in:connectedMonitors())
        for (wanted,actual) in verified {
            guard wanted.width == actual.width, wanted.height == actual.height,
                  wanted.pixelWidth == actual.pixelWidth, wanted.pixelHeight == actual.pixelHeight,
                  abs(wanted.refreshRate - actual.refreshRate) < 0.6,
                  wanted.rotation == actual.rotation, wanted.x == actual.x, wanted.y == actual.y,
                  wanted.isMain == actual.isMain else {
                throw DisplayError.message("\(actual.label(in:monitors))의 적용 결과가 저장된 설정과 다릅니다.")
            }
        }
    }
    func restore() async -> Bool {
        guard !busy, !brightnessBusy, let saved = profile else { return false }
        refresh()
        do { _ = try resolve(saved.monitors,in:monitors) }
        catch { reportError(error.localizedDescription); return false }
        errorMessage = nil
        busy = true
        let before = monitors
        status = "‘\(saved.name)’ 환경의 회전·해상도·배치를 복구하고 있습니다…"
        do {
            try await apply(saved.monitors)
            refresh()
            var brightnessErrors: [String] = []
            for (wanted,actual) in try resolve(saved.monitors,in:monitors) {
                if let level = wanted.brightness {
                    if !(await sendBrightness(actual,percent:level,saveTo:nil)) { brightnessErrors.append(actual.label(in:monitors)) }
                }
            }
            busy = false
            topology = topologyKey(monitors)
            oled.configure(resolvedProtection(saved,current:monitors))
            updateGroupLevel()
            status = "‘\(saved.name)’ 환경으로 복구했습니다."
            if !brightnessErrors.isEmpty { reportError("밝기 응답 확인 필요: " + brightnessErrors.joined(separator:", ")) }
            writeDiagnostics()
            return true
        } catch {
            let reason = error.localizedDescription
            do { try await apply(before); reportError("복구 실패 · 이전 배치로 되돌렸습니다. \(reason)") }
            catch { reportError("복구 실패: \(reason) · 시스템 설정에서 현재 화면을 확인하세요.") }
            busy = false; refresh(); topology = topologyKey(monitors); writeDiagnostics()
            return false
        }
    }
    func readHardwareBrightness(_ id: UInt32) async -> [AnyHashable:Any] {
        await withCheckedContinuation { continuation in
            brightnessQueue.async { continuation.resume(returning:DMReadBrightness(id)) }
        }
    }
    func readBrightness() async {
        if previewMode { return }
        let snapshot = monitors
        for monitor in snapshot {
            if brightness[monitor.uuid]?.busy == true && brightness[monitor.uuid]?.supported == true { continue }
            brightness[monitor.uuid] = BrightnessState()
            let result = await readHardwareBrightness(monitor.displayID)
            guard monitors.contains(where:{$0.uuid == monitor.uuid && $0.sameHardware(as:monitor)}) else { continue }
            if let current = result["current"] as? Int, let maximum = result["maximum"] as? Int, maximum > 0 {
                brightness[monitor.uuid] = BrightnessState(percent:Double(current)*100/Double(maximum),maximum:maximum,supported:true,busy:false,message:monitor.builtIn ? "내장 화면 연결됨" : "DDC/CI 연결됨")
            } else {
                brightness[monitor.uuid] = BrightnessState(busy:false,message:result["error"] as? String ?? "밝기 응답 없음")
            }
        }
        updateGroupLevel()
        writeDiagnostics()
    }
    func updateGroupLevel() {
        guard !brightnessBusy else { return }
        let values = brightnessTargets.compactMap { brightness[$0.uuid]?.percent }
        if !values.isEmpty { groupPercent = (values.reduce(0,+)/Double(values.count)).rounded() }
    }
    func changeGroupBrightness() {
        let targets = brightnessTargets
        let level = Int(groupPercent.rounded())
        guard !busy, !brightnessBusy, !brightnessReading, !targets.isEmpty, (0...100).contains(level) else { return }
        let saveID = profile?.matches(monitors) == true ? profile?.id : library.matching(monitors)?.id
        errorMessage = nil
        brightnessBusy = true
        Task {
            var failed = monitors.filter { brightness[$0.uuid]?.supported != true }.map { $0.label(in:monitors) }
            for monitor in targets {
                if !(await sendBrightness(monitor,percent:level,saveTo:saveID)) { failed.append(monitor.label(in:monitors)) }
            }
            brightnessBusy = false; updateGroupLevel()
            if failed.isEmpty { status = "밝기를 \(level)%로 조절했습니다." }
            else { reportError("밝기 적용 확인 필요: " + failed.joined(separator:", ")) }
            writeDiagnostics()
        }
    }
    func changeBrightness(_ monitor: Monitor, percent: Int) {
        if linkedBrightness { groupPercent = Double(percent); changeGroupBrightness(); return }
        guard !busy, !brightnessBusy else { return }
        let saveID = profile?.matches(monitors) == true ? profile?.id : library.matching(monitors)?.id
        Task { _ = await sendBrightness(monitor,percent:percent,saveTo:saveID); updateGroupLevel(); writeDiagnostics() }
    }
    func sendBrightness(_ monitor: Monitor, percent: Int, saveTo profileID: String?) async -> Bool {
        if previewMode {
            guard (0...100).contains(percent) else { return false }
            brightness[monitor.uuid] = BrightnessState(percent:Double(percent),maximum:100,supported:true,busy:false,message:"미리보기")
            return true
        }
        guard (0...100).contains(percent), let actual = monitor.matching(connectedMonitors()) else { return false }
        guard brightness[actual.uuid]?.busy != true else { return false }
        brightness[actual.uuid]?.busy = true
        let prior = await readHardwareBrightness(actual.displayID)
        guard let maximum = prior["maximum"] as? Int, maximum > 0 else {
            brightness[actual.uuid] = BrightnessState(busy:false,message:prior["error"] as? String ?? "밝기 응답 없음")
            return false
        }
        brightness[actual.uuid] = BrightnessState(percent:Double(percent),maximum:maximum,supported:true,busy:true,message:"밝기 적용 중…")
        guard let stillConnected = actual.matching(connectedMonitors()), stillConnected.displayID == actual.displayID else {
            brightness[actual.uuid] = BrightnessState(busy:false,message:"밝기 적용 중 연결이 변경되었습니다.")
            return false
        }
        let raw = brightnessRawValue(percent:percent,maximum:maximum)
        let write: [AnyHashable:Any] = await withCheckedContinuation { continuation in
            brightnessQueue.async { continuation.resume(returning:DMWriteBrightness(actual.displayID,Int32(raw))) }
        }
        let result = await readHardwareBrightness(actual.displayID)
        guard let current = result["current"] as? Int, abs(current-raw) <= max(1,maximum/100), write["ok"] as? Bool == true else {
            brightness[actual.uuid] = BrightnessState(percent:Double((prior["current"] as? Int ?? 0))*100/Double(maximum),maximum:maximum,supported:true,busy:false,message:actual.builtIn ? "내장 화면의 밝기 적용을 확인하지 못했습니다." : "밝기 적용을 확인하지 못했습니다. HDR/모니터 OSD를 확인하세요.")
            reportError("\(actual.label(in:monitors))의 밝기 적용을 확인하지 못했습니다.")
            return false
        }
        brightness[actual.uuid] = BrightnessState(percent:Double(current)*100/Double(maximum),maximum:maximum,supported:true,busy:false,message:actual.builtIn ? "내장 화면 연결됨" : "DDC/CI 연결됨")
        if let profileID, let index = profiles.firstIndex(where:{$0.id == profileID}),
           let monitorIndex = profiles[index].monitors.firstIndex(where:{$0.sameHardware(as:actual)}) {
            var updated = profiles; updated[index].monitors[monitorIndex].brightness = percent
            do { try persist(updated); profiles = updated }
            catch { reportError("밝기는 적용했지만 저장에 실패했습니다: \(error.localizedDescription)") }
        }
        return true
    }
    func makeMain(_ monitor: Monitor) {
        guard !busy, !brightnessBusy, !monitor.isMain else { return }
        pending?.cancel(); refresh(); errorMessage = nil
        let before = monitors
        busy = true
        Task {
            do {
                guard let actual = monitor.matching(before) else { throw DisplayError.message("모니터 연결을 확인하세요.") }
                let target = try makingPrimary(actual.uuid,in:before)
                try Profile(monitors:target).validate()
                try await apply(target)
                status = "주 모니터를 변경했습니다. 저장하면 이 구성을 기억합니다."
            } catch {
                let reason = error.localizedDescription
                try? await apply(before)
                reportError("주 모니터 변경 실패: \(reason)")
            }
            busy = false; refresh(); topology = topologyKey(monitors); writeDiagnostics()
        }
    }
    func rotate(_ monitor: Monitor, to degree: Int) {
        guard !busy, !brightnessBusy, monitor.rotation != degree, !monitor.builtIn else { return }
        pending?.cancel()
        // Rotation changes available dimensions; use the system-selected mode, then save on request.
        let old = monitor.rotation
        busy = true; status = "\(monitor.label(in:monitors))를 \(degree)°로 회전하고 있습니다…"
        Task {
            if DMSetRotation(monitor.displayID,Int32(degree)) {
                for _ in 0..<20 {
                    try? await pause(0.5)
                    if Int(CGDisplayRotation(monitor.displayID)) == degree { break }
                }
            }
            if Int(CGDisplayRotation(monitor.displayID)) == degree {
                status = "회전했습니다. ‘현재 환경 저장’을 누르면 이 방향을 기억합니다."
            } else {
                _ = DMSetRotation(monitor.displayID,Int32(old))
                reportError("회전하지 못했습니다. 모니터 연결과 시스템 설정을 확인하세요.")
            }
            busy = false; refresh(); topology = topologyKey(monitors); writeDiagnostics()
        }
    }
    func writeDiagnostics() {
        if previewMode { return }
        let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted,.sortedKeys]
        struct Diagnostic: Encodable {
            var current: [Monitor]; var saved: Profile?; var profiles: [Profile]; var status: String
            var selectedProfileID: String; var linkedBrightness: Bool
            var autoRestore: Bool; var loginEnabled: Bool; var loginMessage: String
            var oledProtectionEnabled: Bool; var oledIdleSeconds: Int; var oledBlankedDisplayIDs: [UInt32]
            var oledDetectionMode: String; var oledProtectedDisplayIDs: [UInt32]
            var oledKeyboardCounterAvailable: Bool
            var ddc: [String:String]
        }
        let d = Diagnostic(current:monitors,saved:profile,profiles:profiles,status:status,selectedProfileID:selectedProfileID,linkedBrightness:linkedBrightness,autoRestore:autoRestore,loginEnabled:loginEnabled,loginMessage:loginMessage,
            oledProtectionEnabled:oled.enabled,oledIdleSeconds:oled.seconds,oledBlankedDisplayIDs:oled.blankedDisplayIDs,
            oledDetectionMode:oled.settings.mode,oledProtectedDisplayIDs:oled.protectedDisplayIDs,
            oledKeyboardCounterAvailable:oled.keyboardCounterAvailable,
            ddc:brightness.mapValues { "\($0.message) | \(Int($0.percent.rounded()))% | max \($0.maximum)" })
        if let data = try? encoder.encode(d) { try? data.write(to:directory.appendingPathComponent("diagnostics.json"),options:.atomic) }
    }
}

extension View {
    @ViewBuilder func glassCommand(prominent: Bool = false) -> some View {
        if #available(macOS 26.0, *) {
            if prominent { buttonStyle(.glassProminent).foregroundStyle(.white) } else { buttonStyle(.glass) }
        } else {
            if prominent { buttonStyle(.borderedProminent) } else { buttonStyle(.bordered) }
        }
    }
    func displaySurface() -> some View {
        padding(18).background(ConsolePanelBackdrop(corner:18))
    }

}

struct LiveWindowBackground: NSViewRepresentable {
    func makeNSView(context: Context) -> NSVisualEffectView {
        let effect = NSVisualEffectView()
        effect.material = .underWindowBackground
        effect.blendingMode = .behindWindow
        effect.state = .active
        return effect
    }
    func updateNSView(_ view: NSVisualEffectView,context: Context) {}
}

struct LayoutView: View {
    var monitors: [Monitor]
    var body: some View {
        GeometryReader { geo in
            let left = CGFloat(monitors.map(\.x).min() ?? 0)
            let top = CGFloat(monitors.map(\.y).min() ?? 0)
            let right = CGFloat(monitors.map { $0.x + $0.width }.max() ?? 1)
            let bottom = CGFloat(monitors.map { $0.y + $0.height }.max() ?? 1)
            let scale = min((geo.size.width-112)/max(1,right-left),(geo.size.height-62)/max(1,bottom-top))
            let dx = (geo.size.width-(right-left)*scale)/2
            let dy = (geo.size.height-(bottom-top)*scale)/2-5
            ForEach(Array(monitors.enumerated()),id:\.element.uuid) { index,monitor in
                MonitorPreview(monitor:monitor,index:index,label:monitor.label(in:monitors))
                    .frame(width:max(60,CGFloat(monitor.width)*scale-4),height:max(60,CGFloat(monitor.height)*scale-4))
                    .position(x:dx+(CGFloat(monitor.x)-left+CGFloat(monitor.width)/2)*scale,
                              y:dy+(CGFloat(monitor.y)-top+CGFloat(monitor.height)/2)*scale)
            }
        }
        .frame(height:242)
        .background {
            ConsolePanelBackdrop(corner:22)
            RoundedRectangle(cornerRadius:22).fill(LinearGradient(colors:[Color.accentColor.opacity(0.10),.indigo.opacity(0.035),.clear],startPoint:.topLeading,endPoint:.bottomTrailing))
        }
        .overlay(alignment:.bottomTrailing) {
            Button("배치 설정",systemImage:"arrow.up.right") {
                NSWorkspace.shared.open(URL(string:"x-apple.systempreferences:com.apple.Displays-Settings.extension")!)
            }.glassCommand().controlSize(.small).padding(14).help("macOS 디스플레이 설정 열기")
        }
        .overlay {
            if monitors.isEmpty { ContentUnavailableView("연결된 화면이 없습니다",systemImage:"display",description:Text("모니터를 연결한 뒤 새로 고침하세요.")) }
        }
    }
}
struct MonitorPreview: View {
    var monitor: Monitor
    var index: Int
    var label: String
    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius:11).fill(.black.opacity(0.75))
            RoundedRectangle(cornerRadius:8)
                .fill(LinearGradient(colors:index.isMultiple(of:2) ? [.cyan,.blue,.indigo] : [.blue,.indigo,.purple],startPoint:.topLeading,endPoint:.bottomTrailing))
                .overlay {
                    RoundedRectangle(cornerRadius:8).fill(LinearGradient(colors:[.white.opacity(0.24),.clear,.black.opacity(0.15)],startPoint:.topLeading,endPoint:.bottomTrailing))
                }
                .padding(4)
            VStack(spacing:6) {
                Image(systemName:monitor.builtIn ? "laptopcomputer" : "display").font(.system(size:21,weight:.light))
                Text(label).font(.system(size:11,weight:.medium)).lineLimit(1).minimumScaleFactor(0.75)
                Text(monitor.resolution).font(.system(size:10)).opacity(0.8)
            }.foregroundStyle(.white).padding(10)
        }
        .overlay(alignment:.top) {
            if monitor.isMain {
                Capsule().fill(.white.opacity(0.9)).frame(height:3).padding(.horizontal,10).padding(.top,6)
            }
        }
        .overlay(RoundedRectangle(cornerRadius:11).strokeBorder(.white.opacity(0.25)))
        .shadow(color:.black.opacity(0.14),radius:12,x:0,y:8)
        .accessibilityElement(children:.ignore)
        .accessibilityLabel("\(label), \(monitor.resolution), \(monitor.rotation)도")
    }
}

struct BrightnessPanel: View {
    @ObservedObject var store: MonitorStore
    var body: some View {
        VStack(alignment:.leading,spacing:14) {
            HStack(spacing:12) {
                Image(systemName:"sun.max.fill").font(.title2).foregroundStyle(.tint)
                    .frame(width:40,height:40).background(.tint.opacity(0.09),in:RoundedRectangle(cornerRadius:12))
                VStack(alignment:.leading,spacing:3) {
                    Text("밝기").font(.headline)
                }
                Spacer()
                if store.brightnessBusy { ProgressView().controlSize(.small) }
                if store.linkedBrightness {
                    Text("\(Int(store.groupPercent.rounded()))%")
                        .font(.system(size:28,weight:.medium,design:.rounded)).monospacedDigit().contentTransition(.numericText())
                }
            }
            if store.linkedBrightness {
                HStack(spacing:12) {
                    Image(systemName:"sun.min").foregroundStyle(.secondary)
                    Slider(value:$store.groupPercent,in:0...100,step:1,onEditingChanged:{ editing in
                        if !editing { store.changeGroupBrightness() }
                    }).accessibilityLabel("모든 모니터 통합 밝기")
                        .disabled(store.busy || store.brightnessBusy || store.brightnessReading || store.brightnessTargets.isEmpty)
                    Image(systemName:"sun.max").foregroundStyle(.secondary)
                }
            }
            HStack {
                Spacer()
                Toggle("밝기 함께 조절",isOn:$store.linkedBrightness).toggleStyle(.switch).controlSize(.small)
                    .disabled(store.busy || store.brightnessBusy)
            }
        }.displaySurface()
    }
}

struct OLEDProtectionPanel: View {
    @ObservedObject var store: MonitorStore
    private var enabled: Binding<Bool> {
        Binding(get:{store.screenProtectionSettings.enabled},set:{ value in
            var settings = store.screenProtectionSettings; settings.enabled = value; store.setScreenProtection(settings)
        })
    }
    private var seconds: Binding<Int> {
        Binding(get:{store.screenProtectionSettings.seconds},set:{ value in
            var settings = store.screenProtectionSettings; settings.seconds = value; store.setScreenProtection(settings)
        })
    }
    var body: some View {
        VStack(alignment:.leading,spacing:14) {
            HStack(spacing:12) {
                Image(systemName:"moon.zzz.fill").font(.title2).foregroundStyle(.tint)
                    .frame(width:40,height:40).background(.tint.opacity(0.09),in:RoundedRectangle(cornerRadius:12))
                Text("OLED 화면 보호").font(.headline)
                Spacer()
                Toggle("OLED 화면 보호",isOn:enabled).labelsHidden().toggleStyle(.switch).controlSize(.small)
                    .accessibilityLabel("OLED 화면 보호")
            }
            if store.screenProtectionSettings.enabled {
                HStack {
                    Text("대기 시간").font(.subheadline).foregroundStyle(.secondary)
                    Spacer()
                    TextField("초",value:seconds,format:.number.grouping(.never))
                        .textFieldStyle(.roundedBorder).multilineTextAlignment(.trailing).frame(width:64)
                        .accessibilityLabel("OLED 화면 보호 대기 시간(초)")
                    Text("초").foregroundStyle(.secondary)
                    Stepper("대기 시간",value:seconds,in:1...3600).labelsHidden().help("1~3600초")
                }
            }
        }.displaySurface().disabled(store.busy)
        .help("마우스 활동은 해당 화면에, 키보드 입력은 마지막으로 클릭한 화면에 반영합니다. 선택한 환경에 자동 저장됩니다.")
    }
}

struct MonitorCard: View {
    @ObservedObject var store: MonitorStore
    var monitor: Monitor
    private var title: String { monitor.label(in:store.monitors) }
    private var state: BrightnessState { store.brightness[monitor.uuid] ?? BrightnessState() }
    var body: some View {
        VStack(alignment:.leading,spacing:14) {
            HStack(spacing:10) {
                Image(systemName:monitor.builtIn ? "laptopcomputer" : "display").foregroundStyle(.tint)
                    .frame(width:27,height:27).background(.tint.opacity(0.1),in:RoundedRectangle(cornerRadius:8))
                Text(title).font(.headline).lineLimit(1)
                Spacer(minLength:4)
                Menu {
                    Button("주 모니터로 설정") { store.makeMain(monitor) }
                        .disabled(monitor.isMain || monitor.mirrorUUID != nil || store.busy || store.brightnessBusy)
                } label: { Image(systemName:"ellipsis") }
                .menuStyle(.borderlessButton).menuIndicator(.hidden).frame(width:20)
                .accessibilityLabel("\(title) 설정").help("모니터 역할 설정")
            }
            HStack(spacing:6) {
                Text(monitor.resolution)
                Text("·").foregroundStyle(.tertiary)
                Text(monitor.hz)
            }.font(.system(size:11)).foregroundStyle(.secondary)
            HStack {
                Text("방향").font(.subheadline).foregroundStyle(.secondary)
                Spacer()
                if monitor.builtIn { Text("표준").font(.subheadline) }
                else {
                    Picker("\(title) 회전 방향",selection:Binding(get:{monitor.rotation},set:{store.rotate(monitor,to:$0)})) {
                        ForEach([0,90,180,270],id:\.self) { Text("\($0)°").tag($0) }
                    }.labelsHidden().frame(width:92).disabled(store.busy || store.brightnessBusy)
                }
            }
            if !store.linkedBrightness {
                HStack {
                    Image(systemName:"sun.min").foregroundStyle(.secondary)
                    Slider(value:Binding(get:{state.percent},set:{store.brightness[monitor.uuid]?.percent = $0}),in:0...100,step:1,onEditingChanged:{ editing in
                        if !editing { store.changeBrightness(monitor,percent:Int(state.percent.rounded())) }
                    }).accessibilityLabel("\(title) 밝기")
                        .disabled(!state.supported || state.busy || store.busy || store.brightnessBusy)
                    Text(state.supported ? "\(Int(state.percent.rounded()))%" : "—").font(.caption).monospacedDigit().frame(width:30,alignment:.trailing)
                }
            }
            if !state.supported && !state.busy {
                Label("밝기 연결 확인",systemImage:"exclamationmark.circle")
                    .font(.caption2).foregroundStyle(.orange).help(state.message)
            }
            DisclosureGroup("모니터 정보") {
                VStack(alignment:.leading,spacing:5) {
                    Text(monitor.name).textSelection(.enabled)
                    Text("ID  \(monitor.identityLabel)").textSelection(.enabled)
                    Text("위치  (\(monitor.x), \(monitor.y))")
                    Text("렌더링  \(monitor.pixelWidth) × \(monitor.pixelHeight)")
                    if !state.supported { Text(state.message).foregroundStyle(.orange) }
                }.font(.system(size:10,design:.monospaced)).foregroundStyle(.secondary)
                    .frame(maxWidth:.infinity,alignment:.leading).padding(.top,6)
            }.font(.caption2).foregroundStyle(.secondary)
        }.displaySurface().frame(maxHeight:.infinity,alignment:.top)
    }
}

struct ContentView: View {
    @ObservedObject var store: MonitorStore
    private var controlsBusy: Bool { store.busy || store.brightnessBusy }
    var body: some View {
        NavigationSplitView {
            sidebar.navigationSplitViewColumnWidth(min:190,ideal:220,max:280)
        } detail: {
            detail
                .navigationTitle(store.profile?.name ?? "디스플레이")
                .toolbar { commands }
        }
        .navigationSplitViewStyle(.balanced)
        .background(LiveWindowBackground().ignoresSafeArea().allowsHitTesting(false))
        .frame(minWidth:880,minHeight:680)
        .sheet(isPresented:$store.showSaveDialog) { SaveProfileView(store:store) }
        .alert("저장 환경 삭제",isPresented:Binding(get:{store.deletionTarget != nil},set:{if !$0 { store.deletionTarget = nil }}),presenting:store.deletionTarget) { target in
            Button("취소",role:.cancel) { store.deletionTarget = nil }
            Button("삭제",role:.destructive) { store.deleteProfile(target) }
        } message: { target in
            Text("‘\(target.name)’ 환경을 목록에서 삭제할까요? 삭제한 설정은 저장 폴더에 백업됩니다.")
        }
    }
    private var sidebar: some View {
        VStack(spacing:0) {
            List(selection:Binding<String?>(get:{store.selectedProfileID},set:{store.selectProfile($0 ?? "")})) {
                Section("디스플레이") {
                    Label("현재 연결",systemImage:"display.2").tag("")
                }
                Section("저장된 환경") {
                    ForEach(store.profiles) { saved in
                        Label {
                            VStack(alignment:.leading,spacing:4) {
                                Text(saved.name).lineLimit(1)
                                Text("디스플레이 \(saved.monitors.count)대").font(.caption2).foregroundStyle(.secondary)
                            }.padding(.vertical,3)
                        } icon: {
                            Image(systemName:saved.monitors.contains(where:\.builtIn) ? "laptopcomputer.and.arrow.down" : "display.2")
                        }.tag(saved.id)
                            .contextMenu {
                                Button("환경 삭제…",systemImage:"trash",role:.destructive) {
                                    store.selectProfile(saved.id); store.requestDelete()
                                }.disabled(controlsBusy)
                            }
                    }
                }
            }.listStyle(.sidebar).scrollContentBackground(.hidden).disabled(controlsBusy)
        }.navigationTitle("Monitor Shade")
    }
    private var detail: some View {
        ScrollView {
            VStack(alignment:.leading,spacing:20) {
                HStack(alignment:.firstTextBaseline) {
                    Text("디스플레이").font(.system(size:25,weight:.semibold))
                    Spacer()
                    if controlsBusy { ProgressView().controlSize(.small) }
                    Label("\(store.monitors.count)대 연결됨",systemImage:"circle.fill")
                        .font(.caption).foregroundStyle(.secondary).symbolRenderingMode(.palette).foregroundStyle(.green,.secondary)
                }
                LayoutView(monitors:store.monitors)
                if !store.monitors.isEmpty { BrightnessPanel(store:store) }
                if !store.externalMonitors.isEmpty { OLEDProtectionPanel(store:store) }
                LazyVGrid(columns:[GridItem(.adaptive(minimum:260),spacing:14)],alignment:.leading,spacing:14) {
                    ForEach(monitorsInRoleOrder(store.monitors)) { monitor in
                        MonitorCard(store:store,monitor:monitor)
                    }
                }
                if let message = store.errorMessage {
                    HStack(alignment:.top,spacing:8) {
                        Label(message,systemImage:"exclamationmark.circle").font(.caption).foregroundStyle(.orange)
                            .fixedSize(horizontal:false,vertical:true).textSelection(.enabled)
                        Spacer()
                        Button { store.errorMessage = nil } label: { Image(systemName:"xmark") }
                            .buttonStyle(.borderless).help("알림 닫기")
                    }
                }
            }.padding(26)
        }
        .background(Color.clear)
    }
    @ToolbarContentBuilder private var commands: some ToolbarContent {
        ToolbarItemGroup(placement:.primaryAction) {
            Button("새 환경 추가…",systemImage:"plus") { store.requestSave(asNew:true) }
                .keyboardShortcut("n").help("이름을 정해 새 환경 추가").disabled(controlsBusy).labelStyle(.iconOnly)
            Button("환경 삭제…",systemImage:"trash",action:store.requestDelete)
                .disabled(controlsBusy || store.profile == nil).help("선택한 저장 환경 삭제").labelStyle(.iconOnly)
            Button("현재 환경 저장…",systemImage:"square.and.arrow.down",action:store.saveCurrent)
                .keyboardShortcut("s").help("현재 환경 저장").disabled(controlsBusy || store.brightnessReading).labelStyle(.iconOnly)
        }
        ToolbarItem(placement:.primaryAction) {
            Button(action:store.restoreNow) { Label("복구",systemImage:"arrow.counterclockwise").labelStyle(.titleAndIcon) }
                .keyboardShortcut("r").glassCommand(prominent:true)
                .help("선택한 환경으로 복구")
                .disabled(controlsBusy || store.brightnessReading || store.profile?.matches(store.monitors) != true)
        }
        ToolbarItemGroup(placement:.primaryAction) {
            Button("새로 고침",systemImage:"arrow.clockwise",action:store.reload)
                .help("모니터와 밝기 상태 새로 고침").disabled(controlsBusy).labelStyle(.iconOnly)
            Button("앱 설정",systemImage:"gearshape") { store.showSettings = true }
                .help("자동 복구 및 로그인 설정").labelStyle(.iconOnly)
                .popover(isPresented:$store.showSettings,arrowEdge:.top) { PreferencesView(store:store) }
        }
    }
}

struct PreferencesView: View {
    @ObservedObject var store: MonitorStore
    var body: some View {
        VStack(alignment:.leading,spacing:18) {
            Text("Monitor Shade 설정").font(.headline)
            VStack(alignment:.leading,spacing:16) {
                Toggle("연결하면 자동 복구",isOn:$store.autoRestore)
                Toggle("로그인할 때 실행",isOn:Binding(get:{store.loginEnabled},set:store.setLogin))
            }.toggleStyle(.switch).controlSize(.small)
            if !store.loginMessage.isEmpty {
                Text(store.loginMessage).font(.caption).foregroundStyle(.orange)
                Button("로그인 항목 열기") { SMAppService.openSystemSettingsLoginItems() }
            }
            Divider()
            Button("저장 파일 보기",systemImage:"folder") { NSWorkspace.shared.open(store.directory) }
                .buttonStyle(.link)
        }.padding(22).frame(width:300)
    }
}
struct SaveProfileView: View {
    @ObservedObject var store: MonitorStore
    @FocusState private var nameFocused: Bool
    var body: some View {
        VStack(alignment:.leading,spacing:20) {
            HStack(spacing:12) {
                Image(systemName:"display.2").font(.system(size:25,weight:.light)).foregroundStyle(.tint)
                    .frame(width:52,height:52).background(.tint.opacity(0.09),in:RoundedRectangle(cornerRadius:14))
                VStack(alignment:.leading,spacing:4) {
                    Text("모니터 환경 저장").font(.title3.weight(.semibold))
                    Text("디스플레이 \(store.monitors.count)대의 배치와 밝기").font(.caption).foregroundStyle(.secondary)
                }
            }
            VStack(alignment:.leading,spacing:8) {
                Text("환경 이름").font(.subheadline.weight(.medium))
                TextField("예: 서재, 업무 데스크",text:$store.saveName).textFieldStyle(.roundedBorder).controlSize(.large)
                    .accessibilityLabel("저장할 환경 이름").focused($nameFocused)
                Text("같은 이름으로 저장하면 기존 환경을 갱신합니다.").font(.caption2).foregroundStyle(.secondary)
            }
            if !store.saveError.isEmpty { Text(store.saveError).font(.caption).foregroundStyle(.red).fixedSize(horizontal:false,vertical:true) }
            HStack {
                Spacer()
                Button("취소") { store.showSaveDialog = false }.keyboardShortcut(.cancelAction).glassCommand()
                Button("저장",action:store.commitSave).keyboardShortcut(.defaultAction).glassCommand(prominent:true)
            }.controlSize(.large)
        }.padding(28).frame(width:400).onAppear { nameFocused = true }
    }
}

@MainActor
final class AppDelegate: NSObject,NSApplicationDelegate,NSWindowDelegate {
    var window: NSWindow!
    var statusItem: NSStatusItem!
    let store = MonitorStore()
    lazy var integration = MacIntegration(store:store)
    func applicationDidFinishLaunching(_ notification: Notification) {
        let menu = NSMenu()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle:"설정…",action:#selector(settings),keyEquivalent:",").target = self
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle:"Monitor Shade 종료",action:#selector(NSApplication.terminate(_:)),keyEquivalent:"q")
        let item = NSMenuItem(); item.submenu = appMenu; menu.addItem(item); NSApp.mainMenu = menu
        window = NSWindow(contentRect:NSRect(x:0,y:0,width:1000,height:800),styleMask:[.titled,.closable,.miniaturizable,.resizable,.fullSizeContentView],backing:.buffered,defer:false)
        window.title = "Monitor Shade"; window.isReleasedWhenClosed = false
        window.isOpaque = false
        window.backgroundColor = .clear
        window.titlebarAppearsTransparent = true
        window.toolbarStyle = .unified
        window.minSize = NSSize(width:920,height:680)
        window.contentViewController = NSHostingController(rootView:ConsoleView(store:store,integration:integration))
        window.delegate = self
        if let visible = NSScreen.main?.visibleFrame {
            window.setContentSize(NSSize(width:min(1000,visible.width-48),height:min(820,visible.height-72)))
        }
        window.center()
        statusItem = NSStatusBar.system.statusItem(withLength:NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName:"display.2",accessibilityDescription:"Monitor Shade")
        let statusMenu = NSMenu()
        statusMenu.addItem(withTitle:"Monitor Shade 열기",action:#selector(showWindow),keyEquivalent:"").target = self
        statusMenu.addItem(withTitle:"현재 환경 저장",action:#selector(save),keyEquivalent:"").target = self
        statusMenu.addItem(withTitle:"저장한 환경 복구",action:#selector(restore),keyEquivalent:"").target = self
        statusMenu.addItem(.separator())
        statusMenu.addItem(withTitle:"모든 화면 다시 표시",action:#selector(revealScreens),keyEquivalent:"").target = self
        statusMenu.addItem(.separator())
        statusMenu.addItem(withTitle:"종료",action:#selector(NSApplication.terminate(_:)),keyEquivalent:"q")
        statusItem.menu = statusMenu
        store.start()
        integration.showSettings = { [weak self] in self?.showWindow() }
        integration.start()
        if CommandLine.arguments.contains("--settings") || (!store.preferences.startHidden && !CommandLine.arguments.contains("--background") && !wasLaunchedAtLogin()) { showWindow() }
    }
    func wasLaunchedAtLogin() -> Bool {
        guard let event = NSAppleEventManager.shared().currentAppleEvent else { return false }
        return event.paramDescriptor(forKeyword:keyAEPropData)?.enumCodeValue == keyAELaunchedAsLogInItem
    }
    @objc func showWindow() { store.oled.setSettingsVisible(true); window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps:true) }
    @objc func save() { store.preferences.page = .displays; showWindow(); store.saveCurrent() }
    @objc func settings() { store.preferences.page = .general; showWindow() }
    @objc func restore() { store.restoreNow() }
    @objc func revealScreens() { store.oled.revealAll() }
    func windowWillClose(_ notification: Notification) { store.oled.setSettingsVisible(false) }
    func windowDidMiniaturize(_ notification: Notification) { store.oled.setSettingsVisible(false) }
    func windowDidDeminiaturize(_ notification: Notification) { store.oled.setSettingsVisible(true) }
    func applicationWillTerminate(_ notification: Notification) { integration.stop(); store.oled.stop() }
    func applicationShouldHandleReopen(_ sender:NSApplication,hasVisibleWindows:Bool) -> Bool { showWindow(); return true }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender:NSApplication) -> Bool { false }
}

@main
struct MonitorShade {
    @MainActor static func main() {
        if let index = CommandLine.arguments.firstIndex(of:"--ui-test"), CommandLine.arguments.indices.contains(index+1) {
            MacVerification.runUI(path:CommandLine.arguments[index+1]); return
        }
        if CommandLine.arguments.contains("--self-test") {
            MacVerification.runLogic()
            precondition(DMHardwareSelfTest())
            var a = Monitor(uuid:"D783172C-B777-4524-A1DA-EEB0401B700F",name:"Test A",vendor:1715,model:43575,serial:0x01010101,alphaSerial:"A1",edidUUID:"",location:"",builtIn:false,width:1440,height:2560,pixelWidth:2880,pixelHeight:5120,refreshRate:95,modeID:140,x:0,y:0,rotation:90,mirrorUUID:nil,brightness:50,displayID:4,isMain:true)
            var b = a; b.uuid = "B3BC56A7-94B4-43B7-9021-528DC09F48CC"; b.alphaSerial = "B1"; b.displayID = 3; b.x = 1440; b.y = 403; b.isMain = false
            var swappedA = a; swappedA.uuid = b.uuid; swappedA.displayID = 30
            var swappedB = b; swappedB.uuid = a.uuid; swappedB.displayID = 40
            precondition(a.matching([swappedB,swappedA])?.displayID == 30)
            precondition(a.matching([b]) == nil)
            a.alphaSerial = ""; b.alphaSerial = ""; b.uuid = UUID().uuidString
            precondition(a.matching([b]) == nil)
            let p = Profile(monitors:[a,b]); try! p.validate()
            let decoded = try! JSONDecoder().decode(Profile.self,from:JSONEncoder().encode(p))
            precondition(decoded.monitors == p.monitors)
            var invalid = p; invalid.monitors[0].rotation = 45
            do { try invalid.validate(); fatalError("Invalid rotation accepted") } catch {}
            invalid = p; invalid.monitors[0].brightness = 101
            do { try invalid.validate(); fatalError("Invalid brightness accepted") } catch {}
            invalid = p; invalid.monitors[0].x = Int.min
            do { try invalid.validate(); fatalError("Invalid position accepted") } catch {}
            // Existing single-profile files migrate without losing display configuration.
            var legacyObject = try! JSONSerialization.jsonObject(with:JSONEncoder().encode(p)) as! [String:Any]
            legacyObject.removeValue(forKey:"id"); legacyObject.removeValue(forKey:"name")
            legacyObject["monitors"] = (legacyObject["monitors"] as! [[String:Any]]).map { value in
                var old = value; old.removeValue(forKey:"isMain"); return old
            }
            let migrated = try! JSONDecoder().decode(Profile.self,from:JSONSerialization.data(withJSONObject:legacyObject))
            precondition(migrated.name == "기존 환경" && migrated.monitors == p.monitors)
            var home = p; home.name = "테스트 데스크 A"
            var dellH = a; dellH.uuid = UUID().uuidString; dellH.vendor = 4268; dellH.model = 41234
            dellH.isMain = false; dellH.y = -1440
            dellH.alphaSerial = "DELL-H"; dellH.width = 2560; dellH.height = 1440; dellH.pixelWidth = 2560; dellH.pixelHeight = 1440; dellH.rotation = 0
            var dellV = dellH; dellV.uuid = UUID().uuidString; dellV.alphaSerial = "DELL-V"
            dellV.width = 1440; dellV.height = 2560; dellV.pixelWidth = 1440; dellV.pixelHeight = 2560; dellV.rotation = 90; dellV.x = 2560
            var mac = a; mac.uuid = UUID().uuidString; mac.vendor = 1552; mac.model = 1; mac.builtIn = true; mac.alphaSerial = ""
            mac.width = 1728; mac.height = 1117; mac.rotation = 0; mac.x = 0; mac.y = 0; mac.isMain = true
            let office = Profile(name:"테스트 데스크 B",monitors:[mac,dellH,dellV])
            let list = ProfileLibrary(profiles:[home,office]); try! list.validate()
            precondition(list.matching([b,a])?.id == home.id)
            precondition(list.matching([dellV,mac,dellH])?.id == office.id)
            precondition(list.matching([dellV,dellH]) == nil) // MacBook open vs clamshell are separate sets.
            precondition(list.matching([mac,a,b]) == nil)
            var replacedDell = dellV; replacedDell.alphaSerial = "DIFFERENT-SERIAL"
            precondition(list.matching([mac,dellH,replacedDell]) == nil)
            var recent = home; recent.id = UUID().uuidString; recent.name = "같은 장비의 다른 배치"
            recent.savedAt = home.savedAt.addingTimeInterval(1)
            precondition(ProfileLibrary(profiles:[recent,home,office]).matching([a,b])?.id == recent.id)
            let reloaded = try! JSONDecoder().decode(ProfileLibrary.self,from:JSONEncoder().encode(list))
            precondition(reloaded.profiles.map(\.id) == list.profiles.map(\.id))
            let alternatives = ProfileLibrary(profiles:[recent,home,office]).removing(recent.id)
            precondition(alternatives.matching([a,b])?.id == home.id)
            precondition(alternatives.profiles.map(\.id) == [home.id,office.id])
            let empty = ProfileLibrary(profiles:[home]).removing(home.id)
            try! empty.validate()
            let emptyReloaded = try! JSONDecoder().decode(ProfileLibrary.self,from:JSONEncoder().encode(empty))
            precondition(emptyReloaded.profiles.isEmpty && emptyReloaded.matching([a,b]) == nil)
            precondition(migrated.monitors.filter(\.isMain).map(\.uuid) == [a.uuid])
            precondition(a.label(in:[a,b]) == "주 모니터")
            precondition(b.label(in:[a,b]) == "외부 모니터 A")
            precondition(dellH.label(in:[mac,dellV,dellH]) == "외부 모니터 A")
            precondition(dellV.label(in:[mac,dellV,dellH]) == "외부 모니터 B")
            let changedMain = try! makingPrimary(b.uuid,in:[a,b])
            try! Profile(monitors:changedMain).validate()
            precondition(changedMain.first(where:{$0.isMain})?.uuid == b.uuid)
            precondition(changedMain[0].x == -1440 && changedMain[0].y == -403)
            precondition(changedMain[1].x == 0 && changedMain[1].y == 0)
            let returnedMain = try! makingPrimary(a.uuid,in:changedMain)
            precondition(returnedMain == [a,b])
            var noMain = home; noMain.monitors = changedMain.map { var m = $0; m.isMain = false; return m }
            do { try noMain.validate(); fatalError("Missing main display accepted") } catch {}
            precondition(brightnessRawValue(percent:50,maximum:100) == 50)
            precondition(brightnessRawValue(percent:50,maximum:400) == 200)
            precondition(brightnessRawValue(percent:53,maximum:10000) == 5300)
            precondition(brightnessRawValue(percent:0,maximum:10000) == 0)
            precondition(brightnessRawValue(percent:100,maximum:10000) == 10000)
            let supportedBrightness = BrightnessState(supported:true,busy:false)
            let mixedBrightness = [mac.uuid:supportedBrightness,dellH.uuid:supportedBrightness]
            precondition(brightnessCapableMonitors([mac,dellH,dellV],states:mixedBrightness).map(\.uuid) == [mac.uuid,dellH.uuid])
            precondition(brightnessCapableMonitors([mac],states:mixedBrightness).map(\.uuid) == [mac.uuid])
            precondition(brightnessCapableMonitors([mac],states:[:]).isEmpty)
            var savedMac = mac; savedMac.brightness = 61
            let nativeProfile = Profile(monitors:[savedMac,dellH,dellV])
            try! nativeProfile.validate()
            precondition(try! JSONDecoder().decode(Profile.self,from:JSONEncoder().encode(nativeProfile)).monitors.first?.brightness == 61)
            var idle = DisplayIdleTimers()
            idle.reset([3,4],at:100)
            precondition(idle.expired(at:129.99,after:30).isEmpty)
            idle.activity(on:3,at:120)
            precondition(idle.expired(at:130,after:30) == [4])
            idle.activity(on:4,at:131)
            precondition(idle.expired(at:131,after:30).isEmpty)
            precondition(idle.expired(at:150,after:30) == [3])
            idle.activity(on:99,at:151) // An unprotected / built-in screen cannot reset another screen.
            precondition(idle.lastActivity[99] == nil)
            idle.reset([7],at:200) // Reconnect / wake gets a new grace period and removes stale displays.
            precondition(idle.expired(at:229.99,after:30).isEmpty)
            precondition(idle.expired(at:230,after:30) == [7])
            idle.reset([],at:300)
            precondition(idle.expired(at:400,after:1).isEmpty)
            precondition(ScreenProtectionSettings().seconds == 30 && !ScreenProtectionSettings().enabled)
            precondition(ScreenProtectionSettings.validSeconds(0) == 1 && ScreenProtectionSettings.validSeconds(4000) == 3600)
            var oledHome = home; oledHome.screenProtection = ScreenProtectionSettings(enabled:true,seconds:30)
            let oledLibrary = ProfileLibrary(profiles:[oledHome,office])
            let oledReloaded = try! JSONDecoder().decode(ProfileLibrary.self,from:JSONEncoder().encode(oledLibrary))
            precondition(oledReloaded.matching([a,b])?.screenProtection == ScreenProtectionSettings(enabled:true,seconds:30))
            precondition(oledReloaded.matching([mac,dellH,dellV])?.screenProtection.enabled == false)
            var oldProtection = try! JSONSerialization.jsonObject(with:JSONEncoder().encode(home)) as! [String:Any]
            oldProtection.removeValue(forKey:"screenProtection")
            let oldWithoutProtection = try! JSONDecoder().decode(Profile.self,from:JSONSerialization.data(withJSONObject:oldProtection))
            precondition(!oldWithoutProtection.screenProtection.enabled)
            invalid = home; invalid.screenProtection.seconds = 0
            do { try invalid.validate(); fatalError("Invalid OLED timeout accepted") } catch {}
            precondition(monitorsInRoleOrder([b,a]).first?.isMain == true)
            precondition(monitorsInRoleOrder([dellV,mac,dellH]).map(\.uuid) == [mac.uuid,dellH.uuid,dellV.uuid])
            idle.reset([3,4],at:0)
            idle.pointerActivity(on:3,clicked:true,at:10)
            idle.pointerActivity(on:4,clicked:false,at:15)
            idle.keyboardActivity(pointerDisplay:4,at:25)
            precondition(idle.lastActivity[3] == 25 && idle.lastActivity[4] == 15)
            precondition(idle.expired(at:45,after:30) == [4])
            idle.pointerActivity(on:4,clicked:true,at:46)
            idle.keyboardActivity(pointerDisplay:3,at:50)
            precondition(idle.lastActivity[4] == 50 && idle.lastActivity[3] == 25)
            idle.pointerActivity(on:99,clicked:true,at:51) // Keyboard on the MacBook cannot keep external OLEDs awake.
            idle.keyboardActivity(pointerDisplay:99,at:60)
            precondition(idle.expired(at:80,after:30) == [3,4] && idle.lastActivity[99] == nil)
            idle.reset([3,4],at:100)
            idle.keyboardActivity(pointerDisplay:4,at:110) // Before the first click, use pointer location.
            precondition(idle.lastActivity[3] == 100 && idle.lastActivity[4] == 110)
            print("PASS: built-in and external brightness targets, native brightness scaling and profile persistence; per-display mouse and keyboard idle routing, keyboard wake, OLED built-in exclusion, main-first cards; environment OLED settings; profiles, migration, roles, hardware matching and DDC. No display settings changed.")

            return
        }
        let app = NSApplication.shared
        let delegate = AppDelegate(); app.delegate = delegate; app.setActivationPolicy(.accessory)
        withExtendedLifetime(delegate) { app.run() }
    }
}
