import AppKit
import SwiftUI
import CoreGraphics
import ServiceManagement

struct ConsoleTheme {
    var light: Bool
    var tint: Color
    var aura: Color
    var warmth: Double
    static func warmth(at hour: Double) -> Double {
        let h = hour.truncatingRemainder(dividingBy:24)+24
        let value = h.truncatingRemainder(dividingBy:24)
        func smooth(_ t: Double) -> Double { let v = min(1,max(0,t)); return v*v*(3-2*v) }
        if value < 5 || value >= 20 { return 1 }
        if value < 9 { return 1-smooth((value-5)/4) }
        return smooth((value-14)/6)
    }
    static func at(_ date: Date, mode: String) -> ConsoleTheme {
        let time = Calendar.current.dateComponents([.hour,.minute],from:date)
        let hour = ["morning":9.0,"day":14.0,"evening":20.0,"night":0.0][mode]
            ?? Double(time.hour ?? 0)+Double(time.minute ?? 0)/60
        if hour >= 6 && hour < 12 { return ConsoleTheme(light:true,tint:Color(red:0.62,green:0.36,blue:0.20),aura:Color(red:1,green:0.85,blue:0.69),warmth:warmth(at:hour)) }
        if hour >= 12 && hour < 18 { return ConsoleTheme(light:true,tint:Color(red:0.28,green:0.43,blue:0.28),aura:Color(red:0.67,green:0.79,blue:0.55),warmth:warmth(at:hour)) }
        if hour >= 18 && hour < 23 { return ConsoleTheme(light:false,tint:Color(red:0.78,green:0.89,blue:0.65),aura:Color(red:0.36,green:0.40,blue:0.27),warmth:warmth(at:hour)) }
        return ConsoleTheme(light:false,tint:Color(red:0.67,green:0.84,blue:0.83),aura:Color(red:0.16,green:0.24,blue:0.34),warmth:warmth(at:hour))
    }
}

struct ConsoleView: View {
    @ObservedObject var store: MonitorStore
    @ObservedObject var preferences: MacPreferences
    var integration: MacIntegration
    @State private var search = ""
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    init(store: MonitorStore, integration: MacIntegration) {
        self.store = store; self.integration = integration; preferences = store.preferences
    }
    var body: some View {
        TimelineView(.periodic(from:.now,by:30)) { timeline in
            let theme = ConsoleTheme.at(timeline.date,mode:preferences.appearance)
            NavigationSplitView {
                VStack(alignment:.leading,spacing:14) {
                    HStack(spacing:8) {
                        Image(systemName:"magnifyingglass").foregroundStyle(.secondary)
                        TextField("설정 검색",text:$search).textFieldStyle(.plain)
                        if !search.isEmpty {
                            Button { search = "" } label: { Image(systemName:"xmark.circle.fill").foregroundStyle(.secondary) }.buttonStyle(.plain).help("검색 지우기")
                        }
                    }.padding(.horizontal,12).padding(.vertical,10).consoleGlass(corner:20).padding(.horizontal,12)
                    List(selection:$preferences.page) {
                        ForEach(ConsolePage.allCases.filter { search.isEmpty || $0.title.localizedCaseInsensitiveContains(search) }) { page in
                            Label {
                                Text(page.title).fontWeight(preferences.page == page ? .semibold : .regular)
                            } icon: {
                                Image(systemName:page.symbol).foregroundStyle(theme.tint).symbolRenderingMode(.hierarchical)
                            }.padding(.vertical,7).tag(page)
                        }
                    }.listStyle(.sidebar).scrollContentBackground(.hidden)
                    Text("macOS · 1.18.1").font(.caption2).foregroundStyle(.secondary).padding(.horizontal,16)
                }.padding(.top,12).padding(.bottom,12).navigationSplitViewColumnWidth(min:190,ideal:208,max:250)
            } detail: {
                VStack(spacing:0) {
                    HStack {
                        Text(preferences.page.title).font(.system(size:28,weight:.semibold))
                        Spacer()
                        Text(timeline.date,style:.time).font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                        Button(action:store.reload) { Image(systemName:"arrow.clockwise").frame(width:20,height:20) }.glassCommand().buttonBorderShape(.circle).help("새로 고침")
                    }.padding(.horizontal,28).padding(.top,22).padding(.bottom,18)
                    ScrollView {
                        VStack(alignment:.leading,spacing:18) {
                            page(theme:theme).id(preferences.page).transition(.opacity)
                            if let error = store.errorMessage {
                                HStack {
                                    Label(error,systemImage:"exclamationmark.circle").font(.callout).foregroundStyle(.orange)
                                    Spacer()
                                    Button { store.errorMessage = nil } label: { Image(systemName:"xmark") }.buttonStyle(.plain)
                                }.consoleCard()
                            }
                        }.frame(maxWidth:1120,alignment:.leading).padding(.horizontal,28).padding(.bottom,28)
                    }
                }.animation(reduceMotion ? nil : .easeInOut(duration:0.20),value:preferences.page)
            }
            .navigationSplitViewStyle(.balanced)
            .background {
                if preferences.blur { LiveWindowBackground().ignoresSafeArea() }
                else { Color(nsColor:.windowBackgroundColor).ignoresSafeArea() }
                theme.aura.opacity(preferences.blur ? 0.18 : 0.12).ignoresSafeArea()
                RadialGradient(colors:[theme.aura.opacity(0.18),.clear],center:.topTrailing,startRadius:0,endRadius:650).ignoresSafeArea().allowsHitTesting(false)
            }
            .tint(theme.tint).preferredColorScheme(theme.light ? .light : .dark)
            .sheet(isPresented:$store.showSaveDialog) { SaveProfileView(store:store) }
            .alert("저장 환경 삭제",isPresented:Binding(get:{store.deletionTarget != nil},set:{if !$0 { store.deletionTarget = nil }}),presenting:store.deletionTarget) { target in
                Button("취소",role:.cancel) { store.deletionTarget = nil }
                Button("삭제",role:.destructive) { store.deleteProfile(target) }
            } message: { target in Text("‘\(target.name)’ 환경을 삭제할까요? 삭제한 환경은 저장 폴더에 백업됩니다.") }
        }.frame(minWidth:920,minHeight:680)
    }
    @ViewBuilder private func page(theme: ConsoleTheme) -> some View {
        switch preferences.page {
        case .brightness: BrightnessConsolePage(store:store,integration:integration,theme:theme)
        case .displays: DisplayProfilesPage(store:store)
        case .protection: ProtectionConsolePage(store:store)
        case .layout: LayoutConsolePage(preferences:preferences)
        case .shortcuts: ShortcutsConsolePage(preferences:preferences,integration:integration)
        case .general: GeneralConsolePage(store:store,preferences:preferences,integration:integration)
        case .help: HelpConsolePage()
        }
    }
}

extension View {
    func consoleCard() -> some View {
        modifier(ConsoleCardSurface())
    }
    @ViewBuilder func consoleGlass(corner: CGFloat = 20, interactive: Bool = false) -> some View {
        if #available(macOS 26.0, *) {
            glassEffect(.regular.interactive(interactive),in:.rect(cornerRadius:corner))
        } else {
            background(.regularMaterial,in:RoundedRectangle(cornerRadius:corner))
                .overlay(RoundedRectangle(cornerRadius:corner).strokeBorder(.primary.opacity(0.08)))
        }
    }
}

private struct ConsoleCardSurface: ViewModifier {
    @Environment(\.colorScheme) private var scheme
    @Environment(\.colorSchemeContrast) private var contrast
    @Environment(\.accessibilityReduceTransparency) private var reduceTransparency
    func body(content: Content) -> some View {
        content.padding(22).frame(maxWidth:.infinity,alignment:.leading)
            .background {
                if reduceTransparency { RoundedRectangle(cornerRadius:20).fill(Color(nsColor:.controlBackgroundColor)) }
                else { RoundedRectangle(cornerRadius:20).fill(.thinMaterial) }
            }
            .overlay(RoundedRectangle(cornerRadius:20).strokeBorder(
                LinearGradient(colors:[.white.opacity(scheme == .light ? 0.78 : 0.22),.primary.opacity(contrast == .increased ? 0.3 : 0.06)],startPoint:.topLeading,endPoint:.bottomTrailing)))
            .shadow(color:.black.opacity(scheme == .light ? 0.035 : 0.06),radius:16,x:0,y:6)
    }
}

// Liquid Glass is reserved for navigation and controls. Colorful dashboard
// cards use standard material, keeping their content on a separate layer.
private struct BrightnessAmbient: View {
    var warmth: Double
    var body: some View {
        GeometryReader { geometry in
            ZStack {
                Rectangle().fill(.regularMaterial)
                LinearGradient(colors:[Color(red:0.52,green:0.63,blue:0.40),Color(red:0.21,green:0.48,blue:0.27)],startPoint:.topLeading,endPoint:.bottomTrailing).opacity(0.88)
                RadialGradient(colors:[Color(red:0.95,green:0.87,blue:0.59).opacity(0.85),.clear],center:UnitPoint(x:0.28,y:0),startRadius:0,endRadius:geometry.size.width*0.85)
                RadialGradient(colors:[Color(red:0.09,green:0.48,blue:0.18).opacity(0.8),.clear],center:.bottomLeading,startRadius:0,endRadius:geometry.size.width*0.85)
                ZStack {
                    Color(red:0.70,green:0.74,blue:0.79)
                    RadialGradient(colors:[Color(red:1,green:0.49,blue:0.12),Color(red:1,green:0.56,blue:0.22).opacity(0.80),.clear],center:UnitPoint(x:0.42,y:0.51),startRadius:0,endRadius:geometry.size.width*0.72)
                    RadialGradient(colors:[Color(red:0.97,green:0.46,blue:0.65).opacity(0.87),.clear],center:UnitPoint(x:1,y:0.38),startRadius:0,endRadius:geometry.size.width*0.8)
                    RadialGradient(colors:[.white.opacity(0.75),.clear],center:.topLeading,startRadius:0,endRadius:geometry.size.width*0.72)
                }.opacity(warmth)
                LinearGradient(colors:[.white.opacity(0.10),.clear,.black.opacity(0.10)],startPoint:.top,endPoint:.bottom)
            }
        }.allowsHitTesting(false).accessibilityHidden(true)
    }
}

private struct BrightnessTicks: View {
    var value: Double
    var body: some View {
        Canvas { context,size in
            for index in 0..<41 {
                let x = CGFloat(index)*size.width/40
                let height: CGFloat = index.isMultiple(of:5) ? 12 : 5
                let rect = CGRect(x:x,y:(size.height-height)/2,width:1,height:height)
                context.fill(Path(roundedRect:rect,cornerRadius:0.5),with:.color(.white.opacity(Double(index)/40 <= value/100 ? 0.65 : 0.20)))
            }
        }.frame(height:18).accessibilityHidden(true)
    }
}

struct DotPercent: View {
    var percent: Int
    private let glyphs = [
        "0":["01110","10001","10001","10001","10001","10001","01110"],
        "1":["00100","01100","00100","00100","00100","00100","01110"],
        "2":["01110","10001","00001","00010","00100","01000","11111"],
        "3":["11110","00001","00001","01110","00001","00001","11110"],
        "4":["00010","00110","01010","10010","11111","00010","00010"],
        "5":["11111","10000","10000","11110","00001","00001","11110"],
        "6":["01110","10000","10000","11110","10001","10001","01110"],
        "7":["11111","00001","00010","00100","01000","01000","01000"],
        "8":["01110","10001","10001","01110","10001","10001","01110"],
        "9":["01110","10001","10001","01111","00001","00001","01110"],
        "%":["11001","11001","00010","00100","01000","10011","10011"]]
    var body: some View {
        Canvas { context, size in
            let text = Array("\(min(100,max(0,percent)))%")
            let spacing = min(size.width/CGFloat(text.count*6),size.height/7)
            let left = (size.width-CGFloat(text.count*6-1)*spacing)/2
            let top = (size.height-7*spacing)/2
            for (index,char) in text.enumerated() {
                for (y,row) in (glyphs[String(char)] ?? []).enumerated() {
                    for (x,pixel) in row.enumerated() where pixel == "1" {
                        let rect = CGRect(x:left+CGFloat(index*6+x)*spacing,y:top+CGFloat(y)*spacing,width:spacing*0.25,height:spacing*0.25)
                        context.fill(Path(ellipseIn:rect),with:.color(.white))
                    }
                }
            }
        }.shadow(color:.white.opacity(0.65),radius:4)
            .accessibilityLabel("\(percent)%").frame(width:180,height:78)
    }
}

struct BrightnessConsolePage: View {
    @ObservedObject var store: MonitorStore
    var integration: MacIntegration
    var theme: ConsoleTheme
    @State private var hovering = false
    private var disabled: Bool { store.busy || store.brightnessBusy || store.brightnessReading || store.brightnessTargets.isEmpty }
    var body: some View {
        HStack(spacing:12) {
            Image(systemName:"link").font(.system(size:15,weight:.medium)).foregroundStyle(theme.tint)
            Text("밝기 동기화").font(.system(size:14,weight:.medium))
            Spacer()
            Toggle("밝기 동기화",isOn:$store.linkedBrightness).labelsHidden().toggleStyle(.switch).controlSize(.small)
        }.padding(.horizontal,20).padding(.vertical,15).consoleGlass()
        HStack(alignment:.top,spacing:18) {
            VStack(spacing:16) {
                VStack(spacing:8) {
                    Text("LINKED DISPLAYS").font(.system(size:10,weight:.medium)).tracking(2).opacity(0.75)
                    Text("함께 조절").font(.system(size:19,weight:.semibold))
                }.foregroundStyle(Color(red:0.13,green:0.25,blue:0.19))
                Spacer(minLength:24)
                Image(systemName:"sun.max").font(.system(size:21,weight:.light))
                DotPercent(percent:Int(store.groupPercent.rounded()))
                Text("전체 밝기").font(.system(size:12,weight:.medium)).opacity(0.9)
                BrightnessTicks(value:store.groupPercent).padding(.horizontal,8)
                Spacer(minLength:24)
                VStack(spacing:8) {
                    Slider(value:$store.groupPercent,in:0...100,step:1,onEditingChanged:{if !$0 { store.changeGroupBrightness() }})
                        .tint(.white).disabled(disabled).accessibilityLabel("전체 밝기")
                        .shadow(color:.white.opacity(hovering ? 0.7 : 0),radius:hovering ? 14 : 0)
                        .onHover { hovering = $0 }.animation(.easeInOut(duration:0.35),value:hovering)
                    HStack { Text("0%"); Spacer(); Text("100%") }.font(.caption2).opacity(0.85)
                }
                adjustmentControls.padding(.top,4)
            }.padding(26).frame(minWidth:240,maxWidth:.infinity,minHeight:448)
                .foregroundStyle(.white)
                .background { BrightnessAmbient(warmth:theme.warmth) }
                .clipShape(RoundedRectangle(cornerRadius:20))
                .overlay(RoundedRectangle(cornerRadius:20).strokeBorder(LinearGradient(colors:[.white.opacity(0.70),.white.opacity(0.10)],startPoint:.topLeading,endPoint:.bottomTrailing)))
                .shadow(color:theme.aura.opacity(0.18),radius:22,x:0,y:8)
            VStack(spacing:18) {
                ForEach(monitorsInRoleOrder(store.monitors)) { monitor in
                    MonitorBrightnessCard(store:store,monitor:monitor)
                }
                if store.monitors.isEmpty { Text("연결된 모니터가 없습니다.").consoleCard() }
            }.frame(minWidth:250,maxWidth:.infinity)
        }
    }
    private var adjustmentControls: some View {
        HStack(spacing:0) {
            Button { integration.stepBrightness(-1) } label: { Image(systemName:"minus").frame(width:36,height:30).contentShape(Rectangle()) }.buttonStyle(.plain).help("밝기 낮추기")
            Text("\(Int(store.groupPercent.rounded()))%").font(.system(size:16,weight:.medium)).monospacedDigit().frame(width:72,height:34)
            Button { integration.stepBrightness(1) } label: { Image(systemName:"plus").frame(width:36,height:30).contentShape(Rectangle()) }.buttonStyle(.plain).help("밝기 높이기")
        }.padding(5).consoleGlass(corner:24,interactive:true).environment(\.colorScheme,.dark).disabled(disabled)
    }
}

struct MonitorBrightnessCard: View {
    @ObservedObject var store: MonitorStore
    var monitor: Monitor
    @State private var hovering = false
    private var state: BrightnessState { store.brightness[monitor.uuid] ?? BrightnessState() }
    var body: some View {
        VStack(alignment:.leading,spacing:18) {
            HStack(spacing:12) {
                Image(systemName:monitor.builtIn ? "laptopcomputer" : "display")
                    .font(.system(size:21,weight:.light)).foregroundStyle(.tint)
                    .frame(width:42,height:42).background(.tint.opacity(0.07),in:RoundedRectangle(cornerRadius:12))
                VStack(alignment:.leading,spacing:4) {
                    Text(monitor.label(in:store.monitors)).font(.headline)
                    Text(monitor.name).font(.caption2).foregroundStyle(.secondary).lineLimit(1)
                }
                Spacer()
            }
            HStack(alignment:.firstTextBaseline) {
                HStack(spacing:5) {
                    Circle().fill(state.supported ? Color.accentColor : .secondary).frame(width:5,height:5)
                    Text(monitor.builtIn ? "내장 화면" : "DDC/CI").font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                Text(state.supported ? "\(Int(state.percent.rounded()))%" : "—")
                    .font(.system(size:30,weight:.light)).monospacedDigit().contentTransition(.numericText())
            }
            Slider(value:Binding(get:{state.percent},set:{store.brightness[monitor.uuid]?.percent = $0}),in:0...100,step:1,onEditingChanged:{if !$0 { store.changeBrightness(monitor,percent:Int(state.percent.rounded())) }})
                .disabled(!state.supported || state.busy || store.busy || store.brightnessBusy)
                .accessibilityLabel("\(monitor.label(in:store.monitors)) 밝기")
                .shadow(color:Color.accentColor.opacity(hovering ? 0.5 : 0),radius:hovering ? 12 : 0)
                .onHover { hovering = $0 }.animation(.easeInOut(duration:0.35),value:hovering)
            if !state.supported { Text(state.message).font(.caption2).foregroundStyle(.secondary) }
            HStack { Text("0%"); Spacer(); Text("100%") }.font(.caption2).foregroundStyle(.secondary).padding(.top,-8)
        }.consoleCard()
    }
}

struct DisplayProfilesPage: View {
    @ObservedObject var store: MonitorStore
    private var disabled: Bool { store.busy || store.brightnessBusy || store.brightnessReading }
    var body: some View {
        HStack(spacing:12) {
            Picker("저장 환경",selection:Binding(get:{store.selectedProfileID},set:store.selectProfile)) {
                Text("현재 연결").tag("")
                ForEach(store.profiles) { Text($0.name).tag($0.id) }
            }.frame(maxWidth:300)
            Spacer()
            Button("새 환경",systemImage:"plus") { store.requestSave(asNew:true) }
            Button("저장",systemImage:"square.and.arrow.down",action:store.saveCurrent).keyboardShortcut("s")
            Button("복구",systemImage:"arrow.counterclockwise",action:store.restoreNow).keyboardShortcut("r")
                .disabled(store.profile?.matches(store.monitors) != true)
            Button(action:store.requestDelete) { Image(systemName:"trash") }.disabled(store.profile == nil)
        }.disabled(disabled).consoleCard()
        LayoutView(monitors:store.monitors)
        LazyVGrid(columns:[GridItem(.adaptive(minimum:250),spacing:16)],spacing:16) {
            ForEach(monitorsInRoleOrder(store.monitors)) { MonitorCard(store:store,monitor:$0) }
        }
        HStack {
            Toggle("연결하면 자동 복구",isOn:$store.autoRestore).toggleStyle(.switch).controlSize(.small)
            Spacer()
            if store.busy { ProgressView().controlSize(.small) }
        }.consoleCard()
        Text(store.status).font(.callout).foregroundStyle(.secondary).textSelection(.enabled)
    }
}

struct ProtectionConsolePage: View {
    @ObservedObject var store: MonitorStore
    private func update(_ action: (inout ScreenProtectionSettings) -> Void) {
        var settings = store.screenProtectionSettings; action(&settings); store.setScreenProtection(settings)
    }
    var body: some View {
        OLEDProtectionPanel(store:store)
        VStack(spacing:20) {
            Picker("감지 방식",selection:Binding(get:{store.screenProtectionSettings.mode},set:{v in update{$0.mode = v}})) {
                Text("마우스·키보드 입력").tag("input")
                Text("화면 내용 변화").tag("content")
            }
            if store.screenProtectionSettings.mode == "content" {
                Toggle("배경화면 변화 제외",isOn:Binding(get:{store.screenProtectionSettings.ignoreWallpaper},set:{v in update{$0.ignoreWallpaper = v}}))
                if !store.oled.captureMessage.isEmpty { Text(store.oled.captureMessage).font(.callout).foregroundStyle(.orange) }
                HStack {
                    Button("화면 기록 권한") {
                        _ = CGRequestScreenCaptureAccess()
                        NSWorkspace.shared.open(URL(string:"x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture")!)
                    }
                    Button("다시 확인") { store.oled.retryCapture() }
                    Spacer()
                }
            }
        }.consoleCard()
        ForEach(store.externalMonitors) { monitor in
            HStack {
                Label(monitor.label(in:store.monitors),systemImage:"display")
                Spacer()
                Button("지금 가리기") { store.oled.toggleForced(monitor.displayID) }
                Toggle("보호 대상",isOn:Binding(get:{store.screenProtectionSettings.selectedUUIDs?.contains(monitor.uuid) ?? true},set:{ selected in
                    update { settings in
                        var ids = Set(settings.selectedUUIDs ?? store.externalMonitors.map(\.uuid))
                        if selected { ids.insert(monitor.uuid) } else { ids.remove(monitor.uuid) }
                        settings.selectedUUIDs = ids.sorted()
                    }
                })).labelsHidden().toggleStyle(.switch).controlSize(.small)
            }.consoleCard()
        }
    }
}

struct LayoutConsolePage: View {
    @ObservedObject var preferences: MacPreferences
    var body: some View {
        ForEach(ShortcutAction.allCases.filter(\.isWindowAction)) { action in
            HStack {
                Label(action.title,systemImage:"macwindow")
                Spacer()
                Text(preferences.shortcuts.first(where:{$0.action == action})?.label ?? "—").font(.system(.body,design:.monospaced)).foregroundStyle(.secondary)
            }.consoleCard()
        }
        Stepper("중앙 16:9 창 너비: \(preferences.centerPercent)%",value:$preferences.centerPercent,in:30...95).consoleCard()
        Button("단축키 설정") { preferences.page = .shortcuts }.buttonStyle(.link)
    }
}

struct ShortcutsConsolePage: View {
    @ObservedObject var preferences: MacPreferences
    var integration: MacIntegration
    @State private var draft = [MacShortcut]()
    var body: some View {
        ForEach($draft) { $binding in
            HStack(spacing:18) {
                Toggle(binding.action.title,isOn:$binding.enabled).toggleStyle(.checkbox).frame(maxWidth:.infinity,alignment:.leading)
                Picker("보조키",selection:$binding.modifiers) {
                    ForEach(MacShortcut.modifierChoices,id:\.1) { Text($0.0).tag($0.1) }
                }.labelsHidden().frame(width:132)
                Picker("키",selection:$binding.key) {
                    ForEach(MacShortcut.keyChoices,id:\.1) { Text($0.0).tag($0.1) }
                }.labelsHidden().frame(width:140)
            }.consoleCard()
        }
        Stepper("밝기 변경 간격: \(preferences.brightnessStep)%",value:$preferences.brightnessStep,in:1...20).consoleCard()
        if !preferences.shortcutError.isEmpty { Text(preferences.shortcutError).foregroundStyle(.orange).font(.callout) }
        HStack {
            Button("기본값") { draft = MacShortcut.defaults }
            Spacer()
            Button("저장") { integration.save(draft) }.buttonStyle(.borderedProminent)
        }.onAppear { draft = preferences.shortcuts }
    }
}

struct GeneralConsolePage: View {
    @ObservedObject var store: MonitorStore
    @ObservedObject var preferences: MacPreferences
    var integration: MacIntegration
    var body: some View {
        VStack(spacing:20) {
            Picker("시간별 색상",selection:$preferences.appearance) {
                Text("자동").tag("auto"); Text("아침").tag("morning"); Text("낮").tag("day")
                Text("저녁").tag("evening"); Text("밤").tag("night")
            }
            Toggle("창 뒤 배경 블러",isOn:$preferences.blur)
            Toggle("시작 시 창 숨기기",isOn:$preferences.startHidden)
            Toggle("로그인 시 실행",isOn:Binding(get:{store.loginEnabled},set:store.setLogin))
            if !store.loginMessage.isEmpty {
                Text(store.loginMessage).font(.caption).foregroundStyle(.orange)
                Button("로그인 항목 열기") { SMAppService.openSystemSettingsLoginItems() }
            }
            Toggle("모니터 연결 시 저장 환경 복구",isOn:$store.autoRestore)
        }.toggleStyle(.switch).controlSize(.small).consoleCard()
        HStack {
            Label("창 배치",systemImage:"macwindow")
            Spacer()
            Button("손쉬운 사용 권한",action:integration.requestAccessibility)
        }.consoleCard()
        HStack {
            Label("저장 환경",systemImage:"folder")
            Spacer()
            Button("저장 폴더 열기") { NSWorkspace.shared.open(store.directory) }
        }.consoleCard()
    }
}

struct HelpConsolePage: View {
    var body: some View {
        Group {
            DisclosureGroup("모니터 환경 저장·복구") {
                Text("모니터 환경에서 현재 연결에 이름을 정해 저장합니다. 배열, 주 모니터, 해상도, 주사율, 회전, 밝기와 화면 보호 설정을 함께 기억합니다. 동일한 장비 조합을 다시 연결하면 저장 환경을 복구합니다. 지원되지 않는 모드를 임의로 선택하지 않으며 적용 실패 시 이전 배열로 복귀합니다.").padding(.top,10)
            }
            DisclosureGroup("밝기") {
                Text("내장 화면은 macOS 밝기 API, 외부 화면은 DDC/CI를 사용합니다. 밝기 동기화에서 40%·60%를 5% 높이면 65%·65%, 낮추면 35%·35%가 됩니다. 동기화를 끈 단축키는 마우스가 있는 모니터를 조절합니다. 케이블·독·HDR·모니터 OSD에 따라 외부 밝기 제어가 제한될 수 있습니다.").padding(.top,10)
            }
            DisclosureGroup("화면 보호") {
                Text("입력 모드는 화면별 마우스·키보드 유휴 시간을 봅니다. 화면 내용 변화 모드는 화면 기록 권한을 사용해 메모리에서 프레임을 비교합니다. 배경화면 제외를 켜면 일반 앱 창만 비교하며 앱 창 뒤로 드러나는 배경도 제외합니다. 캡처 이미지를 저장하거나 전송하지 않습니다. 마우스가 있는 화면은 자동으로 가리지 않습니다. 설정 창을 닫으면 감시가 재개됩니다.").padding(.top,10)
            }
            DisclosureGroup("강제 가림") {
                Text("같은 단축키를 다시 누르거나 마우스가 화면에 들어오면 해제됩니다. 가릴 때 마우스가 이미 그 화면에 있었다면 밖으로 나갔다가 다시 들어오세요. 절전·잠금·화면 재연결 시 강제 가림을 해제합니다. 메뉴 막대의 ‘모든 화면 다시 표시’로도 해제할 수 있습니다.").padding(.top,10)
            }
            DisclosureGroup("창 배치·단축키") {
                Text("창 배치에는 손쉬운 사용 권한이 필요합니다. macOS 전체 화면 대신 메뉴 막대와 Dock을 제외한 작업 영역을 채웁니다. 다른 키 조합은 단축키 페이지에서 지정합니다. macOS Fn 설정에 따라 F2/F3 입력에 Fn 키가 필요할 수 있습니다. 앱의 최소 크기 제한이나 macOS가 사용하는 조합은 적용할 수 없습니다.").padding(.top,10)
            }
            DisclosureGroup("기존 Mac 앱에서 업데이트") {
                Text("기존 Dock Monitor / Monitor Shade의 번들 ID와 Application Support/DockMonitor 저장 경로를 유지합니다. profiles.json과 이전 profile.json을 읽으며 사용자 저장 환경을 배포 파일에 포함하지 않습니다. 이전 앱을 종료한 뒤 새 앱으로 교체하고 로그인 항목을 확인하세요.").padding(.top,10)
            }
        }.font(.callout).consoleCard()
    }
}
