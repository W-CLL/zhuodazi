import AppKit

@MainActor
final class InvitationWindowController: NSObject, NSWindowDelegate {
    private let window: NSWindow
    private let input = NSTextField(string: "")
    private let statusLabel = NSTextField(wrappingLabelWithString: "")
    private let payButton = NSButton(title: "正在加载…", target: nil, action: nil)
    private let licenses: LicenseService
    private let payments: PaymentCheckoutClient
    private let replacingExisting: Bool
    private var refreshTimer: Timer?
    private var refreshTask: Task<Void, Never>?
    private var purchaseTask: Task<Void, Never>?
    private var finished = false
    private var creatingPayment = false
    private var paymentAvailable = false
    private(set) var activatedRemotely = false
    private var result: String?

    init(licenses: LicenseService, replacingExisting: Bool = false, required: Bool, statusMessage: String? = nil, trialEnded: Bool = false) {
        self.licenses = licenses; self.replacingExisting = replacingExisting
        payments = PaymentCheckoutClient(licenses: licenses)
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 500, height: replacingExisting ? 280 : 420),
            styleMask: [.titled, .closable], backing: .buffered, defer: false)
        super.init()
        window.title = replacingExisting ? "更换激活码" : "激活桌搭子"
        window.isReleasedWhenClosed = false; window.delegate = self
        buildInterface(statusMessage: statusMessage)
        window.center()
    }

    func runModal() -> String? {
        result = nil; window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
        window.makeFirstResponder(input)
        refreshTimer = Timer(timeInterval: 15, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.requestRefresh() }
        }
        if let refreshTimer { RunLoop.main.add(refreshTimer, forMode: .modalPanel) }
        requestRefresh(); NSApp.runModal(for: window)
        finish(); window.orderOut(nil)
        return result
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool { finish(); NSApp.stopModal(); return true }
    func windowDidBecomeKey(_ notification: Notification) { requestRefresh() }

    private func buildInterface(statusMessage: String?) {
        let root = NSView(), stack = NSStackView()
        stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 16
        stack.translatesAutoresizingMaskIntoConstraints = false; root.addSubview(stack); window.contentView = root
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 28),
            stack.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -28),
            stack.topAnchor.constraint(equalTo: root.topAnchor, constant: 24),
            stack.bottomAnchor.constraint(lessThanOrEqualTo: root.bottomAnchor, constant: -22)])
        let heading = NSTextField(labelWithString: window.title)
        heading.font = .systemFont(ofSize: 23, weight: .bold); stack.addArrangedSubview(heading)
        if !replacingExisting {
            let product = NSTextField(labelWithString: "桌搭子完整功能")
            product.font = .systemFont(ofSize: 16, weight: .semibold); stack.addArrangedSubview(product)
            let description = NSTextField(labelWithString: "永久激活 · 支持 2 台设备")
            description.textColor = .secondaryLabelColor; stack.addArrangedSubview(description)
            payButton.target = self; payButton.action = #selector(purchaseActivation)
            payButton.bezelStyle = .rounded; payButton.bezelColor = .controlAccentColor; payButton.isEnabled = false
            payButton.heightAnchor.constraint(equalToConstant: 38).isActive = true
            stack.addArrangedSubview(payButton); payButton.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
        }
        stack.addArrangedSubview(NSTextField(labelWithString: replacingExisting ? "输入新的激活码" : "使用激活码"))
        input.placeholderString = "6 位激活码"; input.font = .monospacedSystemFont(ofSize: 22, weight: .semibold)
        input.alignment = .center; input.heightAnchor.constraint(equalToConstant: 40).isActive = true
        stack.addArrangedSubview(input); input.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
        statusLabel.font = .systemFont(ofSize: 12); statusLabel.textColor = .secondaryLabelColor
        statusLabel.stringValue = statusMessage ?? " "; stack.addArrangedSubview(statusLabel)
        statusLabel.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
        let cancel = NSButton(title: replacingExisting ? "取消" : "稍后", target: self, action: #selector(cancelPrompt))
        let submit = NSButton(title: "激活", target: self, action: #selector(submitPrompt)); submit.keyEquivalent = "\r"
        let buttons = NSStackView(views: [NSView(), cancel, submit]); buttons.spacing = 10
        stack.addArrangedSubview(buttons); buttons.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
    }

    @objc private func purchaseActivation() {
        guard !finished, !creatingPayment, paymentAvailable else { return }
        creatingPayment = true; payButton.isEnabled = false
        purchaseTask = Task { [weak self] in
            guard let self else { return }
            defer { creatingPayment = false; payButton.isEnabled = paymentAvailable && !finished; purchaseTask = nil }
            do {
                _ = try await licenses.checkTrial()
                guard !finished else { return }
                if licenses.isActivated { activatedRemotely = true; finish(); NSApp.stopModal(); return }
                try await payments.purchase(activation: true)
                if !finished { statusLabel.stringValue = "等待付款" }
            } catch is CancellationError { }
            catch { if !finished { statusLabel.stringValue = error.localizedDescription } }
        }
    }

    private func requestRefresh() {
        guard !finished, !replacingExisting, refreshTask == nil else { return }
        refreshTask = Task { [weak self] in
            guard let self else { return }
            defer { refreshTask = nil }
            do {
                try await payments.refreshActivations()
                _ = try await licenses.checkTrial()
                guard !finished else { return }
                if licenses.isActivated { activatedRemotely = true; finish(); NSApp.stopModal(); return }
                let catalog = try await payments.catalog()
                guard !finished else { return }
                paymentAvailable = catalog.provider.configured
                payButton.title = String(format: "支付宝支付 · ¥%.2f", Double(catalog.activation.amountFen) / 100)
                payButton.isEnabled = paymentAvailable && !creatingPayment
            } catch is CancellationError { }
            catch {
                if !finished { payButton.title = "暂时无法支付"; paymentAvailable = false; payButton.isEnabled = false }
            }
        }
    }

    private func finish() {
        finished = true; refreshTimer?.invalidate(); refreshTask?.cancel(); purchaseTask?.cancel()
    }

    @objc private func submitPrompt() {
        let allowed = Set("ABCDEFGHJKLMNPQRSTUVWXYZ23456789")
        input.stringValue = String(input.stringValue.uppercased().filter { allowed.contains($0) }.prefix(6))
        guard input.stringValue.count == 6 else {
            statusLabel.stringValue = "请输入完整的 6 位激活码"; window.makeFirstResponder(input); return
        }
        result = input.stringValue; finish(); NSApp.stopModal()
    }
    @objc private func cancelPrompt() { result = nil; finish(); NSApp.stopModal() }
}
