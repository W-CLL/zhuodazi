import AppKit

@MainActor
final class AuthorContactWindowController: NSWindowController {
    private let wechat: String
    init(config: RemoteConfig) {
        wechat = config.wechatId
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 360, height: 340),
            styleMask: [.titled, .closable], backing: .buffered, defer: false)
        super.init(window: window)
        window.title = "联系作者"; window.isReleasedWhenClosed = false
        let stack = NSStackView(); stack.orientation = .vertical; stack.spacing = 20
        stack.translatesAutoresizingMaskIntoConstraints = false
        let root = NSView(); window.contentView = root; root.addSubview(stack)
        NSLayoutConstraint.activate([stack.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 28),
            stack.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -28),
            stack.topAnchor.constraint(equalTo: root.topAnchor, constant: 24)])
        let image = NSImageView(); image.imageScaling = .scaleProportionallyUpOrDown
        image.widthAnchor.constraint(equalToConstant: 220).isActive = true
        image.heightAnchor.constraint(equalToConstant: 220).isActive = true
        let dataURL = config.activationQrCodeDataUrl
        if dataURL.hasPrefix("data:image/png;base64,"), dataURL.count <= 350_000,
           let data = Data(base64Encoded: String(dataURL.dropFirst(22))), let decoded = NSImage(data: data) {
            image.image = decoded
        } else if let url = Bundle.main.url(forResource: "contact-author-wechat", withExtension: "png") {
            image.image = NSImage(contentsOf: url)
        }
        stack.addArrangedSubview(image)
        let account = NSTextField(labelWithString: wechat); account.isSelectable = true
        account.font = .systemFont(ofSize: 15)
        let copy = NSButton(title: "复制微信号", target: self, action: #selector(copyWechat))
        stack.addArrangedSubview(NSStackView(views: [account, copy]))
        window.center()
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }
    @objc private func copyWechat() {
        NSPasteboard.general.clearContents(); NSPasteboard.general.setString(wechat, forType: .string)
    }
}
