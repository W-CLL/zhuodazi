import AppKit

@MainActor
final class HallWindowController: NSWindowController, NSTextFieldDelegate {
    private let service: CompanionService
    private let hasAccess: () -> Bool
    private let isTrial: () -> Bool
    private let hallAvailable: () -> Bool
    private let currentGIFURL: () -> URL?
    private let stateChanged: () -> Void
    private let nickname = NSTextField(string: "")
    private let saveName = NSButton(title: "保存昵称", target: nil, action: nil)
    private let join = NSButton(title: "加入大厅", target: nil, action: nil)
    private let refreshButton = NSButton(title: "刷新", target: nil, action: nil)
    private let onlineCount = NSTextField(labelWithString: "尚未加入")
    private let accessLabel = NSTextField(labelWithString: "")
    private let status = NSTextField(wrappingLabelWithString: "正在连接大厅…")
    private let peopleStack = NSStackView()
    private var memberRows: [HallMemberButton] = []
    private var memberListSignature: [String] = []
    private let preview = NSImageView()
    private let previewLabel = NSTextField(wrappingLabelWithString: "")
    private let recipientLabel = NSTextField(labelWithString: "先选择一位在线用户")
    private let message = NSTextField(string: "")
    private let sendButton = NSButton(title: "发送当前 GIF", target: nil, action: nil)
    private let spinner = NSProgressIndicator()
    private var selectedRecipientId: String?
    private var nicknameDirty = false
    private var busy = false
    private var lastError: String?
    private var resultText: String?
    private var cooldownUntil: Date?
    private var refreshTimer: Timer?
    private var cooldownTimer: Timer?

    init(service: CompanionService, hasAccess: @escaping () -> Bool, isTrial: @escaping () -> Bool,
         hallAvailable: @escaping () -> Bool, currentGIFURL: @escaping () -> URL?, stateChanged: @escaping () -> Void) {
        self.service = service
        self.hasAccess = hasAccess
        self.isTrial = isTrial
        self.hallAvailable = hallAvailable
        self.currentGIFURL = currentGIFURL
        self.stateChanged = stateChanged
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 820, height: 740),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "桌宠大厅"
        window.minSize = NSSize(width: 760, height: 640)
        window.isReleasedWhenClosed = false
        window.center()
        super.init(window: window)
        buildInterface(in: window)
        render()
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    deinit {
        refreshTimer?.invalidate()
        cooldownTimer?.invalidate()
    }

    override func showWindow(_ sender: Any?) {
        super.showWindow(sender)
        window?.makeKeyAndOrderFront(sender)
        NSApp.activate(ignoringOtherApps: true)
        refresh()
        refreshTimer?.invalidate()
        refreshTimer = Timer.scheduledTimer(withTimeInterval: 15, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in
                guard let self, window?.isVisible == true, !busy, service.profile?.hallEnabled == true else { return }
                refresh(preserveResult: true)
            }
        }
    }

    func refreshAccessState() { render() }

    private func buildInterface(in window: NSWindow) {
        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true
        scroll.drawsBackground = false
        let document = HallDocumentView()
        document.translatesAutoresizingMaskIntoConstraints = false
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 18
        stack.translatesAutoresizingMaskIntoConstraints = false
        document.addSubview(stack)
        scroll.documentView = document
        window.contentView = scroll
        NSLayoutConstraint.activate([
            document.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
            stack.leadingAnchor.constraint(equalTo: document.leadingAnchor, constant: 28),
            stack.trailingAnchor.constraint(equalTo: document.trailingAnchor, constant: -28),
            stack.topAnchor.constraint(equalTo: document.topAnchor, constant: 24),
            stack.bottomAnchor.constraint(equalTo: document.bottomAnchor, constant: -24)
        ])

        let title = NSTextField(labelWithString: "来大厅，遇见另一只桌搭子")
        title.font = .systemFont(ofSize: 25, weight: .bold)
        stack.addArrangedSubview(title)
        stack.addArrangedSubview(label("给在线的人送一只表情，也让有趣的来访落在你的桌角。", secondary: true))

        let identity = card(in: stack)
        accessLabel.font = .systemFont(ofSize: 12, weight: .semibold)
        accessLabel.textColor = .systemTeal
        identity.addArrangedSubview(accessLabel)
        nickname.placeholderString = "公开昵称，最多 12 字"
        nickname.delegate = self
        nickname.widthAnchor.constraint(equalToConstant: 260).isActive = true
        saveName.target = self
        saveName.action = #selector(saveNameAction)
        identity.addArrangedSubview(row([label("我的昵称"), nickname, saveName]))
        identity.addArrangedSubview(label("加入后，在线用户能看到你的昵称和在线状态，并向你发送 GIF。退出后停止接收新的大厅来访；已保存的来访仍会展示，私人搭子关系不受影响。", secondary: true))
        join.target = self
        join.action = #selector(joinAction)
        join.bezelStyle = .rounded
        join.controlSize = .large
        onlineCount.font = .systemFont(ofSize: 13, weight: .medium)
        identity.addArrangedSubview(row([join, onlineCount]))

        let people = card(in: stack)
        let peopleTitle = label("现在在线")
        peopleTitle.font = .systemFont(ofSize: 17, weight: .semibold)
        refreshButton.target = self
        refreshButton.action = #selector(refreshAction)
        spinner.style = .spinning
        spinner.controlSize = .small
        spinner.isDisplayedWhenStopped = false
        people.addArrangedSubview(row([peopleTitle, refreshButton, spinner]))
        peopleStack.orientation = .vertical
        peopleStack.alignment = .leading
        peopleStack.spacing = 8
        people.addArrangedSubview(peopleStack)
        peopleStack.widthAnchor.constraint(equalTo: people.widthAnchor).isActive = true

        let composer = card(in: stack)
        let sendTitle = label("送一只表情")
        sendTitle.font = .systemFont(ofSize: 17, weight: .semibold)
        composer.addArrangedSubview(sendTitle)
        preview.imageScaling = .scaleProportionallyUpOrDown
        preview.animates = true
        preview.widthAnchor.constraint(equalToConstant: 72).isActive = true
        preview.heightAnchor.constraint(equalToConstant: 72).isActive = true
        previewLabel.textColor = .secondaryLabelColor
        composer.addArrangedSubview(row([preview, previewLabel]))
        recipientLabel.font = .systemFont(ofSize: 13, weight: .medium)
        composer.addArrangedSubview(recipientLabel)
        message.placeholderString = "顺便留句话（可选，最多 120 字）"
        message.delegate = self
        composer.addArrangedSubview(message)
        message.widthAnchor.constraint(equalTo: composer.widthAnchor).isActive = true
        sendButton.target = self
        sendButton.action = #selector(sendAction)
        sendButton.bezelStyle = .rounded
        sendButton.controlSize = .large
        composer.addArrangedSubview(sendButton)
        composer.addArrangedSubview(label("每 30 秒可发送一次；对方最多保留 3 条待收取来访，24 小时未接收会过期。设备接收回执不代表对方已阅读。", secondary: true))

        status.isSelectable = true
        status.maximumNumberOfLines = 0
        status.font = .systemFont(ofSize: 13)
        stack.addArrangedSubview(status)
        status.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
    }

    private func card(in parent: NSStackView) -> NSStackView {
        let container = NSView()
        container.wantsLayer = true
        container.layer?.cornerRadius = 16
        container.layer?.backgroundColor = NSColor.controlBackgroundColor.cgColor
        container.layer?.borderColor = NSColor.separatorColor.withAlphaComponent(0.5).cgColor
        container.layer?.borderWidth = 1
        let body = NSStackView()
        body.orientation = .vertical
        body.alignment = .leading
        body.spacing = 12
        body.translatesAutoresizingMaskIntoConstraints = false
        container.addSubview(body)
        parent.addArrangedSubview(container)
        NSLayoutConstraint.activate([
            container.widthAnchor.constraint(equalTo: parent.widthAnchor),
            body.leadingAnchor.constraint(equalTo: container.leadingAnchor, constant: 20),
            body.trailingAnchor.constraint(equalTo: container.trailingAnchor, constant: -20),
            body.topAnchor.constraint(equalTo: container.topAnchor, constant: 18),
            body.bottomAnchor.constraint(equalTo: container.bottomAnchor, constant: -18)
        ])
        return body
    }

    private func label(_ text: String, secondary: Bool = false) -> NSTextField {
        let field = NSTextField(wrappingLabelWithString: text)
        field.maximumNumberOfLines = 0
        field.font = .systemFont(ofSize: 13)
        field.textColor = secondary ? .secondaryLabelColor : .labelColor
        field.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        field.widthAnchor.constraint(lessThanOrEqualToConstant: 620).isActive = true
        return field
    }

    private func row(_ views: [NSView]) -> NSStackView {
        let row = NSStackView(views: views)
        row.orientation = .horizontal
        row.alignment = .centerY
        row.spacing = 12
        return row
    }

    private func render() {
        let allowed = hasAccess() && hallAvailable()
        let joined = service.profile?.hallEnabled == true
        if !nicknameDirty, nickname.currentEditor() == nil, let profile = service.profile { nickname.stringValue = profile.displayName }
        accessLabel.stringValue = isTrial() ? "七天体验 · 可以加入大厅并发送表情" : "公开大厅 · 自由加入和退出"
        nickname.isEnabled = allowed && !busy
        saveName.isEnabled = allowed && !busy && service.profile != nil
        join.isEnabled = allowed && !busy && service.profile != nil
        join.title = joined ? "退出大厅" : "加入大厅"
        join.contentTintColor = joined ? .secondaryLabelColor : .systemTeal
        onlineCount.stringValue = joined ? "\(service.hallPeople.count) 位其他用户在线" : "当前未公开在线"
        refreshButton.isEnabled = allowed && !busy
        if busy { spinner.startAnimation(nil) } else { spinner.stopAnimation(nil) }
        renderMembers(joined: joined, allowed: allowed)
        let file = currentGIFURL()
        preview.image = file.flatMap { NSImage(contentsOf: $0) }
        previewLabel.stringValue = file.map { "当前形象：\($0.deletingPathExtension().lastPathComponent)\n发送时会使用这个 GIF" }
            ?? "尚无可发送的 GIF。先到“我的桌宠”选择一个形象。"
        let selected = service.hallPeople.first { $0.id == selectedRecipientId }
        recipientLabel.stringValue = selected.map { "发送给：\($0.displayName)" } ?? "先选择一位在线用户"
        message.isEnabled = allowed && joined && !busy
        let remaining = max(0, Int(ceil(cooldownUntil?.timeIntervalSinceNow ?? 0)))
        sendButton.title = remaining > 0 ? "\(remaining) 秒后可再次发送" : "发送当前 GIF"
        sendButton.isEnabled = allowed && joined && !busy && selected != nil && file != nil && remaining == 0
        status.textColor = lastError == nil ? .secondaryLabelColor : .systemRed
        status.stringValue = !hasAccess() ? "有效体验或正式激活后可以加入大厅。"
            : !hallAvailable() ? "大厅暂时关闭，请稍后再来。"
            : lastError ?? resultText ?? (busy ? "正在连接，请稍候…" : "昵称与加入状态会随账号同步。私人搭子请从菜单“私人搭子”进入。")
    }

    private func renderMembers(joined: Bool, allowed: Bool) {
        let people = joined && allowed ? service.hallPeople : []
        let placeholder = !joined || !allowed
            ? "主动加入后即可看到在线用户。你的昵称和在线状态会公开在这里。"
            : busy ? "正在寻找在线桌搭子…" : "此刻还没有其他人在线。可以稍后刷新，或继续让桌宠陪你。"
        let signature = people.isEmpty ? ["empty", placeholder] : people.flatMap { [$0.id, $0.displayName] }
        if !people.contains(where: { $0.id == selectedRecipientId }) { selectedRecipientId = nil }
        // Preserve the focused native button during selection and cooldown updates.
        if signature != memberListSignature {
            memberListSignature = signature
            for view in peopleStack.arrangedSubviews { peopleStack.removeArrangedSubview(view); view.removeFromSuperview() }
            memberRows.removeAll()
            if people.isEmpty {
                peopleStack.addArrangedSubview(label(placeholder, secondary: true))
            } else {
                for person in people {
                    let button = HallMemberButton(person: person)
                    button.target = self
                    button.action = #selector(selectPerson(_:))
                    peopleStack.addArrangedSubview(button)
                    button.widthAnchor.constraint(equalTo: peopleStack.widthAnchor).isActive = true
                    memberRows.append(button)
                }
            }
        }
        for button in memberRows {
            button.updateSelection(button.identifier?.rawValue == selectedRecipientId, enabled: !busy)
        }
    }

    private func run(_ action: @escaping @MainActor () async throws -> Void, preserveResult: Bool = false) {
        guard !busy, hasAccess(), hallAvailable() else { render(); return }
        busy = true
        lastError = nil
        if !preserveResult { resultText = nil }
        render()
        Task { @MainActor [weak self] in
            guard let self else { return }
            defer { busy = false; render(); stateChanged() }
            do { try await action() }
            catch { lastError = error.localizedDescription + " 可以点击刷新重试。" }
        }
    }

    private func refresh(preserveResult: Bool = false) {
        run({
            let profile = try await self.service.refreshProfile()
            if profile.hallEnabled { _ = try await self.service.refreshHall() }
        }, preserveResult: preserveResult)
    }

    @objc private func refreshAction() { refresh() }

    @objc private func selectPerson(_ sender: NSButton) {
        selectedRecipientId = sender.identifier?.rawValue
        render()
    }

    @objc private func saveNameAction() {
        let value = nickname.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty, value.unicodeScalars.count <= 12 else {
            lastError = "昵称需要 1–12 字，请修改后保存。"
            render()
            return
        }
        run {
            _ = try await self.service.updateName(value)
            self.nicknameDirty = false
            self.resultText = "昵称已保存。"
        }
    }

    @objc private func joinAction() {
        let enabled = service.profile?.hallEnabled != true
        if enabled {
            let alert = NSAlert()
            alert.messageText = "以“\(service.profile?.displayName ?? "桌搭子")”加入大厅？"
            alert.informativeText = "其他在线用户将看到你的昵称与在线状态，并可向你发送 GIF 来访。你可以随时退出，私人搭子关系不受影响。"
            alert.addButton(withTitle: "加入大厅")
            alert.addButton(withTitle: "取消")
            guard alert.runModal() == .alertFirstButtonReturn else { return }
        }
        run {
            _ = try await self.service.setHallEnabled(enabled)
            if enabled { _ = try await self.service.refreshHall() }
            self.resultText = enabled ? "已加入大厅，现在可以选择在线用户。" : "已退出大厅，不再公开在线。"
        }
    }

    @objc private func sendAction() {
        guard let recipient = selectedRecipientId, let file = currentGIFURL() else { return }
        let text = message.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard text.unicodeScalars.count <= 120 else { lastError = "留言最多 120 字。"; render(); return }
        run {
            let name = try await self.service.sendToHall(fileURL: file, recipientId: recipient, message: text)
            self.message.stringValue = ""
            self.resultText = "已发给 \(name)，等待对方设备接收。24 小时未接收会过期。"
            self.cooldownUntil = Date().addingTimeInterval(30)
            self.startCooldownTimer()
        }
    }

    private func startCooldownTimer() {
        cooldownTimer?.invalidate()
        cooldownTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in
                guard let self else { return }
                if (cooldownUntil?.timeIntervalSinceNow ?? 0) <= 0 { cooldownTimer?.invalidate(); cooldownTimer = nil }
                render()
            }
        }
    }

    func controlTextDidChange(_ obj: Notification) {
        guard let field = obj.object as? NSTextField else { return }
        if field === nickname { nicknameDirty = true }
        // Preserve marked text while an input method is composing Chinese.
        if let editor = field.currentEditor() as? NSTextView, editor.hasMarkedText() { return }
        let limit = field === nickname ? 12 : 120
        if field.stringValue.unicodeScalars.count > limit {
            field.stringValue = String(String.UnicodeScalarView(field.stringValue.unicodeScalars.prefix(limit)))
        }
    }
}

private final class HallDocumentView: NSView {
    override var isFlipped: Bool { true }
}

@MainActor
private final class HallMemberButton: NSButton {
    private let avatar = NSTextField(labelWithString: "")
    private let nameLabel = NSTextField(labelWithString: "")
    private let onlineBadge = NSTextField(labelWithString: "● 在线")
    private let selectionMark = NSTextField(labelWithString: "✓")

    init(person: CompanionHallPerson) {
        super.init(frame: .zero)
        translatesAutoresizingMaskIntoConstraints = false
        identifier = NSUserInterfaceItemIdentifier(person.id)
        title = person.displayName
        setButtonType(.radio)
        isBordered = false
        focusRingType = .exterior
        toolTip = "选择 \(person.displayName) 接收本次 GIF；按空格选择"
        setAccessibilityLabel("\(person.displayName)，在线")
        heightAnchor.constraint(equalToConstant: 64).isActive = true

        avatar.stringValue = String(person.displayName.trimmingCharacters(in: .whitespacesAndNewlines).prefix(1))
        avatar.font = .systemFont(ofSize: 19, weight: .semibold)
        avatar.alignment = .center
        avatar.textColor = .systemTeal
        nameLabel.stringValue = person.displayName
        nameLabel.font = .systemFont(ofSize: 14, weight: .semibold)
        nameLabel.lineBreakMode = .byTruncatingTail
        nameLabel.maximumNumberOfLines = 1
        nameLabel.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        onlineBadge.font = .systemFont(ofSize: 11, weight: .medium)
        onlineBadge.textColor = .systemGreen
        selectionMark.font = .systemFont(ofSize: 18, weight: .bold)
        selectionMark.textColor = .controlAccentColor
        selectionMark.alignment = .center
        for view in [avatar, nameLabel, onlineBadge, selectionMark] {
            view.translatesAutoresizingMaskIntoConstraints = false
            view.setAccessibilityElement(false)
            addSubview(view)
        }
        NSLayoutConstraint.activate([
            avatar.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 14),
            avatar.widthAnchor.constraint(equalToConstant: 40),
            avatar.centerYAnchor.constraint(equalTo: centerYAnchor),
            nameLabel.leadingAnchor.constraint(equalTo: avatar.trailingAnchor, constant: 12),
            nameLabel.trailingAnchor.constraint(lessThanOrEqualTo: onlineBadge.leadingAnchor, constant: -12),
            nameLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            onlineBadge.trailingAnchor.constraint(equalTo: selectionMark.leadingAnchor, constant: -12),
            onlineBadge.centerYAnchor.constraint(equalTo: centerYAnchor),
            selectionMark.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -14),
            selectionMark.widthAnchor.constraint(equalToConstant: 22),
            selectionMark.centerYAnchor.constraint(equalTo: centerYAnchor)
        ])
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override var acceptsFirstResponder: Bool { isEnabled }

    override func hitTest(_ point: NSPoint) -> NSView? {
        super.hitTest(point) == nil ? nil : self
    }

    func updateSelection(_ selected: Bool, enabled: Bool) {
        state = selected ? .on : .off
        isEnabled = enabled
        selectionMark.isHidden = !selected
        alphaValue = enabled ? 1 : 0.55
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        let selected = state == .on
        let outline = NSBezierPath(roundedRect: bounds.insetBy(dx: 1, dy: 1), xRadius: 12, yRadius: 12)
        (selected || isHighlighted ? NSColor.controlAccentColor.withAlphaComponent(0.12) : NSColor.windowBackgroundColor).setFill()
        outline.fill()
        (selected ? NSColor.controlAccentColor : NSColor.separatorColor.withAlphaComponent(0.45)).setStroke()
        outline.lineWidth = selected ? 1.5 : 1
        outline.stroke()
        NSColor.systemTeal.withAlphaComponent(0.12).setFill()
        NSBezierPath(ovalIn: NSRect(x: 14, y: (bounds.height - 40) / 2, width: 40, height: 40)).fill()
    }

    override var focusRingMaskBounds: NSRect { bounds }

    override func drawFocusRingMask() {
        NSBezierPath(roundedRect: bounds, xRadius: 12, yRadius: 12).fill()
    }
}
