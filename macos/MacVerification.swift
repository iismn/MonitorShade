import AppKit
import SwiftUI

@MainActor enum MacVerification {
    static func demoMonitors() -> [Monitor] {
        let left = Monitor(uuid:"11111111-1111-4111-8111-111111111111",name:"ASUS PG27UCWM",vendor:1715,model:100,serial:1,alphaSerial:"DEMO-LEFT",edidUUID:"",location:"",builtIn:false,width:1440,height:2560,pixelWidth:2160,pixelHeight:3840,refreshRate:120,modeID:10,x:-1440,y:-628,rotation:90,mirrorUUID:nil,brightness:70,displayID:1001,isMain:false)
        let right = Monitor(uuid:"22222222-2222-4222-8222-222222222222",name:"ASUS PG27UCWM",vendor:1715,model:100,serial:2,alphaSerial:"DEMO-RIGHT",edidUUID:"",location:"",builtIn:false,width:2560,height:1440,pixelWidth:3840,pixelHeight:2160,refreshRate:120,modeID:11,x:0,y:0,rotation:0,mirrorUUID:nil,brightness:70,displayID:1002,isMain:true)
        return [left,right]
    }
    static func populate(_ store: MonitorStore) {
        store.monitors = demoMonitors()
        store.profiles = [Profile(name:"Desk",monitors:store.monitors)]
        store.selectedProfileID = store.profiles[0].id
        for monitor in store.monitors { store.brightness[monitor.uuid] = BrightnessState(percent:70,maximum:100,supported:true,busy:false,message:"미리보기") }
        store.groupPercent = 70; store.linkedBrightness = true
        store.status = "미리보기 · 실제 모니터 설정은 변경하지 않습니다."
    }
    static func runLogic() {
        var passed = 0
        func check(_ valid: @autoclosure () -> Bool, _ name: String) {
            precondition(valid(),name); passed += 1; print("PASS: \(name)")
        }
        let areas = [CGRect(x:0,y:0,width:3840,height:2120),CGRect(x:-2160,y:-628,width:2160,height:3780),CGRect(x:200,y:500,width:1440,height:875)]
        for area in areas {
            for action in ShortcutAction.allCases.filter(\.isWindowAction) {
                for stage in 0..<(action == .center ? 2 : 3) {
                    let rect = WindowPlacement.rectangle(action:action,stage:stage,area:area,centerPercent:65)
                    check(area.insetBy(dx:-1,dy:-1).contains(rect),"\(action.rawValue)/\(stage) fits \(area)")
                    check(rect.width > 0 && rect.height > 0,"Window size is positive")
                    if action == .center && stage == 0 { check(abs(rect.width/rect.height-16/9) < 0.015,"Center window retains 16:9 on portrait and landscape work areas") }
                }
            }
            let half = WindowPlacement.rectangle(action:.resize,stage:0,area:area,centerPercent:65)
            let mid = WindowPlacement.rectangle(action:.resize,stage:1,area:area,centerPercent:65)
            let full = WindowPlacement.rectangle(action:.resize,stage:2,area:area,centerPercent:65)
            check(abs(half.width-area.width*0.5) <= 1 && abs(half.height-area.height*0.5) <= 1,"Resize stage 1 scales both axes to 50%")
            check(abs(mid.width-area.width*0.7) <= 1 && abs(mid.height-area.height*0.7) <= 1,"Resize stage 2 scales both axes to 70%")
            check(full == area,"Resize stage 3 fills the work area")
        }
        check((try? MacShortcut.validate(MacShortcut.defaults)) != nil,"Default shortcuts are complete and distinct")
        var duplicate = MacShortcut.defaults
        duplicate[1].key = duplicate[0].key; duplicate[1].modifiers = duplicate[0].modifiers
        check((try? MacShortcut.validate(duplicate)) == nil,"Duplicate global key combinations are rejected")
        duplicate[1].enabled = false
        check((try? MacShortcut.validate(duplicate)) != nil,"Disabled bindings do not reserve keys")
        var invalid = MacShortcut.defaults; invalid[0].modifiers = 0
        check((try? MacShortcut.validate(invalid)) == nil,"Bare typing keys are rejected")
        invalid = MacShortcut.defaults; invalid[0].key = 9999
        check((try? MacShortcut.validate(invalid)) == nil,"Unknown key codes are rejected")
        let encoded = try! JSONEncoder().encode(MacShortcut.defaults)
        check(try! JSONDecoder().decode([MacShortcut].self,from:encoded) == MacShortcut.defaults,"Custom shortcuts round-trip without changing actions")
        let legacy = Data("{\"enabled\":true,\"seconds\":60}".utf8)
        let old = try! JSONDecoder().decode(ScreenProtectionSettings.self,from:legacy)
        check(old.enabled && old.seconds == 60 && old.mode == "input" && old.ignoreWallpaper && old.selectedUUIDs == nil,"Existing Mac protection settings preserve permission-free input mode")
        let monitors = demoMonitors()
        var profile = Profile(name:"Test desk",monitors:monitors)
        profile.screenProtection = ScreenProtectionSettings(enabled:true,seconds:10,mode:"content",ignoreWallpaper:true,selectedUUIDs:[monitors[0].uuid])
        try! profile.validate()
        let reloaded = try! JSONDecoder().decode(Profile.self,from:JSONEncoder().encode(profile))
        check(reloaded.screenProtection == profile.screenProtection && reloaded.monitors == monitors,"Monitor arrays, main display, modes and protection targets persist together")
        var reconnected = monitors
        reconnected[0].uuid = "33333333-3333-4333-8333-333333333333"
        check(resolvedProtection(profile,current:reconnected).selectedUUIDs == [reconnected[0].uuid],"Protection selection follows matched hardware when display UUIDs change across ports")
        var bad = profile; bad.screenProtection.mode = "unknown"
        check((try? bad.validate()) == nil,"Unknown protection modes are rejected")
        bad = profile; bad.screenProtection.selectedUUIDs = ["invalid"]
        check((try? bad.validate()) == nil,"Invalid persistent display identities are rejected")
        bad = profile; bad.screenProtection.selectedUUIDs = ["44444444-4444-4444-8444-444444444444"]
        check((try? bad.validate()) == nil,"Protection targets must belong to their saved display setup")
        let before = [UInt8](repeating:100,count:10000)
        check(!FrameDifference.changed(before,before),"Static frame does not reset the idle timer")
        var noise = before; noise[0] = 101; noise[1] = 102
        check(!FrameDifference.changed(before,noise),"Subpixel capture noise is ignored")
        var motion = before; for i in 0..<20 { motion[i] = 200 }
        check(FrameDifference.changed(before,motion),"Visible content changes wake the display")
        check(FrameDifference.changed([],before),"The first frame starts a fresh idle interval")
        check(ConsoleTheme.warmth(at:9) == 0 && ConsoleTheme.warmth(at:14) == 0,"Daytime brightness card stays green")
        check(ConsoleTheme.warmth(at:20) == 1 && ConsoleTheme.warmth(at:24) == ConsoleTheme.warmth(at:0),"Warm card wraps continuously at midnight")
        var prior = ConsoleTheme.warmth(at:0)
        for minute in 1...1440 {
            let next = ConsoleTheme.warmth(at:Double(minute)/60)
            precondition(abs(next-prior) < 0.007); prior = next
        }
        check(true,"Time palettes change gradually across a complete day")
        print("PASS: \(passed) additional macOS checks. No hardware writes, screen capture, login registration or user-profile changes.")
    }
    static func runUI(path: String) {
        let app = NSApplication.shared
        app.setActivationPolicy(.accessory)
        let store = MonitorStore(preview:true); store.start()
        let integration = MacIntegration(store:store)
        let window = NSWindow(contentRect:NSRect(x:0,y:0,width:1120,height:820),styleMask:[.titled,.closable,.resizable,.fullSizeContentView],backing:.buffered,defer:false)
        window.isReleasedWhenClosed = false; window.title = "Monitor Shade"
        window.titlebarAppearsTransparent = true; window.isOpaque = false; window.backgroundColor = .clear
        window.contentViewController = NSHostingController(rootView:ConsoleView(store:store,integration:integration))
        window.center(); window.makeKeyAndOrderFront(nil)
        app.activate(ignoringOtherApps:true)
        let directory = URL(fileURLWithPath:path,isDirectory:true)
        try! FileManager.default.createDirectory(at:directory,withIntermediateDirectories:true)
        Task { @MainActor in
            var results = [String]()
            var morningSidebar: [Double]?
            for mode in ["morning","evening"] {
                store.preferences.appearance = mode
                for page in ConsolePage.allCases {
                    store.preferences.page = page
                    try? await Task.sleep(for:.milliseconds(650))
                    guard let view = window.contentView else { fatalError("Missing settings view") }
                    view.layoutSubtreeIfNeeded(); view.displayIfNeeded()
                    guard let bitmap = view.bitmapImageRepForCachingDisplay(in:view.bounds) else { fatalError("Cannot render settings view") }
                    view.cacheDisplay(in:view.bounds,to:bitmap)
                    guard let png = bitmap.representation(using:.png,properties:[:]) else { fatalError("Cannot encode settings preview") }
                    let imageURL = directory.appendingPathComponent("\(mode)-\(page.rawValue).png")
                    try! png.write(to:imageURL)
                    // AppKit's view cache omits vibrancy-hosted sidebar content.
                    // Capture this test app's own window through the compositor.
                    let capture = Process()
                    capture.executableURL = URL(fileURLWithPath:"/usr/sbin/screencapture")
                    capture.arguments = ["-x","-o","-l",String(window.windowNumber),imageURL.path]
                    do { try capture.run(); capture.waitUntilExit() }
                    catch { fatalError("Native window capture failed: \(error)") }
                    precondition(capture.terminationStatus == 0,"Native test-window capture must succeed")
                    let rendered = NSBitmapImageRep(data:try! Data(contentsOf:imageURL))!
                    var minimum = 1.0, maximum = 0.0
                    for y in stride(from:Int(Double(rendered.pixelsHigh)*0.18),to:Int(Double(rendered.pixelsHigh)*0.65),by:4) {
                        for x in stride(from:Int(Double(rendered.pixelsWide)*0.04),to:Int(Double(rendered.pixelsWide)*0.18),by:4) {
                            if let color = rendered.colorAt(x:x,y:y)?.usingColorSpace(.deviceRGB) {
                                let level = (color.redComponent+color.greenComponent+color.blueComponent)/3
                                minimum = min(minimum,level); maximum = max(maximum,level)
                            }
                        }
                    }
                    precondition(maximum-minimum > 0.12,"Sidebar text and controls must be visible in the native window")
                    precondition(bitmap.pixelsWide >= 900 && bitmap.pixelsHigh >= 600)
                    if page == .general {
                        // Sample empty areas at the same height, away from cards,
                        // navigation rows and the version label. This catches a
                        // separate opaque sidebar even when its text is visible.
                        func background(_ x: Double) -> [Double] {
                            let px = Int(x/window.frame.width*Double(rendered.pixelsWide))
                            let py = Int(Double(rendered.pixelsHigh)*0.86)
                            var channels = [Double](repeating:0,count:3)
                            for dy in -2...2 { for dx in -2...2 {
                                let color = rendered.colorAt(x:px+dx,y:py+dy)!.usingColorSpace(.deviceRGB)!
                                channels[0] += color.redComponent/25
                                channels[1] += color.greenComponent/25
                                channels[2] += color.blueComponent/25
                            } }
                            return channels
                        }
                        let sidebar = background(100), detail = background(270)
                        let difference = zip(sidebar,detail).map { abs($0-$1) }.max()!
                        precondition(difference < 0.08,"Sidebar and detail must share the same backdrop; delta=\(difference)")
                        results.append("PASS: \(mode) sidebar/detail background delta \(String(format:"%.4f",difference))")
                        if mode == "morning" { morningSidebar = sidebar }
                        else if let morningSidebar {
                            precondition(zip(morningSidebar,sidebar).map { abs($0-$1) }.max()! > 0.15,"The sidebar must follow the time palette")
                            results.append("PASS: Sidebar changes with the morning/evening palette")
                        }
                    }
                    results.append("PASS: \(mode)/\(page.rawValue) settings page rendered")
                }
            }
            results.append("INFO: Preview monitors only; no display capture, global shortcuts, hardware writes or login registration.")
            try! results.joined(separator:"\n").write(to:directory.appendingPathComponent("ui.txt"),atomically:true,encoding:.utf8)
            window.close(); integration.stop(); app.stop(nil)
            let event = NSEvent.otherEvent(with:.applicationDefined,location:.zero,modifierFlags:[],timestamp:0,windowNumber:0,context:nil,subtype:0,data1:0,data2:0)
            if let event { app.postEvent(event,atStart:true) }
        }
        app.run()
    }
}
