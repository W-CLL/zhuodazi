import AppKit

@MainActor
enum ActivationPrompts {
    static func activate(licenses: LicenseService, required: Bool, replacingExisting: Bool = false) async -> Bool {
        while true {
            let input = NSTextField(string: "")
            input.placeholderString = "6 位激活码"
            input.font = .monospacedSystemFont(ofSize: 18, weight: .medium)
            input.alignment = .center
            input.frame = NSRect(x: 0, y: 0, width: 260, height: 30)

            let alert = NSAlert()
            alert.messageText = required ? "激活桌搭子" : "重新激活设备"
            alert.informativeText = required
                ? "请输入激活码后继续使用桌搭子。"
                : "输入新的激活码以更新此设备的授权。"
            alert.accessoryView = input
            alert.addButton(withTitle: "激活")
            alert.addButton(withTitle: required ? "退出" : "取消")
            let result = alert.runModal()
            guard result == .alertFirstButtonReturn else { return false }

            do {
                try await licenses.activate(input.stringValue, replacingExisting: replacingExisting)
                let success = NSAlert()
                success.messageText = "设备已激活"
                success.informativeText = licenses.summary
                success.addButton(withTitle: "完成")
                success.runModal()
                return true
            } catch {
                let failure = NSAlert(error: error)
                failure.runModal()
            }
        }
    }
}

final class SettingsWindowController: NSWindowController {
    private let petController: PetWindowController
    private let licenses: LicenseService
    private let updates: UpdateService
    private let dockVisibilityChanged: (Bool) -> Void
    private let mouseCheckbox = NSButton(checkboxWithTitle: "跟随鼠标", target: nil, action: nil)
    private let movementCheckbox = NSButton(checkboxWithTitle: "随机移动", target: nil, action: nil)
    private let randomPetCheckbox = NSButton(checkboxWithTitle: "自动随机换宠", target: nil, action: nil)
    private let alwaysOnTopCheckbox = NSButton(checkboxWithTitle: "始终置顶", target: nil, action: nil)
    private let dockCheckbox = NSButton(checkboxWithTitle: "在 Dock 显示应用图标", target: nil, action: nil)
    private let activationLabel = NSTextField(labelWithString: "")
    private let updateLabel = NSTextField(labelWithString: "")
    private let updateButton = NSButton(title: "检查更新", target: nil, action: nil)

    init(
        petController: PetWindowController,
        licenses: LicenseService,
        updates: UpdateService,
        dockVisibilityChanged: @escaping (Bool) -> Void
    ) {
        self.petController = petController
        self.licenses = licenses
        self.updates = updates
        self.dockVisibilityChanged = dockVisibilityChanged

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 430, height: 420),
            styleMask: [.titled, .closable, .miniaturizable],
            backing: .buffered,
            defer: false
        )
        window.title = "桌搭子设置"
        window.isReleasedWhenClosed = false
        window.center()
        super.init(window: window)
        buildInterface(in: window)
        refresh()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func showWindow(_ sender: Any?) {
        refresh()
        super.showWindow(sender)
        window?.makeKeyAndOrderFront(sender)
        NSApp.activate(ignoringOtherApps: true)
    }

    private func buildInterface(in window: NSWindow) {
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 10
        stack.edgeInsets = NSEdgeInsets(top: 22, left: 24, bottom: 22, right: 24)
        window.contentView = stack

        let title = NSTextField(labelWithString: "桌宠行为")
        title.font = .systemFont(ofSize: 16, weight: .semibold)
        stack.addArrangedSubview(title)

        for checkbox in [mouseCheckbox, movementCheckbox, randomPetCheckbox, alwaysOnTopCheckbox, dockCheckbox] {
            checkbox.target = self
            checkbox.action = #selector(changeSettings(_:))
            stack.addArrangedSubview(checkbox)
        }

        stack.addArrangedSubview(separator())
        let licenseTitle = NSTextField(labelWithString: "设备授权")
        licenseTitle.font = .systemFont(ofSize: 16, weight: .semibold)
        stack.addArrangedSubview(licenseTitle)
        activationLabel.textColor = .secondaryLabelColor
        stack.addArrangedSubview(activationLabel)
        let activationButton = NSButton(title: "激活设备", target: self, action: #selector(activateDevice))
        stack.addArrangedSubview(activationButton)

        stack.addArrangedSubview(separator())
        let updateTitle = NSTextField(labelWithString: "软件更新")
        updateTitle.font = .systemFont(ofSize: 16, weight: .semibold)
        stack.addArrangedSubview(updateTitle)
        updateLabel.textColor = .secondaryLabelColor
        stack.addArrangedSubview(updateLabel)
        updateButton.target = self
        updateButton.action = #selector(checkForUpdates)
        stack.addArrangedSubview(updateButton)
    }

    private func separator() -> NSBox {
        let box = NSBox()
        box.boxType = .separator
        box.translatesAutoresizingMaskIntoConstraints = false
        box.widthAnchor.constraint(equalToConstant: 380).isActive = true
        return box
    }

    private func refresh() {
        let settings = petController.currentSettings
        mouseCheckbox.state = settings.mouseInteractionEnabled ? .on : .off
        movementCheckbox.state = settings.randomMovementEnabled ? .on : .off
        randomPetCheckbox.state = settings.randomPetEnabled ? .on : .off
        alwaysOnTopCheckbox.state = settings.alwaysOnTop ? .on : .off
        dockCheckbox.state = settings.dockIconVisible ? .on : .off
        activationLabel.stringValue = licenses.summary
        updateLabel.stringValue = "当前版本 v\(AppVersion.current) - \(updates.status.message)"
    }

    @objc private func changeSettings(_ sender: NSButton) {
        var settings = petController.currentSettings
        let enabled = sender.state == .on
        switch sender {
        case mouseCheckbox: settings.mouseInteractionEnabled = enabled
        case movementCheckbox: settings.randomMovementEnabled = enabled
        case randomPetCheckbox: settings.randomPetEnabled = enabled
        case alwaysOnTopCheckbox: settings.alwaysOnTop = enabled
        case dockCheckbox:
            settings.dockIconVisible = enabled
            dockVisibilityChanged(enabled)
        default: return
        }
        petController.apply(settings)
    }

    @objc private func activateDevice() {
        Task { @MainActor [weak self] in
            guard let self else { return }
            if licenses.isActivated {
                let confirm = NSAlert()
                confirm.messageText = "更换设备授权"
                confirm.informativeText = "需要输入新的激活码。当前设备会使用新的授权记录。"
                confirm.addButton(withTitle: "继续")
                confirm.addButton(withTitle: "取消")
                guard confirm.runModal() == .alertFirstButtonReturn else { return }
            }
            _ = await ActivationPrompts.activate(
                licenses: licenses,
                required: false,
                replacingExisting: licenses.isActivated
            )
            refresh()
        }
    }

    @objc private func checkForUpdates() {
        updateButton.isEnabled = false
        updateLabel.stringValue = "当前版本 v\(AppVersion.current) - 正在检查更新"
        Task { @MainActor [weak self] in
            guard let self else { return }
            defer {
                updateButton.isEnabled = true
                refresh()
            }
            do {
                guard let manifest = try await updates.check() else {
                    let alert = NSAlert()
                    alert.messageText = "已经是最新版本"
                    alert.addButton(withTitle: "完成")
                    alert.runModal()
                    return
                }
                let alert = NSAlert()
                alert.messageText = "发现新版本 v\(manifest.version)"
                alert.informativeText = manifest.notes.isEmpty ? "更新包已通过签名校验。" : manifest.notes
                alert.addButton(withTitle: "下载并安装")
                alert.addButton(withTitle: "稍后")
                guard alert.runModal() == .alertFirstButtonReturn else { return }
                try await updates.downloadAndInstall(manifest)
            } catch {
                NSAlert(error: error).runModal()
            }
        }
    }

    @objc func checkForUpdatesFromMenu() {
        checkForUpdates()
    }
}
