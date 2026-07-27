import AppKit

final class AppDelegate: NSObject, NSApplicationDelegate {
    private let settingsStore = SettingsStore()
    private var settings = AppSettings()
    private var petController: PetWindowController!
    private var licenses: LicenseService!
    private var updates: UpdateService!
    private var settingsWindow: SettingsWindowController?
    private var statusItem: NSStatusItem!
    private var visibilityItem: NSMenuItem!
    private var mouseItem: NSMenuItem!
    private var movementItem: NSMenuItem!
    private var randomPetItem: NSMenuItem!
    private var randomizeNowItem: NSMenuItem!

    func applicationDidFinishLaunching(_ notification: Notification) {
        do {
            settings = settingsStore.load()
            licenses = try LicenseService()
            updates = UpdateService(licenses: licenses)
            petController = PetWindowController(settings: settings) { [weak self] settings in
                self?.settings = settings
                self?.settingsStore.save(settings)
                self?.refreshMenuState()
            }
            applyDockVisibility(settings.dockIconVisible)
            configureStatusMenu()

            if !licenses.isActivated {
                Task { @MainActor [weak self] in
                    guard let self else { return }
                    guard await ActivationPrompts.activate(licenses: licenses, required: true) else {
                        NSApplication.shared.terminate(nil)
                        return
                    }
                    startPet()
                }
            } else {
                startPet()
            }
        } catch {
            NSAlert(error: error).runModal()
            NSApplication.shared.terminate(nil)
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showSettings()
        return true
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    private func startPet() {
        petController.show()
        Task { @MainActor [weak self] in
            guard let self else { return }
            try? await Task.sleep(for: .seconds(3))
            _ = try? await updates.check()
            if let manifest = updates.availableManifest {
                petController.showBubble("发现新版本 v\(manifest.version)，可在设置中安装。")
            }
        }
    }

    private func configureStatusMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = NSImage(systemSymbolName: "sparkles", accessibilityDescription: "桌搭子")
        statusItem.button?.toolTip = "桌搭子"

        let menu = NSMenu()
        menu.addItem(withTitle: "打开设置", action: #selector(openSettings), keyEquivalent: ",")
        menu.addItem(withTitle: "检查更新", action: #selector(checkUpdates), keyEquivalent: "")
        menu.addItem(.separator())
        visibilityItem = menu.addItem(withTitle: "隐藏桌搭子", action: #selector(toggleVisibility(_:)), keyEquivalent: "")
        mouseItem = menu.addItem(withTitle: "跟随鼠标", action: #selector(toggleMouseInteraction(_:)), keyEquivalent: "")
        movementItem = menu.addItem(withTitle: "随机移动", action: #selector(toggleRandomMovement(_:)), keyEquivalent: "")
        menu.addItem(.separator())
        randomPetItem = menu.addItem(withTitle: "自动随机换宠", action: #selector(toggleRandomPet(_:)), keyEquivalent: "")
        randomizeNowItem = menu.addItem(withTitle: "立即换一只", action: #selector(randomizePet(_:)), keyEquivalent: "")
        menu.addItem(.separator())
        menu.addItem(withTitle: "退出桌搭子", action: #selector(quit), keyEquivalent: "q")

        for item in menu.items where item.action != nil {
            item.target = self
        }
        statusItem.menu = menu
        refreshMenuState()
    }

    private func refreshMenuState() {
        guard petController != nil, visibilityItem != nil else { return }
        visibilityItem.title = petController.isVisible ? "隐藏桌搭子" : "显示桌搭子"
        mouseItem.state = petController.mouseInteractionEnabled ? .on : .off
        movementItem.state = petController.randomMovementEnabled ? .on : .off
        randomPetItem.state = petController.randomPetEnabled ? .on : .off
        randomPetItem.isEnabled = petController.canRandomizePet
        randomizeNowItem.isEnabled = petController.canRandomizePet
    }

    private func applyDockVisibility(_ visible: Bool) {
        NSApplication.shared.setActivationPolicy(visible ? .regular : .accessory)
    }

    private func showSettings(checkForUpdates: Bool = false) {
        if settingsWindow == nil {
            settingsWindow = SettingsWindowController(
                petController: petController,
                licenses: licenses,
                updates: updates,
                dockVisibilityChanged: { [weak self] visible in self?.applyDockVisibility(visible) }
            )
        }
        settingsWindow?.showWindow(nil)
        if checkForUpdates {
            settingsWindow?.checkForUpdatesFromMenu()
        }
    }

    @objc private func openSettings() {
        showSettings()
    }

    @objc private func checkUpdates() {
        showSettings(checkForUpdates: true)
    }

    @objc private func toggleVisibility(_ sender: NSMenuItem) {
        if petController.isVisible { petController.hide() } else { petController.show() }
        refreshMenuState()
    }

    @objc private func toggleMouseInteraction(_ sender: NSMenuItem) {
        petController.mouseInteractionEnabled.toggle()
        refreshMenuState()
    }

    @objc private func toggleRandomMovement(_ sender: NSMenuItem) {
        petController.randomMovementEnabled.toggle()
        refreshMenuState()
    }

    @objc private func toggleRandomPet(_ sender: NSMenuItem) {
        petController.randomPetEnabled.toggle()
        refreshMenuState()
    }

    @objc private func randomizePet(_ sender: NSMenuItem) {
        _ = petController.randomizePet()
    }

    @objc private func quit() {
        NSApplication.shared.terminate(nil)
    }
}
