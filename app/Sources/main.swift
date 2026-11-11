// aADHD (anti-ADHD) — 맥 메뉴 막대 뽀모도로 앱.
// 메뉴 막대 표시와 패널, 창 관리, 전역 단축키(⌥⇧L), 로컬 서버와 브라우저 감시 시작을 담당한다.

import AppKit
import Carbon.HIToolbox
import Combine
import ServiceManagement
import SwiftUI
import UserNotifications

// MARK: - 창 관리

final class Windows: NSObject, NSWindowDelegate {
    static let shared = Windows()
    private var windows: [String: NSWindow] = [:]
    private var lastOtherApp: NSRunningApplication? // 창을 다 닫으면 이 앱으로 돌아간다

    override init() {
        super.init()
        if let front = NSWorkspace.shared.frontmostApplication, front != .current { lastOtherApp = front }
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main) { [weak self] note in
            guard let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication, app != .current else { return }
            self?.lastOtherApp = app
        }
    }

    /// height: 처음 열 때의 높이 (스크롤되는 긴 창용). 주면 그 뒤로는 사용자가 크기를 조절할 수 있다.
    func show<V: View>(_ id: String, title: String, floating: Bool = false, resizable: Bool = false,
                       height: CGFloat? = nil, @ViewBuilder content: () -> V) {
        let root = content().environmentObject(Store.shared)
        let w: NSWindow
        let wasVisible = windows[id]?.isVisible == true
        func host() -> NSViewController {
            let h = NSHostingController(rootView: root)
            if height != nil { h.sizingOptions = [.minSize] } // 내용 크기에 창을 맞추지 않는다 (스크롤)
            return h
        }
        if let existing = windows[id] {
            w = existing
            w.contentViewController = host()
        } else {
            var style: NSWindow.StyleMask = [.titled, .closable]
            if resizable { style.insert(.resizable) }
            w = NSWindow(contentRect: .zero, styleMask: style, backing: .buffered, defer: false)
            w.isReleasedWhenClosed = false
            w.delegate = self
            w.contentViewController = host()
            windows[id] = w
        }
        if !wasVisible {
            // 모니터가 여러 개면 지금 마우스가 있는 화면에 연다 (같은 화면이면 지난 자리 그대로)
            let p = NSEvent.mouseLocation
            let screen = NSScreen.screens.first { NSMouseInRect(p, $0.frame, false) } ?? NSScreen.main ?? NSScreen.screens.first
            if let height {
                w.setContentSize(NSSize(width: w.frame.width, height: min(height, (screen?.visibleFrame.height ?? 800) * 0.85)))
                if let screen { center(w, on: screen) }
            } else if let screen, w.frame.origin == .zero || w.screen != screen {
                center(w, on: screen)
            }
        }
        w.title = title
        w.level = floating ? .floating : .normal
        NSApp.setActivationPolicy(.regular) // 창이 있는 동안은 Dock 과 ⌘Tab 에 보인다
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
    }

    /// NSWindow.center() 처럼 가운데보다 조금 위에 둔다 (center() 는 화면을 고를 수 없다)
    private func center(_ w: NSWindow, on screen: NSScreen) {
        let v = screen.visibleFrame, s = w.frame.size
        w.setFrameOrigin(NSPoint(x: (v.midX - s.width / 2).rounded(), y: (v.minY + (v.height - s.height) * 0.6).rounded()))
    }

    func close(_ id: String) { windows[id]?.close() }

    // 닫힌 창의 화면을 놓아준다 (안 그러면 숨은 창도 매초 다시 그려진다).
    // 열린 창이 하나도 없으면 다시 메뉴 막대 전용으로 돌아간다.
    func windowWillClose(_ note: Notification) {
        let closing = note.object as? NSWindow
        closing?.contentViewController = nil
        if !windows.values.contains(where: { $0 !== closing && $0.isVisible }) {
            NSApp.setActivationPolicy(.accessory)
            // 맥이 뒤에 있던 아무 앱(시스템 설정 등)을 앞으로 가져오지 않게, 쓰던 앱으로 돌아간다
            if let app = lastOtherApp, !app.isTerminated { app.activate() }
        }
    }
}

// MARK: - 전역 단축키

enum HotKey {
    static var handler: (() -> Void)?

    static func register() {
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
            DispatchQueue.main.async { HotKey.handler?() }
            return noErr
        }, 1, &spec, nil, nil)
        var ref: EventHotKeyRef?
        let id = EventHotKeyID(signature: OSType(0x4647_5244), id: 1) // 'FGRD'
        RegisterEventHotKey(UInt32(kVK_ANSI_L), UInt32(optionKey | shiftKey), id, GetApplicationEventTarget(), 0, &ref)
    }
}

// MARK: - 앱

final class AppDelegate: NSObject, NSApplicationDelegate, NSPopoverDelegate, UNUserNotificationCenterDelegate {
    let store = Store.shared
    let server = StateServer()
    let watcher = BrowserWatcher()
    let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    let popover = NSPopover()
    var bag = Set<AnyCancellable>()
    private var lastTitle = ""

    func applicationDidFinishLaunching(_ note: Notification) {
        if Updater.moveToNewName() { return } // 앱 이름이 바뀌었다: 새 이름 자리에서 다시 켜진다
        Updater.restoreLoginAfterMove()
        item.button?.font = .monospacedDigitSystemFont(ofSize: NSFont.systemFontSize, weight: .medium)
        item.button?.target = self
        item.button?.action = #selector(togglePopover)
        popover.behavior = .transient
        popover.delegate = self

        store.$now.sink { [weak self] now in
            self?.renderTitle()
            self?.checkNewDay(now)
        }.store(in: &bag)
        store.onPhaseChange = { [weak self] old, new in self?.phaseChanged(from: old, to: new) }
        store.onPlayOver = { [weak self] in self?.showStart() }

        UNUserNotificationCenter.current().delegate = self
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }

        HotKey.handler = { [weak self] in self?.showQuickCapture() }
        HotKey.register()
        server.start()
        watcher.onWarn = { [weak self] min, site, bid in self?.showWarning(min: min, site: site, bid: bid) }
        store.onSecond = { [weak self] in self?.watcher.tick() }
        Updater.shared.start()
        RemindersSync.shared.start(store)
        _ = Windows.shared // 쓰던 앱을 지금부터 기억한다
        if store.isBlocking { watcher.sweepAllTabs() }
        renderTitle()
    }

    func applicationWillTerminate(_ note: Notification) { store.saveNow() }

    // 앱이 앞에 있어도 알림 배너를 보여준다
    func userNotificationCenter(_ c: UNUserNotificationCenter, willPresent n: UNNotification,
                                withCompletionHandler done: @escaping (UNNotificationPresentationOptions) -> Void) {
        done([.banner, .sound])
    }

    // MARK: 메뉴 막대 표시

    func renderTitle() {
        guard let b = item.button else { return }
        let title: String
        if let left = store.playLeft {
            title = left > 0 ? "🎮 \(Int((left / 60).rounded(.up)))" : "🎮 +\(Int(-left / 60))" // 넘긴 시간
        } else if store.phase == .idle {
            title = "🍅"
        } else {
            title = "\(store.isPaused ? "⏸" : store.phase.icon) \(Int((store.remaining / 60).rounded(.up)))"
        }
        guard title != lastTitle else { return }
        lastTitle = title
        b.title = title
        b.toolTip = store.timer.task.isEmpty ? "aADHD" : "📌 \(store.timer.task)"
    }

    // MARK: 메뉴 막대 패널

    @objc func togglePopover() {
        guard let button = item.button else { return }
        if popover.isShown {
            popover.performClose(nil)
            return
        }
        let host = NSHostingController(rootView: PopoverView(
            primary: { [weak self] in self?.popover.performClose(nil); self?.primaryAction() },
            togglePause: { [weak self] in self?.togglePause() },
            openApp: { [weak self] in self?.popover.performClose(nil); self?.showDashboard() },
            openUpdate: { [weak self] in self?.popover.performClose(nil); self?.showUpdate() },
            close: { [weak self] in self?.popover.performClose(nil) }
        ).environmentObject(store))
        host.sizingOptions = .preferredContentSize
        popover.contentViewController = host
        NSApp.activate(ignoringOtherApps: true)
        popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        popover.contentViewController?.view.window?.makeKey()
    }

    // 닫힌 패널의 화면을 놓아준다
    func popoverDidClose(_ note: Notification) { popover.contentViewController = nil }

    /// 단계별 시작/중지 버튼 동작
    func primaryAction() {
        switch store.phase {
        case .idle: showStart()
        case .focus: showGiveUp()
        case .wrap: finishWrap()
        case .rest: skipBreak()
        }
    }

    // MARK: 동작

    /// 아침 6시 이후 처음 맥을 쓸 때 할 일 창을 하루 한 번 띄운다 (설정에서 끌 수 있음).
    func checkNewDay(_ now: Date) {
        if store.shouldShowMorningPlan(at: now) { showPlan() }
    }

    func showPlan() {
        showPlan(day: dayKey())
    }

    func showPlan(day: String) {
        Windows.shared.show("plan", title: L("할 일", "To-dos")) { PlanView(day: day) }
    }

    func phaseChanged(from old: Phase, to new: Phase) {
        renderTitle()
        switch new {
        case .focus: watcher.sweepAllTabs() // 미리 열어둔 분산 사이트 탭도 막는다
        case .wrap: showWrap()
        case .rest: Windows.shared.close("wrap")
        case .idle where old == .rest: showStart() // 휴식 끝나면 바로 다음 할 일을 묻는다
        default: break
        }
    }

    func showStart() {
        let last = store.today.log.last?.task ?? ""
        Windows.shared.show("start", title: L("집중 시작", "Start Focus"), floating: true) {
            StartView(task: last) { Windows.shared.close("start") }
        }
    }

    func showWrap() {
        Windows.shared.show("wrap", title: L("정리", "Wrap-up"), floating: true) {
            WrapView { Windows.shared.close("wrap") }
        }
    }

    func showQuickCapture() {
        Windows.shared.show("capture", title: L("나중에 할 것", "Later"), floating: true) {
            QuickCaptureView { Windows.shared.close("capture") }
        }
    }

    func showWarning(min: Int, site: String, bid: String) {
        Windows.shared.show("warn", title: L("딴짓 경고", "Distraction Alert"), floating: true) {
            WarnView(min: min, site: site,
                     closeTab: { [weak self] in self?.watcher.closeFrontTab(bid: bid, ifStillOn: site); Windows.shared.close("warn") },
                     keep: { Windows.shared.close("warn") })
        }
    }

    func showStats() {
        Windows.shared.show("stats", title: L("학습 통계", "Study Stats"), resizable: true) { StatsView() }
    }

    func showUpdate() {
        Windows.shared.show("update", title: L("업데이트", "Update")) { UpdateView { Windows.shared.close("update") } }
    }

    func showGiveUp() { presentGiveUp(thenQuit: false) }

    func presentGiveUp(thenQuit: Bool) {
        Windows.shared.show("giveup", title: L("포기하기", "Give Up"), floating: true) {
            GiveUpView(onGiveUp: { [weak self] in
                self?.store.stop()
                if thenQuit { NSApp.terminate(nil) }
            }, close: { Windows.shared.close("giveup") })
        }
    }

    func showDashboard() {
        Windows.shared.show("dashboard", title: "aADHD", resizable: true) {
            DashboardView(openPlan: { [weak self] in self?.showPlan(day: $0) },
                          openSettings: { [weak self] in self?.showSettings() },
                          openStats: { [weak self] in self?.showStats() },
                          quit: { [weak self] in self?.quit() },
                          openUpdate: { [weak self] in self?.showUpdate() })
        }
    }

    func showSettings() {
        Windows.shared.show("settings", title: L("aADHD 설정", "aADHD Settings"), resizable: true, height: 760) {
            SettingsView(chooseFolder: { [weak self] in self?.chooseFolder() },
                         revealFolder: { [weak self] in self?.revealFolder() },
                         toggleLogin: { [weak self] in self?.toggleLogin() },
                         uninstall: { [weak self] in self?.uninstall() })
        }
    }

    func togglePause() { store.isPaused ? store.resume() : store.pause(); renderTitle() }
    func finishWrap() { store.finishWrap(); Windows.shared.close("wrap") }
    func skipBreak() { store.stop() }

    // 집중·정리 중에 앱을 끄면 차단이 풀리므로 포기 절차를 거치게 한다.
    func quit() {
        if store.isBlocking {
            presentGiveUp(thenQuit: true)
        } else {
            NSApp.terminate(nil)
        }
    }

    func toggleLogin() {
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            alert(L("자동 실행 설정에 실패했어요", "Couldn't change launch at login"), L("시스템 설정 → 일반 → 로그인 항목에서 aADHD를 직접 추가할 수 있어요.\n\(error.localizedDescription)", "You can add aADHD yourself in System Settings → General → Login Items.\n\(error.localizedDescription)"))
        }
    }

    /// 앱을 휴지통으로 옮기고 종료한다. 원하면 기록 폴더도 휴지통으로 옮긴다 (되살릴 수 있음).
    func uninstall() {
        let a = NSAlert()
        a.messageText = L("aADHD를 삭제할까요?", "Delete aADHD?")
        a.informativeText = L("앱을 휴지통으로 옮기고 종료해요. 로그인 시 자동 실행도 꺼져요.", "This moves the app to the Trash and quits it. Launch at login will also be turned off.")
        let withData = NSButton(checkboxWithTitle: L("기록도 함께 휴지통으로 옮기기\n(\(store.dataDir.path))", "Also move my data to the Trash\n(\(store.dataDir.path))"), target: nil, action: nil)
        a.accessoryView = withData
        a.addButton(withTitle: L("삭제", "Delete"))
        a.addButton(withTitle: L("취소", "Cancel"))
        NSApp.activate(ignoringOtherApps: true)
        guard a.runModal() == .alertFirstButtonReturn else { return }

        try? SMAppService.mainApp.unregister()
        if withData.state == .on {
            store.stopSaving() // 종료하면서 기록을 다시 만들지 않게
            NSWorkspace.shared.recycle([store.dataDir]) { _, _ in }
            UserDefaults.standard.removePersistentDomain(forName: Bundle.main.bundleIdentifier ?? "")
        } else {
            store.saveNow()
        }
        NSWorkspace.shared.recycle([Bundle.main.bundleURL]) { _, error in
            if let error { self.alert(L("앱을 휴지통으로 옮기지 못했어요", "Couldn't move the app to the Trash"), error.localizedDescription) }
            NSApp.terminate(nil)
        }
    }

    func chooseFolder() {
        let p = NSOpenPanel()
        p.canChooseDirectories = true
        p.canChooseFiles = false
        p.canCreateDirectories = true
        p.prompt = L("이 폴더에 저장", "Save Here")
        NSApp.activate(ignoringOtherApps: true)
        if p.runModal() == .OK, let url = p.url, let err = store.changeDataDir(to: url) {
            alert(L("저장 위치를 바꾸지 않았어요", "Storage location unchanged"), err)
        }
    }

    func revealFolder() {
        store.saveNow()
        NSWorkspace.shared.activateFileViewerSelecting([store.dataURL])
    }

    func alert(_ title: String, _ text: String) {
        let a = NSAlert()
        a.messageText = title
        a.informativeText = text
        NSApp.activate(ignoringOtherApps: true)
        a.runModal()
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory) // Dock에 아이콘 없이 메뉴 막대에만
app.run()
