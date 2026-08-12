import AppKit

@MainActor
final class CompanionWindowController: NSWindowController {
    private let service: CompanionService
    private let sendCurrentGIF: () async throws -> Void
    private let stateChanged: () -> Void
    private let status = NSTextField(wrappingLabelWithString: "正在连接搭子服务…")
    private let nameField = NSTextField(string: "")
    private let codeField = NSTextField(string: "")
    private let pairField = NSTextField(string: "")
    private let pairButton = NSButton(title: "绑定搭子", target: nil, action: nil)
    private let sendButton = NSButton(title: "发送当前 GIF", target: nil, action: nil)
    private let unpairButton = NSButton(title: "解除绑定", target: nil, action: nil)
    private var loading = false

    init(service: CompanionService, sendCurrentGIF: @escaping () async throws -> Void, stateChanged: @escaping () -> Void) {
        self.service = service
        self.sendCurrentGIF = sendCurrentGIF
        self.stateChanged = stateChanged
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 520, height: 390),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        window.title = "搭子联机"
        window.isReleasedWhenClosed = false
        window.center()
        super.init(window: window)
        buildInterface(in: window)
        render()
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func showWindow(_ sender: Any?) {
        super.showWindow(sender)
        window?.makeKeyAndOrderFront(sender)
        NSApp.activate(ignoringOtherApps: true)
        refreshProfile()
    }

    private func buildInterface(in window: NSWindow) {
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 14
        stack.translatesAutoresizingMaskIntoConstraints = false
        window.contentView = NSView()
        window.contentView?.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor, constant: 28),
            stack.trailingAnchor.constraint(equalTo: window.contentView!.trailingAnchor, constant: -28),
            stack.topAnchor.constraint(equalTo: window.contentView!.topAnchor, constant: 26)
        ])

        let title = NSTextField(labelWithString: "搭子联机")
        title.font = .systemFont(ofSize: 22, weight: .bold)
        stack.addArrangedSubview(title)
        status.textColor = .secondaryLabelColor
        status.maximumNumberOfLines = 2
        status.widthAnchor.constraint(equalToConstant: 460).isActive = true
        stack.addArrangedSubview(status)

        nameField.placeholderString = "我的昵称"
        nameField.widthAnchor.constraint(equalToConstant: 300).isActive = true
        let saveName = NSButton(title: "保存昵称", target: self, action: #selector(saveNameAction))
        stack.addArrangedSubview(row(label: "昵称", controls: [nameField, saveName]))

        codeField.isEditable = false
        codeField.font = .monospacedSystemFont(ofSize: 17, weight: .medium)
        codeField.widthAnchor.constraint(equalToConstant: 300).isActive = true
        let copyCode = NSButton(title: "复制", target: self, action: #selector(copyCodeAction))
        stack.addArrangedSubview(row(label: "我的搭子码", controls: [codeField, copyCode]))

        pairField.placeholderString = "对方的 8 位搭子码"
        pairField.widthAnchor.constraint(equalToConstant: 300).isActive = true
        pairButton.target = self
        pairButton.action = #selector(pairAction)
        stack.addArrangedSubview(row(label: "绑定", controls: [pairField, pairButton]))

        sendButton.target = self
        sendButton.action = #selector(sendAction)
        unpairButton.target = self
        unpairButton.action = #selector(unpairAction)
        let actions = NSStackView(views: [sendButton, unpairButton])
        actions.orientation = .horizontal
        actions.spacing = 9
        stack.addArrangedSubview(actions)
    }

    private func row(label: String, controls: [NSView]) -> NSStackView {
        let title = NSTextField(labelWithString: label)
        title.font = .systemFont(ofSize: 13, weight: .medium)
        title.widthAnchor.constraint(equalToConstant: 90).isActive = true
        let row = NSStackView(views: [title] + controls)
        row.orientation = .horizontal
        row.alignment = .centerY
        row.spacing = 9
        return row
    }

    private func refreshProfile() {
        run { try await self.service.refreshProfile() }
    }

    private func render() {
        let profile = service.profile
        nameField.stringValue = profile?.displayName ?? nameField.stringValue
        codeField.stringValue = profile?.pairingCode ?? ""
        if let partner = profile?.partner {
            status.stringValue = "已和 \(partner.displayName) 绑定"
            pairField.isHidden = true
            pairButton.isHidden = true
            sendButton.isHidden = false
            unpairButton.isHidden = false
        } else {
            status.stringValue = profile == nil ? "正在连接搭子服务…" : "分享搭子码，或输入对方的搭子码"
            pairField.isHidden = false
            pairButton.isHidden = false
            sendButton.isHidden = true
            unpairButton.isHidden = true
        }
        for control in [nameField, codeField, pairField, pairButton, sendButton, unpairButton] {
            control.isEnabled = !loading
        }
    }

    private func run<T>(_ action: @escaping () async throws -> T) {
        guard !loading else { return }
        loading = true
        render()
        Task { @MainActor [weak self] in
            guard let self else { return }
            defer {
                loading = false
                render()
                stateChanged()
            }
            do { _ = try await action() }
            catch { presentFriendlyError(error, title: "搭子联机") }
        }
    }

    @objc private func saveNameAction() {
        let name = nameField.stringValue
        run { try await self.service.updateName(name) }
    }

    @objc private func copyCodeAction() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(codeField.stringValue, forType: .string)
    }

    @objc private func pairAction() {
        let code = pairField.stringValue
        run { try await self.service.pair(code: code) }
    }

    @objc private func sendAction() {
        run { try await self.sendCurrentGIF() }
    }

    @objc private func unpairAction() {
        let alert = NSAlert()
        alert.messageText = "解除搭子绑定？"
        alert.informativeText = "双方之后都不能继续投递 GIF。"
        alert.addButton(withTitle: "解除绑定")
        alert.addButton(withTitle: "取消")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        run { try await self.service.unpair() }
    }
}
