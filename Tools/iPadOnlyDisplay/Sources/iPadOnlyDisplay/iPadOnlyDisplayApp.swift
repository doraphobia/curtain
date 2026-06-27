import AppKit
import Carbon.HIToolbox
import CoreGraphics

private let hotKeySignature: OSType = 0x4950_4144 // "IPAD"
private let hotKeyIdentifier: UInt32 = 1

private struct DisplayState {
    let activeDisplayIDs: [CGDirectDisplayID]
    let builtInScreens: [NSScreen]
    let screenNames: [String]
    let hasMirrorSet: Bool

    var hasExternalDisplay: Bool {
        activeDisplayIDs.contains { CGDisplayIsBuiltin($0) == 0 }
    }

    static func queryActiveDisplayIDs() -> [CGDirectDisplayID] {
        var count: UInt32 = 0
        CGGetActiveDisplayList(0, nil, &count)

        var displayIDs = Array(repeating: CGDirectDisplayID(), count: Int(count))
        if count > 0 {
            displayIDs.withUnsafeMutableBufferPointer { buffer in
                _ = CGGetActiveDisplayList(count, buffer.baseAddress, &count)
            }
            displayIDs = Array(displayIDs.prefix(Int(count)))
        }

        return displayIDs
    }

    static func current() -> DisplayState {
        let displayIDs = queryActiveDisplayIDs()

        let screens = NSScreen.screens
        let builtIn = screens.filter { screen in
            guard let displayID = screen.displayID else { return false }
            return CGDisplayIsBuiltin(displayID) != 0
        }

        return DisplayState(
            activeDisplayIDs: displayIDs,
            builtInScreens: builtIn,
            screenNames: screens.map(\.localizedName),
            hasMirrorSet: displayIDs.contains { CGDisplayIsInMirrorSet($0) != 0 }
        )
    }
}

private extension NSScreen {
    var displayID: CGDirectDisplayID? {
        let key = NSDeviceDescriptionKey("NSScreenNumber")
        guard let number = deviceDescription[key] as? NSNumber else { return nil }
        return CGDirectDisplayID(number.uint32Value)
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem?
    private var toggleItem: NSMenuItem?
    private var displaySummaryItem: NSMenuItem?
    private var blackoutWindows: [NSWindow] = []
    private var activity: NSObjectProtocol?
    private var hotKeyRef: EventHotKeyRef?
    private var hotKeyHandlerRef: EventHandlerRef?
    private var screenObserver: NSObjectProtocol?
    private var modeEnabled = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        configureMenuBar()
        registerGlobalHotKey()
        updateMenu()

        screenObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor in
                self?.handleScreenChange()
            }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        disableMode()
        if let hotKeyRef {
            UnregisterEventHotKey(hotKeyRef)
        }
        if let hotKeyHandlerRef {
            RemoveEventHandler(hotKeyHandlerRef)
        }
        if let screenObserver {
            NotificationCenter.default.removeObserver(screenObserver)
        }
    }

    private func configureMenuBar() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = NSImage(
            systemSymbolName: "ipad.and.arrow.forward",
            accessibilityDescription: "仅 iPad 可见"
        )

        let menu = NSMenu()

        let summary = NSMenuItem(title: "正在检测显示器…", action: nil, keyEquivalent: "")
        summary.isEnabled = false
        menu.addItem(summary)
        displaySummaryItem = summary

        menu.addItem(.separator())

        let toggle = NSMenuItem(
            title: "进入仅 iPad 可见模式",
            action: #selector(toggleMode),
            keyEquivalent: "i"
        )
        toggle.keyEquivalentModifierMask = [.control, .option, .command]
        toggle.target = self
        menu.addItem(toggle)
        toggleItem = toggle

        let settings = NSMenuItem(
            title: "打开“显示器”设置…",
            action: #selector(openDisplaySettings),
            keyEquivalent: ","
        )
        settings.target = self
        menu.addItem(settings)

        menu.addItem(.separator())

        let quit = NSMenuItem(title: "退出", action: #selector(quitApplication), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)

        item.menu = menu
        statusItem = item
    }

    @objc private func toggleMode() {
        modeEnabled ? disableMode() : enableMode()
    }

    private func enableMode() {
        let state = DisplayState.current()

        guard state.hasExternalDisplay else {
            showError(
                title: "还没有检测到 iPad",
                message: "请先在“系统设置 → 显示器 → 添加显示器”中连接 iPad，再重试。USB 连接通常更稳定。"
            )
            return
        }

        guard !state.hasMirrorSet else {
            showError(
                title: "请先切换到扩展显示",
                message: "当前处于镜像模式。若现在遮黑 Mac 屏幕，iPad 也会一起变黑。请在“显示器”设置中将 iPad 的“用作”改为“扩展显示器”。"
            )
            return
        }

        guard !state.builtInScreens.isEmpty else {
            showError(
                title: "没有找到 Mac 内建屏幕",
                message: "这个模式只会遮黑 MacBook 的内建屏幕，不会修改外接显示器。"
            )
            return
        }

        blackoutWindows = state.builtInScreens.map(makeBlackoutWindow(for:))
        blackoutWindows.forEach { $0.orderFrontRegardless() }

        activity = ProcessInfo.processInfo.beginActivity(
            options: [.idleSystemSleepDisabled, .idleDisplaySleepDisabled, .userInitiated],
            reason: "仅 iPad 可见模式正在使用"
        )

        modeEnabled = true
        updateMenu()
    }

    private func disableMode() {
        blackoutWindows.forEach { $0.close() }
        blackoutWindows.removeAll()

        if let activity {
            ProcessInfo.processInfo.endActivity(activity)
            self.activity = nil
        }

        modeEnabled = false
        updateMenu()
    }

    private func makeBlackoutWindow(for screen: NSScreen) -> NSWindow {
        let window = NSWindow(
            contentRect: screen.frame,
            styleMask: [.borderless],
            backing: .buffered,
            defer: false,
            screen: screen
        )
        window.backgroundColor = .black
        window.isOpaque = true
        window.hasShadow = false
        window.ignoresMouseEvents = true
        window.hidesOnDeactivate = false
        window.isReleasedWhenClosed = false
        window.level = .screenSaver
        window.collectionBehavior = [
            .canJoinAllSpaces,
            .fullScreenAuxiliary,
            .stationary,
            .ignoresCycle
        ]
        window.setFrame(screen.frame, display: true)
        return window
    }

    private func handleScreenChange() {
        guard modeEnabled else {
            updateMenu()
            return
        }

        let state = DisplayState.current()
        guard state.hasExternalDisplay, !state.hasMirrorSet else {
            disableMode()
            return
        }

        blackoutWindows.forEach { $0.close() }
        blackoutWindows = state.builtInScreens.map(makeBlackoutWindow(for:))
        blackoutWindows.forEach { $0.orderFrontRegardless() }
        updateMenu()
    }

    private func updateMenu() {
        let state = DisplayState.current()
        let names = state.screenNames.isEmpty ? "未检测到显示器" : state.screenNames.joined(separator: "、")
        displaySummaryItem?.title = "显示器：\(names)"
        toggleItem?.title = modeEnabled ? "恢复 Mac 屏幕" : "进入仅 iPad 可见模式"
        toggleItem?.state = modeEnabled ? .on : .off
        statusItem?.button?.contentTintColor = modeEnabled ? .systemGreen : nil
        statusItem?.button?.toolTip = modeEnabled
            ? "仅 iPad 可见模式已开启；按 ⌃⌥⌘I 恢复"
            : "仅 iPad 可见"
    }

    private func registerGlobalHotKey() {
        var eventType = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: UInt32(kEventHotKeyPressed)
        )

        let userData = UnsafeMutableRawPointer(Unmanaged.passUnretained(self).toOpaque())
        InstallEventHandler(
            GetApplicationEventTarget(),
            { _, _, userData in
                guard let userData else { return OSStatus(eventNotHandledErr) }
                let appDelegate = Unmanaged<AppDelegate>.fromOpaque(userData).takeUnretainedValue()
                Task { @MainActor in
                    appDelegate.toggleMode()
                }
                return noErr
            },
            1,
            &eventType,
            userData,
            &hotKeyHandlerRef
        )

        let hotKeyID = EventHotKeyID(signature: hotKeySignature, id: hotKeyIdentifier)
        RegisterEventHotKey(
            UInt32(kVK_ANSI_I),
            UInt32(controlKey | optionKey | cmdKey),
            hotKeyID,
            GetApplicationEventTarget(),
            0,
            &hotKeyRef
        )
    }

    private func showError(title: String, message: String) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = title
        alert.informativeText = message
        alert.addButton(withTitle: "好")
        alert.addButton(withTitle: "打开显示器设置")

        NSApp.activate(ignoringOtherApps: true)
        if alert.runModal() == .alertSecondButtonReturn {
            openDisplaySettings()
        }
    }

    @objc private func openDisplaySettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.Displays-Settings.extension"),
           NSWorkspace.shared.open(url) {
            return
        }

        NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Applications/System Settings.app"))
    }

    @objc private func quitApplication() {
        NSApp.terminate(nil)
    }
}

@main
enum iPadOnlyDisplayApp {
    @MainActor
    static func main() {
        if CommandLine.arguments.contains("--diagnose") {
            let displayIDs = DisplayState.queryActiveDisplayIDs()
            let builtInCount = displayIDs.filter { CGDisplayIsBuiltin($0) != 0 }.count
            let hasExternalDisplay = displayIDs.contains { CGDisplayIsBuiltin($0) == 0 }
            let hasMirrorSet = displayIDs.contains { CGDisplayIsInMirrorSet($0) != 0 }
            print("activeDisplays=\(displayIDs.count)")
            print("externalDisplayConnected=\(hasExternalDisplay)")
            print("mirroring=\(hasMirrorSet)")
            print("builtInDisplays=\(builtInCount)")
            return
        }

        let application = NSApplication.shared
        let delegate = AppDelegate()
        application.delegate = delegate
        application.run()
    }
}
