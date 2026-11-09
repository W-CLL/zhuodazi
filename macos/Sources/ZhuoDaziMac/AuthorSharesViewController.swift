import AppKit

@MainActor
final class AuthorSharesViewController: NSViewController {
    struct Content: Decodable {
        struct Category: Decodable {
            struct Item: Decodable { let id: String; let title: String; let description: String; let url: String }
            let id: String; let name: String; let items: [Item]
        }
        let categories: [Category]
    }
    private let categories = NSPopUpButton()
    private let status = NSTextField(wrappingLabelWithString: "正在加载…")
    private let links = NSStackView()
    private var content: [Content.Category] = []
    private var task: Task<Void, Never>?
    override func loadView() {
        let root = NSView(); view = root
        let stack = NSStackView(); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 16
        stack.translatesAutoresizingMaskIntoConstraints = false; root.addSubview(stack)
        let title = NSTextField(labelWithString: "作者分享"); title.font = .systemFont(ofSize: 27, weight: .semibold)
        stack.addArrangedSubview(title)
        categories.target = self; categories.action = #selector(selectCategory)
        let refresh = NSButton(title: "刷新", target: self, action: #selector(reload))
        let row = NSStackView(views: [categories, refresh]); row.spacing = 12
        stack.addArrangedSubview(row); categories.widthAnchor.constraint(greaterThanOrEqualToConstant: 220).isActive = true
        stack.addArrangedSubview(status); status.textColor = .secondaryLabelColor
        let scroll = NSScrollView(); scroll.hasVerticalScroller = true; scroll.drawsBackground = false
        links.orientation = .vertical; links.alignment = .leading; links.spacing = 16
        links.translatesAutoresizingMaskIntoConstraints = false; scroll.documentView = links
        stack.addArrangedSubview(scroll)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 28),
            stack.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -28),
            stack.topAnchor.constraint(equalTo: root.topAnchor, constant: 28),
            stack.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -28),
            scroll.widthAnchor.constraint(equalTo: stack.widthAnchor),
            status.widthAnchor.constraint(equalTo: stack.widthAnchor),
            links.leadingAnchor.constraint(equalTo: scroll.contentView.leadingAnchor),
            links.trailingAnchor.constraint(equalTo: scroll.contentView.trailingAnchor),
            links.topAnchor.constraint(equalTo: scroll.contentView.topAnchor),
            links.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor)
        ])
    }
    override func viewWillAppear() { super.viewWillAppear(); reload() }
    override func viewDidDisappear() { super.viewDidDisappear(); task?.cancel(); task = nil }
    @objc private func reload() {
        task?.cancel(); status.stringValue = "正在加载…"
        task = Task { [weak self] in
            guard let self else { return }
            do {
                let selected = content.indices.contains(categories.indexOfSelectedItem) ? content[categories.indexOfSelectedItem].id : nil
                let request = URLRequest(url: URL(string: "https://in.desktoppet.online/api/public/author-shares")!, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 15)
                let (bytes, response) = try await URLSession.shared.data(for: request)
                guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw URLError(.badServerResponse) }
                let result = try JSONDecoder().decode(Content.self, from: bytes)
                try Task.checkCancellation()
                content = result.categories; categories.removeAllItems(); categories.addItems(withTitles: content.map(\.name))
                if let index = content.firstIndex(where: { $0.id == selected }) { categories.selectItem(at: index) }
                selectCategory()
            } catch { if !Task.isCancelled { status.stringValue = "暂时无法加载，请稍后刷新。" } }
        }
    }
    @objc private func selectCategory() {
        links.arrangedSubviews.forEach { links.removeArrangedSubview($0); $0.removeFromSuperview() }
        guard content.indices.contains(categories.indexOfSelectedItem) else { status.stringValue = "暂无分享"; return }
        let items = content[categories.indexOfSelectedItem].items
        status.stringValue = items.isEmpty ? "这个分类还没有分享" : ""
        for item in items {
            let button = ShareLinkButton(title: item.title, target: self, action: #selector(openLink(_:)))
            button.link = item.url; button.bezelStyle = .rounded; button.font = .systemFont(ofSize: 17, weight: .semibold)
            let description = NSTextField(wrappingLabelWithString: item.description); description.textColor = .secondaryLabelColor
            let url = NSTextField(wrappingLabelWithString: item.url); url.font = .systemFont(ofSize: 12); url.textColor = .secondaryLabelColor
            let card = NSStackView(views: [button, description, url]); card.orientation = .vertical; card.alignment = .leading; card.spacing = 8
            card.edgeInsets = NSEdgeInsets(top: 14, left: 16, bottom: 14, right: 16)
            card.wantsLayer = true; card.layer?.cornerRadius = 12; card.layer?.backgroundColor = NSColor.controlBackgroundColor.cgColor
            links.addArrangedSubview(card)
            card.widthAnchor.constraint(equalTo: links.widthAnchor).isActive = true
            description.widthAnchor.constraint(equalTo: card.widthAnchor, constant: -32).isActive = true
            url.widthAnchor.constraint(equalTo: description.widthAnchor).isActive = true
            button.widthAnchor.constraint(lessThanOrEqualTo: description.widthAnchor).isActive = true
        }
    }
    @objc private func openLink(_ sender: ShareLinkButton) {
        guard let url = URL(string: sender.link), ["http", "https"].contains(url.scheme?.lowercased() ?? ""), url.user == nil, url.password == nil else { return }
        if !NSWorkspace.shared.open(url) { status.stringValue = "无法打开浏览器，请稍后重试。" }
    }
}

private final class ShareLinkButton: NSButton { var link = "" }
