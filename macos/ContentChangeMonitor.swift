import AppKit
import ScreenCaptureKit
import CoreMedia
import CoreVideo

enum FrameDifference {
    static func changed(_ before: [UInt8], _ after: [UInt8]) -> Bool {
        guard before.count == after.count, !before.isEmpty else { return true }
        var changed = 0
        let threshold = max(3,before.count/5000)
        for index in before.indices where abs(Int(before[index])-Int(after[index])) > 3 {
            changed += 1
            if changed >= threshold { return true }
        }
        return false
    }
}

private final class ContentFrameReceiver: NSObject, SCStreamOutput, SCStreamDelegate {
    var onFrame: ((Bool) -> Void)?
    var onError: ((Error) -> Void)?
    private var previous: [UInt8] = []
    func stream(_ stream: SCStream, didStopWithError error: Error) { onError?(error) }
    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen, sampleBuffer.isValid else { return }
        if let list = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer,createIfNecessary:false) as? [[SCStreamFrameInfo:Any]],
           let raw = list.first?[.status] as? Int {
            if raw == SCFrameStatus.idle.rawValue { onFrame?(false); return }
            if raw != SCFrameStatus.complete.rawValue { return }
        }
        guard let image = sampleBuffer.imageBuffer else { return }
        CVPixelBufferLockBaseAddress(image,.readOnly)
        defer { CVPixelBufferUnlockBaseAddress(image,.readOnly) }
        guard let base = CVPixelBufferGetBaseAddress(image) else { return }
        let bytes = base.assumingMemoryBound(to:UInt8.self)
        let width = CVPixelBufferGetWidth(image), height = CVPixelBufferGetHeight(image), stride = CVPixelBufferGetBytesPerRow(image)
        var luminance = [UInt8](repeating:0,count:width*height)
        for y in 0..<height {
            for x in 0..<width {
                let p = y*stride+x*4
                luminance[y*width+x] = UInt8((Int(bytes[p])*29+Int(bytes[p+1])*150+Int(bytes[p+2])*77)>>8)
            }
        }
        let changed = FrameDifference.changed(previous,luminance)
        previous = luminance
        onFrame?(changed)
    }
}

@MainActor final class ContentChangeMonitor {
    var onFrame: ((CGDirectDisplayID,Bool) -> Void)?
    var onStatus: (() -> Void)?
    private(set) var message = ""
    private var ids = Set<CGDirectDisplayID>()
    private var ignoreWallpaper = true
    private var streams: [CGDirectDisplayID:SCStream] = [:]
    private var receivers: [CGDirectDisplayID:ContentFrameReceiver] = [:]
    private var frames: [CGDirectDisplayID:TimeInterval] = [:]
    private var filterKeys: [CGDirectDisplayID:[CGWindowID]] = [:]
    private var task: Task<Void,Never>?
    private var generation = 0
    private let queue = DispatchQueue(label:"MonitorShade.content",qos:.utility)
    func isReceiving(_ id: CGDirectDisplayID) -> Bool {
        guard let last = frames[id] else { return false }
        return ProcessInfo.processInfo.systemUptime-last < 4
    }
    func start(ids: Set<CGDirectDisplayID>, ignoreWallpaper: Bool) {
        if self.ids == ids && self.ignoreWallpaper == ignoreWallpaper && task != nil { return }
        stop(); self.ids = ids; self.ignoreWallpaper = ignoreWallpaper
        guard !ids.isEmpty else { return }
        guard CGPreflightScreenCaptureAccess() else {
            message = "화면 변화 감지에는 화면 기록 권한이 필요합니다. 일반 → 화면 기록에서 허용한 뒤 다시 확인하세요."
            onStatus?(); return
        }
        let token = generation
        task = Task { [weak self] in
            guard let self else { return }
            while !Task.isCancelled && token == generation {
                do { try await refresh(token:token) }
                catch {
                    guard token == generation else { return }
                    message = "화면 변화 감지: \(error.localizedDescription)"
                    frames.removeAll(); onStatus?()
                }
                do { try await Task.sleep(for:.seconds(2)) } catch { return }
            }
        }
    }
    func stop() {
        generation += 1
        task?.cancel(); task = nil
        let old = Array(streams.values)
        streams.removeAll(); receivers.removeAll(); frames.removeAll(); filterKeys.removeAll()
        ids.removeAll(); message = ""
        Task { for stream in old { try? await stream.stopCapture() } }
    }
    private func refresh(token: Int) async throws {
        let content = try await SCShareableContent.excludingDesktopWindows(false,onScreenWindowsOnly:true)
        guard token == generation, !Task.isCancelled else { return }
        let ownPID = ProcessInfo.processInfo.processIdentifier
        let excluded = content.applications.filter { $0.processID == ownPID }
        let windows = content.windows.filter { window in
            window.owningApplication?.processID != ownPID && window.windowLayer == 0 &&
            window.owningApplication?.bundleIdentifier != "com.apple.wallpaper.agent" &&
            window.owningApplication?.bundleIdentifier != "com.apple.Wallpaper"
        }
        for display in content.displays where ids.contains(display.displayID) {
            let id = display.displayID
            let included = windows.filter { $0.frame.intersects(display.frame) }
            let keys = included.map(\.windowID).sorted()
            if ignoreWallpaper && included.isEmpty {
                if let old = streams.removeValue(forKey:id) { try? await old.stopCapture() }
                receivers.removeValue(forKey:id)
                let changed = filterKeys[id] != []
                filterKeys[id] = []; frames[id] = ProcessInfo.processInfo.systemUptime
                onFrame?(id,changed)
                continue
            }
            let filter = ignoreWallpaper ? SCContentFilter(display:display,including:included)
                : SCContentFilter(display:display,excludingApplications:excluded,exceptingWindows:[])
            if let stream = streams[id] {
                if filterKeys[id] != keys { try await stream.updateContentFilter(filter); filterKeys[id] = keys }
                // A healthy ScreenCaptureKit stream may emit no new image for a
                // static desktop. Filter refresh confirms that it is still active.
                if frames[id] != nil { frames[id] = ProcessInfo.processInfo.systemUptime }
                continue
            }
            let config = SCStreamConfiguration()
            let aspect = Double(display.width)/Double(max(1,display.height))
            config.width = min(640,max(160,display.width))
            config.height = max(90,Int(Double(config.width)/aspect))
            config.pixelFormat = kCVPixelFormatType_32BGRA
            config.minimumFrameInterval = CMTime(value:1,timescale:2)
            config.queueDepth = 3; config.showsCursor = false; config.capturesAudio = false
            config.backgroundColor = NSColor.black.cgColor
            let receiver = ContentFrameReceiver()
            receiver.onFrame = { [weak self] changed in
                Task { @MainActor in
                    guard let self, token == generation else { return }
                    frames[id] = ProcessInfo.processInfo.systemUptime
                    onFrame?(id,changed)
                }
            }
            receiver.onError = { [weak self] error in
                Task { @MainActor in
                    guard let self, token == generation else { return }
                    frames.removeValue(forKey:id); streams.removeValue(forKey:id); receivers.removeValue(forKey:id)
                    message = "화면 변화 감지: \(error.localizedDescription)"; onStatus?()
                }
            }
            let stream = SCStream(filter:filter,configuration:config,delegate:receiver)
            try stream.addStreamOutput(receiver,type:.screen,sampleHandlerQueue:queue)
            streams[id] = stream; receivers[id] = receiver; filterKeys[id] = keys
            do { try await stream.startCapture() }
            catch { streams.removeValue(forKey:id); receivers.removeValue(forKey:id); throw error }
            guard token == generation else { try? await stream.stopCapture(); return }
        }
        message = ""; onStatus?()
    }
}
