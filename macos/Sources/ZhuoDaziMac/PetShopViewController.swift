import AppKit

/// Scroll documents begin at the first card, including when the document is taller than its viewport.
private final class PetShopDocumentView: NSView {
    override var isFlipped: Bool { true }
}

@MainActor
private final class PetShopButton: NSButton {
    private var actionHandler: (() -> Void)?
    init(_ title: String, action: @escaping () -> Void) {
        super.init(frame: .zero)
        self.title = title; actionHandler = action; target = self; self.action = #selector(run)
        bezelStyle = .rounded
        setContentHuggingPriority(.required, for: .vertical)
        setContentCompressionResistancePriority(.required, for: .vertical)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }
    @objc private func run() { actionHandler?() }
}

@MainActor
final class PetShopViewController: NSViewController, NSSearchFieldDelegate {
    private let licenses: LicenseService
    private let client: PetMarketClient
    private let checkout: PaymentCheckoutClient?
    private let resources: ResourcePackClient
    private let installedPets: () -> [PetMarketInstallation]
    private let onInstallPet: (PetMarketInstallation) -> Void
    private let onRefreshPet: (PetMarketInstallation) -> Void
    private let isPetEnabled: (String) -> Bool
    private let onTogglePet: (String, Bool) throws -> Void
    private let resourceState: (String, String) -> String
    private let onInstallResources: (ParsedResourcePack) throws -> Void
    private let onToggleResources: (String) throws -> Void
    private let requestActivation: () -> Void
    private var products: [PetMarketProduct] = [], packs: [ResourcePackProduct] = [], orders: [PetMarketOrder] = []
    private var selectedId: String?, selectedOrderId: String?
    private var payment: PetMarketPayment?
    private var previewIndex = 0, previewGeneration = 0
    private var previewTask: Task<Void, Never>?, refreshTask: Task<Void, Never>?, pollTask: Task<Void, Never>?
    private var actionTasks: [UUID: Task<Void, Never>] = [:]
    private var timer: Timer?, nextPoll = Date.distantPast, nextGrantRefresh = Date.distantPast
    private var polling = false, loading = false, started = false, stopped = false
    private var installing = Set<String>(), installErrors: [String: String] = [:]
    private var purchasing = Set<String>()
    private var thumbnailCache: [String: NSImage] = [:]
    private var thumbnailTasks: [String: Task<Void, Never>] = [:]
    private var thumbnailViews: [String: NSImageView] = [:]
    private let tabs = NSSegmentedControl(labels: ["桌宠图鉴", "小剧场 / 互动词库"], trackingMode: .selectOne, target: nil, action: nil)
    private let search = NSSearchField()
    private let filter = NSPopUpButton()
    private let status = NSTextField(wrappingLabelWithString: "")
    private let productStack = NSStackView(), detailStack = NSStackView(), detailActions = NSStackView(), resourceStack = NSStackView()
    private let detailScroll = NSScrollView()
    private let market = NSView(), resourceScroll = NSScrollView()
    private var countdownLabel: NSTextField?
    private var ordersWindow: NSWindowController?
    private var orderStack: NSStackView?
    private var resourceColumns = 1, catalogColumns = 1
    private var detailProductId: String?
    private var catalogPreviewOnly = false

    init(licenses: LicenseService, client: PetMarketClient,
         installedPets: @escaping () -> [PetMarketInstallation],
         onInstallPet: @escaping (PetMarketInstallation) -> Void,
         onRefreshPet: @escaping (PetMarketInstallation) -> Void,
         isPetEnabled: @escaping (String) -> Bool,
         onTogglePet: @escaping (String, Bool) throws -> Void,
         resourceState: @escaping (String, String) -> String,
         onInstallResources: @escaping (ParsedResourcePack) throws -> Void,
         onToggleResources: @escaping (String) throws -> Void,
         requestActivation: @escaping () -> Void) {
        self.licenses = licenses; self.client = client; resources = ResourcePackClient(transport: client)
        checkout = client.isPreview ? nil : PaymentCheckoutClient(licenses: licenses)
        self.installedPets = installedPets; self.onInstallPet = onInstallPet; self.onRefreshPet = onRefreshPet
        self.isPetEnabled = isPetEnabled; self.onTogglePet = onTogglePet; self.resourceState = resourceState
        self.onInstallResources = onInstallResources; self.onToggleResources = onToggleResources
        self.requestActivation = requestActivation
        super.init(nibName: nil, bundle: nil)
        title = "桌宠小铺"
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    override func loadView() {
        view = NSView()
        view.wantsLayer = true
        view.layer?.backgroundColor = NSColor.windowBackgroundColor.cgColor
        let root = column()
        root.translatesAutoresizingMaskIntoConstraints = false; view.addSubview(root)
        NSLayoutConstraint.activate([root.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 20),
            root.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -20),
            root.topAnchor.constraint(equalTo: view.topAnchor, constant: 18)])
        let heading = row([label("桌宠小铺", size: 24, bold: true), NSView(),
            PetShopButton("订单记录") { [weak self] in self?.showOrders() },
            PetShopButton("刷新") { [weak self] in self?.reload() }])
        addFullWidth(heading, to: root)
        if client.isPreview { addFullWidth(label("本地独立预览 · 只连接配置的本机后台", secondary: true), to: root) }
        if !licenses.isActivated {
            let activate = PetShopButton("正式激活") { [weak self] in self?.requestActivation() }
            addFullWidth(card(row([label("可以先浏览预览，领取、购买和启用桌宠需要正式激活。", secondary: true), activate])), to: root)
        }
        tabs.selectedSegment = 0; tabs.target = self; tabs.action = #selector(changeTab)
        tabs.identifier = NSUserInterfaceItemIdentifier("shop-tabs")
        root.addArrangedSubview(tabs)
        search.placeholderString = "搜索名称、介绍或标签"; search.delegate = self
        search.identifier = NSUserInterfaceItemIdentifier("shop-search")
        search.setContentHuggingPriority(.defaultLow, for: .horizontal)
        filter.target = self; filter.action = #selector(changeFilter); updateFilters()
        filter.identifier = NSUserInterfaceItemIdentifier("shop-filter")
        let controls = row([search, filter]); addFullWidth(controls, to: root)
        filter.widthAnchor.constraint(equalToConstant: 120).isActive = true
        status.textColor = .secondaryLabelColor; status.maximumNumberOfLines = 2
        addFullWidth(status, to: root)
        let content = NSView(); view.addSubview(content)
        content.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([content.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            content.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            content.topAnchor.constraint(equalTo: root.bottomAnchor, constant: 12),
            content.bottomAnchor.constraint(equalTo: view.bottomAnchor, constant: -18),
            content.heightAnchor.constraint(greaterThanOrEqualToConstant: 220)])

        // Both columns are constrained to exactly the same width, with their own scrollbars.
        let horizontal = NSScrollView(); horizontal.hasHorizontalScroller = true; horizontal.hasVerticalScroller = false
        horizontal.drawsBackground = false; horizontal.documentView = market
        market.translatesAutoresizingMaskIntoConstraints = false
        let left = scroll(productStack), right = NSView()
        left.identifier = NSUserInterfaceItemIdentifier("shop-catalog-scroll")
        right.identifier = NSUserInterfaceItemIdentifier("shop-detail-panel")
        right.wantsLayer = true; right.layer?.cornerRadius = 10
        right.layer?.backgroundColor = NSColor.controlBackgroundColor.cgColor
        right.layer?.borderWidth = 1; right.layer?.borderColor = NSColor.separatorColor.cgColor
        configureScroll(detailScroll, stack: detailStack)
        detailScroll.identifier = NSUserInterfaceItemIdentifier("shop-detail-scroll")
        detailActions.orientation = .vertical; detailActions.alignment = .leading; detailActions.spacing = 8
        detailActions.identifier = NSUserInterfaceItemIdentifier("shop-detail-actions")
        for child in [detailScroll, detailActions] {
            child.translatesAutoresizingMaskIntoConstraints = false; right.addSubview(child)
        }
        // The purchase/install/enable controls stay in the panel, not beneath a tall preview or description.
        NSLayoutConstraint.activate([detailScroll.leadingAnchor.constraint(equalTo: right.leadingAnchor, constant: 14),
            detailScroll.trailingAnchor.constraint(equalTo: right.trailingAnchor, constant: -14),
            detailScroll.topAnchor.constraint(equalTo: right.topAnchor, constant: 14),
            detailScroll.bottomAnchor.constraint(equalTo: detailActions.topAnchor, constant: -12),
            detailActions.leadingAnchor.constraint(equalTo: right.leadingAnchor, constant: 14),
            detailActions.trailingAnchor.constraint(equalTo: right.trailingAnchor, constant: -14),
            detailActions.bottomAnchor.constraint(equalTo: right.bottomAnchor, constant: -14)])
        for panel in [left, right] { market.addSubview(panel); panel.translatesAutoresizingMaskIntoConstraints = false }
        NSLayoutConstraint.activate([market.widthAnchor.constraint(greaterThanOrEqualToConstant: 720),
            market.widthAnchor.constraint(greaterThanOrEqualTo: horizontal.contentView.widthAnchor),
            market.leadingAnchor.constraint(equalTo: horizontal.contentView.leadingAnchor),
            market.topAnchor.constraint(equalTo: horizontal.contentView.topAnchor),
            market.heightAnchor.constraint(equalTo: horizontal.contentView.heightAnchor),
            left.leadingAnchor.constraint(equalTo: market.leadingAnchor), left.topAnchor.constraint(equalTo: market.topAnchor),
            left.bottomAnchor.constraint(equalTo: market.bottomAnchor), right.trailingAnchor.constraint(equalTo: market.trailingAnchor),
            right.topAnchor.constraint(equalTo: market.topAnchor), right.bottomAnchor.constraint(equalTo: market.bottomAnchor),
            right.leadingAnchor.constraint(equalTo: left.trailingAnchor, constant: 14), left.widthAnchor.constraint(equalTo: right.widthAnchor)])
        let preferredWidth = market.widthAnchor.constraint(equalTo: horizontal.contentView.widthAnchor)
        preferredWidth.priority = .defaultHigh; preferredWidth.isActive = true
        configureScroll(resourceScroll, stack: resourceStack)
        resourceScroll.identifier = NSUserInterfaceItemIdentifier("shop-resource-scroll")
        resourceScroll.isHidden = true
        for panel in [horizontal, resourceScroll] {
            panel.translatesAutoresizingMaskIntoConstraints = false; content.addSubview(panel)
            NSLayoutConstraint.activate([panel.leadingAnchor.constraint(equalTo: content.leadingAnchor), panel.trailingAnchor.constraint(equalTo: content.trailingAnchor),
                panel.topAnchor.constraint(equalTo: content.topAnchor), panel.bottomAnchor.constraint(equalTo: content.bottomAnchor)])
        }
        horizontal.identifier = NSUserInterfaceItemIdentifier("market-scroll")
    }

    override func viewDidAppear() {
        super.viewDidAppear()
        guard !stopped, !catalogPreviewOnly else { return }
        if !started {
            started = true; reload()
            timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
                Task { @MainActor [weak self] in self?.tick() }
            }
        } else { render() }
    }
    override func viewDidLayout() {
        super.viewDidLayout()
        let columns = resourceScroll.contentView.bounds.width >= 720 ? 2 : 1
        if columns != resourceColumns { resourceColumns = columns; if started || catalogPreviewOnly { renderResources() } }
        let newCatalogColumns = productStack.bounds.width >= 440 ? 2 : 1
        if newCatalogColumns != catalogColumns {
            catalogColumns = newCatalogColumns
            if started || catalogPreviewOnly { renderProducts() }
        }
    }
    func stop() {
        stopped = true
        timer?.invalidate(); timer = nil; refreshTask?.cancel(); previewTask?.cancel(); pollTask?.cancel()
        actionTasks.values.forEach { $0.cancel() }; actionTasks.removeAll()
        thumbnailTasks.values.forEach { $0.cancel() }; thumbnailTasks.removeAll()
        ordersWindow?.close()
    }
    func controlTextDidChange(_ notification: Notification) { render() }
    @objc private func changeTab() { updateFilters(); render() }
    @objc private func changeFilter() { render() }
    private func updateFilters() {
        filter.removeAllItems()
        filter.addItems(withTitles: tabs.selectedSegment == 0 ? ["全部", "免费", "付费", "已拥有"] : ["全部", "互动词库", "小剧场", "已安装"])
    }

    private func reload() {
        guard !stopped, !loading else { return }
        loading = true; status.stringValue = "正在刷新小铺…"
        refreshTask = Task { [weak self] in
            guard let self else { return }
            defer { loading = false }
            do {
                let catalog = try await client.catalog()
                try Task.checkCancellation()
                products = catalog.products.sorted { ($0.sortOrder, $0.title) < ($1.sortOrder, $1.title) }; payment = catalog.payment
                if !products.contains(where: { $0.id == selectedId }) { selectedId = products.first?.id }
                packs = try await resources.catalog()
                try Task.checkCancellation()
                guard !stopped else { return }
                status.stringValue = "\(products.count) 款桌宠 · \(packs.count) 套免费内容"
                render(); await syncOrders()
            } catch is CancellationError { }
            catch { if !stopped { status.stringValue = error.localizedDescription; render() } }
        }
    }
    private func tick() {
        guard !stopped else { return }
        if let pending = displayedOrder, pending.status == "pending_payment" {
            let remaining = max(0, Int(ceil(pending.expiresAt.timeIntervalSince(client.serverNow))))
            countdownLabel?.stringValue = String(format: "%02d:%02d", remaining / 60, remaining % 60)
            if remaining == 0, countdownLabel != nil { renderDetail() }
        }
        guard licenses.isActivated, !polling, Date() >= nextPoll else { return }
        pollTask = Task { [weak self] in await self?.syncOrders() }
    }
    private func syncOrders() async {
        guard !stopped, licenses.isActivated, !polling else { return }
        polling = true
        defer {
            polling = false
            nextPoll = Date().addingTimeInterval(orders.contains { $0.status == "pending_payment" } ? 3 : 30)
        }
        do {
            let pending = try await checkout?.pendingPetOrders() ?? []
            var updatedOrders = try await client.orders()
            let existing = Set(updatedOrders.map(\.id))
            updatedOrders += pending.filter { !existing.contains($0.id) }
            updatedOrders.sort { $0.createdAt > $1.createdAt }
            try Task.checkCancellation()
            guard !stopped else { return }
            let changed = orders != updatedOrders
            orders = updatedOrders
            if changed { renderProducts(); renderDetail(); renderOrderList() }
            for order in orders where order.status == "paid" {
                try Task.checkCancellation()
                guard !stopped else { return }
                if installedPets().contains(where: { $0.productId == order.productId && $0.version == order.productVersion }) { continue }
                if installErrors[order.productId] == nil { await installPet(order.productId, title: order.productTitle ?? order.productId) }
            }
            if client.isPreview, Date() >= nextGrantRefresh {
                nextGrantRefresh = Date().addingTimeInterval(30)
                for installation in installedPets() where isPetEnabled(installation.productId) {
                    do {
                        let refreshed = try await client.authorizeUse(installation)
                        try Task.checkCancellation()
                        guard !stopped else { return }
                        onRefreshPet(refreshed)
                    } catch is CancellationError { return }
                    catch { if !stopped, !client.hasValidStoredUseGrant(installation) { status.stringValue = "桌宠授权暂不可用，联网后可重试启用。" } }
                }
            }
        } catch is CancellationError { }
        catch { if !stopped { status.stringValue = "订单同步失败：\(error.localizedDescription)" } }
    }
    private var displayedOrder: PetMarketOrder? {
        if let id = selectedOrderId, let order = orders.first(where: { $0.id == id && $0.productId == selectedId }) { return order }
        return orders.first { $0.productId == selectedId && $0.status == "pending_payment" }
    }
    private func render() {
        guard !stopped, isViewLoaded else { return }
        market.enclosingScrollView?.isHidden = tabs.selectedSegment != 0
        resourceScroll.isHidden = tabs.selectedSegment == 0
        renderProducts(); renderDetail(); renderResources()
    }

    /// Deterministic AppKit review data is accepted only by an isolated preview client.
    /// It skips timers, catalog requests and preview downloads, so tests cannot contact production.
    func showLayoutPreview(catalog: PetMarketCatalog, resourcePacks: [ResourcePackProduct] = [],
                           orders: [PetMarketOrder] = [], thumbnails: [String: NSImage] = [:],
                           installingProductIds: Set<String> = [], errors: [String: String] = [:], showResources: Bool = false) {
        precondition(client.isPreview, "Layout review requires an isolated shop preview")
        precondition(!started, "Set review data before the live shop starts")
        catalogPreviewOnly = true
        _ = view // AppKit loads an NSViewController's view lazily through this property.
        products = catalog.products; payment = catalog.payment; packs = resourcePacks; self.orders = orders
        thumbnailCache = thumbnails; installing = installingProductIds; installErrors = errors
        selectedId = products.first?.id; tabs.selectedSegment = showResources ? 1 : 0
        updateFilters(); status.stringValue = "\(products.count) 款桌宠 · \(packs.count) 套免费内容"
        render()
    }
    private func matches(_ text: String) -> Bool { search.stringValue.isEmpty || text.localizedCaseInsensitiveContains(search.stringValue) }
    private func owns(_ product: PetMarketProduct) -> Bool {
        product.owned || orders.contains { $0.productId == product.id && $0.status == "paid" }
    }
    private func renderProducts() {
        guard !stopped else { return }
        clear(productStack); thumbnailViews.removeAll()
        let items = products.filter {
            matches($0.title + $0.description + $0.tags.joined()) && (filter.indexOfSelectedItem == 0 || tabs.selectedSegment != 0
                || filter.indexOfSelectedItem == 1 && $0.isFree || filter.indexOfSelectedItem == 2 && !$0.isFree
                || filter.indexOfSelectedItem == 3 && owns($0))
        }
        if items.isEmpty { addFullWidth(label(loading ? "正在加载桌宠…" : "没有找到桌宠，试试其他关键词。", secondary: true), to: productStack) }
        for offset in stride(from: 0, to: items.count, by: catalogColumns) {
            let line = row([]); line.distribution = .fillEqually; line.alignment = .top; line.spacing = 12
            for product in items[offset..<min(items.count, offset + catalogColumns)] {
                let content = column(); content.spacing = 7
                let image = NSImageView(); image.imageScaling = .scaleProportionallyUpOrDown; image.animates = true
                image.image = thumbnailCache[product.id]; image.heightAnchor.constraint(equalToConstant: 104).isActive = true
                image.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
                thumbnailViews[product.id] = image; addFullWidth(image, to: content)
                let title = label(product.title, size: 15, bold: true); title.maximumNumberOfLines = 2
                addFullWidth(title, to: content)
                let summary = label(product.description, secondary: true); summary.maximumNumberOfLines = 2
                summary.lineBreakMode = .byTruncatingTail; addFullWidth(summary, to: content)
                let badges = ([product.isFeatured ? "精选" : "", product.isDefault ? "默认" : ""] + product.tags).filter { !$0.isEmpty }.joined(separator: " · ")
                if !badges.isEmpty {
                    let badge = label(badges, size: 11, secondary: true); badge.maximumNumberOfLines = 1
                    badge.lineBreakMode = .byTruncatingTail; addFullWidth(badge, to: content)
                }
                addFullWidth(label("\(product.priceLabel) · \(product.assetCount) 个动作", bold: true), to: content)
                let state = installedPets().contains { $0.productId == product.id }
                    ? (isPetEnabled(product.id) ? "已安装 · 已启用" : "已安装 · 待启用") : owns(product) ? "已拥有 · 待安装" : "尚未领取或购买"
                addFullWidth(label(state, size: 11, secondary: true), to: content)
                let choose = PetShopButton(selectedId == product.id ? "正在预览" : "查看桌宠") { [weak self] in
                    self?.selectedId = product.id; self?.selectedOrderId = nil; self?.previewIndex = 0; self?.render()
                }
                choose.identifier = NSUserInterfaceItemIdentifier("shop-product-\(product.id)")
                addFullWidth(choose, to: content)
                line.addArrangedSubview(card(content, selected: selectedId == product.id))
                if !catalogPreviewOnly, thumbnailCache[product.id] == nil, thumbnailTasks[product.id] == nil, !product.previewFiles.isEmpty {
                    thumbnailTasks[product.id] = Task { [weak self] in
                        guard let self else { return }
                        if let bytes = try? await client.preview(product, index: 0), !stopped, !Task.isCancelled, let image = NSImage(data: bytes) {
                            thumbnailCache[product.id] = image; thumbnailViews[product.id]?.image = image
                        }
                        thumbnailTasks[product.id] = nil
                    }
                }
            }
            if line.arrangedSubviews.count < catalogColumns { line.addArrangedSubview(NSView()) }
            addFullWidth(line, to: productStack)
        }
    }

    private func renderDetail() {
        guard !stopped, isViewLoaded else { return }
        clear(detailStack); clear(detailActions); countdownLabel = nil
        previewGeneration += 1; let generation = previewGeneration; previewTask?.cancel()
        guard let product = products.first(where: { $0.id == selectedId }) else {
            addFullWidth(label("选择一只桌宠，看看它的动作。", secondary: true), to: detailStack); return
        }
        let selectionChanged = detailProductId != product.id; detailProductId = product.id
        if let order = displayedOrder, order.status == "pending_payment", order.expiresAt > client.serverNow {
            renderPayment(order)
        } else if let order = displayedOrder {
            addFullWidth(label(order.status == "paid" ? "付款验证成功" : order.statusLabel, bold: true), to: detailStack)
        }
        let overview = row([]); overview.alignment = .top
        let preview = column(); preview.spacing = 6
        preview.widthAnchor.constraint(equalToConstant: 118).isActive = true
        let image = NSImageView(); image.imageScaling = .scaleProportionallyUpOrDown; image.animates = true
        image.heightAnchor.constraint(equalToConstant: 132).isActive = true
        image.image = thumbnailCache[product.id]; addFullWidth(image, to: preview)
        image.identifier = NSUserInterfaceItemIdentifier("shop-preview-image")
        if !product.previewFiles.isEmpty {
            previewIndex = min(previewIndex, product.previewFiles.count - 1)
            let previous = PetShopButton("‹") { [weak self] in self?.movePreview(-1) }; previous.toolTip = "上一个动作"
            let next = PetShopButton("›") { [weak self] in self?.movePreview(1) }; next.toolTip = "下一个动作"
            let count = label("\(previewIndex + 1)/\(product.previewFiles.count)", size: 11, secondary: true)
            count.alignment = .center
            let controls = row([previous, count, next]); controls.spacing = 2
            count.setContentHuggingPriority(.defaultLow, for: .horizontal)
            addFullWidth(controls, to: preview)
            let index = previewIndex
            if !catalogPreviewOnly { previewTask = Task { [weak self, weak image] in
                guard let self else { return }
                do {
                    let bytes = try await client.preview(product, index: index)
                    guard !Task.isCancelled, generation == previewGeneration else { return }
                    if let bytes { image?.image = NSImage(data: bytes) }
                } catch is CancellationError { }
                catch { if generation == previewGeneration { status.stringValue = "预览加载失败：\(error.localizedDescription)" } }
            } }
        } else { addFullWidth(label("暂未开放动作预览", size: 11, secondary: true), to: preview) }
        let info = column(); info.setContentHuggingPriority(.defaultLow, for: .horizontal)
        addFullWidth(label(product.title, size: 20, bold: true), to: info)
        addFullWidth(label(product.priceLabel, size: 18, bold: true), to: info)
        addFullWidth(label("同一角色 · \(product.assetCount) 个动作/表情", secondary: true), to: info)
        if owns(product) { addFullWidth(label("已拥有", bold: true, secondary: true), to: info) }
        let badges = ([product.isFeatured ? "精选" : "", product.isDefault ? "默认" : ""] + product.tags).filter { !$0.isEmpty }.joined(separator: " · ")
        if !badges.isEmpty { addFullWidth(label(badges, size: 11, secondary: true), to: info) }
        overview.addArrangedSubview(preview); overview.addArrangedSubview(info)
        addFullWidth(overview, to: detailStack)
        addFullWidth(label(product.description), to: detailStack)
        addFullWidth(label("\(product.priceLabel) · \(owns(product) ? "已拥有" : product.isFree ? "免费桌宠" : "付费桌宠")", size: 16, bold: true), to: detailActions)
        if let error = installErrors[product.id] {
            let message = label(error, secondary: true); message.maximumNumberOfLines = 3
            addFullWidth(message, to: detailActions)
        }
        if purchasing.contains(product.id) {
            let busy = PetShopButton(product.isFree ? "正在领取…" : "正在创建订单…") {}; busy.isEnabled = false
            busy.identifier = NSUserInterfaceItemIdentifier("shop-primary-action"); addFullWidth(busy, to: detailActions)
        } else if installing.contains(product.id) {
            addFullWidth(label("正在下载并校验安装…", bold: true), to: detailActions)
            let busy = PetShopButton("正在安装…") {}; busy.isEnabled = false
            busy.identifier = NSUserInterfaceItemIdentifier("shop-primary-action"); addFullWidth(busy, to: detailActions)
        } else if let installation = installedPets().first(where: { $0.productId == product.id }) {
            let enabled = isPetEnabled(product.id)
            addFullWidth(label(enabled ? "已安装 · 已启用" : "已安装 · 待启用", bold: true), to: detailActions)
            let use = PetShopButton(enabled ? "停用" : "启用桌宠") { [weak self] in self?.togglePet(installation, enable: !enabled) }
            use.identifier = NSUserInterfaceItemIdentifier("shop-primary-action"); addFullWidth(use, to: detailActions)
            let reinstall = PetShopButton("重新安装已拥有版本") { [weak self] in self?.beginInstall(product) }
            addFullWidth(reinstall, to: detailActions)
        } else if owns(product) {
            addFullWidth(label("已拥有 · 待安装", secondary: true), to: detailActions)
            let install = PetShopButton("下载安装") { [weak self] in self?.beginInstall(product) }
            install.identifier = NSUserInterfaceItemIdentifier("shop-primary-action"); addFullWidth(install, to: detailActions)
        } else {
            let pending = displayedOrder.map { $0.status == "pending_payment" && $0.expiresAt > client.serverNow } ?? false
            let button = PetShopButton(pending ? "等待付款确认" : product.isFree ? "免费领取并安装" : "购买 \(product.priceLabel)") { [weak self] in self?.purchase(product) }
            button.identifier = NSUserInterfaceItemIdentifier("shop-primary-action")
            button.isEnabled = !pending && (checkout != nil || product.isFree || !licenses.isActivated || payment?.canOrder != false)
            addFullWidth(button, to: detailActions)
            if checkout == nil, !product.isFree, let payment, !payment.canOrder {
                let message = label(payment.unavailableReason, secondary: true); message.maximumNumberOfLines = 2
                addFullWidth(message, to: detailActions)
            }
        }
        if selectionChanged { scrollToTop(detailScroll) }
    }
    private func movePreview(_ step: Int) {
        guard let product = products.first(where: { $0.id == selectedId }), !product.previewFiles.isEmpty else { return }
        previewIndex = (previewIndex + step + product.previewFiles.count) % product.previewFiles.count; renderDetail()
    }

    private func renderPayment(_ order: PetMarketOrder) {
        let content = column()
        let remaining = max(0, Int(ceil(order.expiresAt.timeIntervalSince(client.serverNow))))
        let countdown = label(String(format: "%02d:%02d", remaining / 60, remaining % 60), bold: true); countdownLabel = countdown
        content.addArrangedSubview(row([label("完成付款", size: 18, bold: true), NSView(), countdown]))
        content.addArrangedSubview(label(String(format: "应付 ¥%.2f", Double(order.amountFen) / 100), size: 20, bold: true))
        if order.payment?.method == "alipay_page" {
            content.addArrangedSubview(PetShopButton("前往支付宝付款") { [weak self] in
                do { try self?.checkout?.reopen(order.id) } catch { self?.show(error) }
            })
            fillColumn(content)
            let paymentCard = card(content, selected: true)
            detailStack.addArrangedSubview(paymentCard)
            paymentCard.widthAnchor.constraint(equalTo: detailStack.widthAnchor).isActive = true
            return
        }
        content.addArrangedSubview(row([label(order.paymentReference, size: 24, bold: true),
            PetShopButton("复制备注码") { Self.copy(order.paymentReference) }]))
        if let payment = order.payment {
            content.addArrangedSubview(label("收款人：\(payment.recipientName)"))
            if payment.method == "personal_transfer" {
                content.addArrangedSubview(row([label("微信号：\(payment.recipientAccount)"),
                    PetShopButton("复制") { Self.copy(payment.recipientAccount) }]))
                content.addArrangedSubview(label("请在微信好友转账的说明中填写上方完整五位备注码。", secondary: true))
            } else if payment.method == "collection_qr", let image = qrImage(payment.qrCodeDataUrl) {
                let qr = NSImageView(); qr.image = image; qr.imageScaling = .scaleProportionallyUpOrDown
                qr.heightAnchor.constraint(equalToConstant: 180).isActive = true; content.addArrangedSubview(qr)
            }
            if !payment.instructions.isEmpty { content.addArrangedSubview(label(payment.instructions, secondary: true)) }
            if !payment.available { content.addArrangedSubview(label("暂勿付款：\(payment.unavailableReason)", bold: true)) }
        }
        content.addArrangedSubview(label("确认成功后自动安装，不会自动启用。订单到期后付款区域自动收起。", secondary: true))
        content.addArrangedSubview(PetShopButton("取消订单") { [weak self] in
            self?.action { [weak self] in guard let self else { return }; try await client.cancelOrder(order.id); await syncOrders() }
        })
        fillColumn(content)
        let paymentCard = card(content, selected: true)
        detailStack.addArrangedSubview(paymentCard); paymentCard.widthAnchor.constraint(equalTo: detailStack.widthAnchor).isActive = true
    }
    private func qrImage(_ text: String) -> NSImage? {
        guard text.count <= 768 * 1024,
              let prefix = ["data:image/png;base64,", "data:image/jpeg;base64,"].first(where: text.hasPrefix),
              let bytes = Data(base64Encoded: String(text.dropFirst(prefix.count))), bytes.count <= 512 * 1024 else { return nil }
        return NSImage(data: bytes)
    }

    private func purchase(_ product: PetMarketProduct) {
        guard licenses.isActivated else { requestActivation(); return }
        guard purchasing.insert(product.id).inserted else { return }
        renderDetail()
        action { [weak self] in
            guard let self else { return }
            defer { purchasing.remove(product.id); renderDetail() }
            if product.isFree {
                try await client.claim(product.id); await installPet(product.id, title: product.title); reload()
            } else {
                if let checkout {
                    let result = try await checkout.purchase(activation: false, productId: product.id)
                    selectedOrderId = result.order.id; status.stringValue = "等待付款"
                    await syncOrders(); nextPoll = .distantPast
                    return
                }
                let order = try await client.createOrder(product.id)
                try Task.checkCancellation()
                guard !stopped else { return }
                orders.removeAll { $0.id == order.id }; orders.insert(order, at: 0); selectedOrderId = order.id
                nextPoll = .distantPast; renderDetail(); scrollToTop(detailScroll); renderOrderList()
            }
        }
    }
    private func beginInstall(_ product: PetMarketProduct) {
        guard licenses.isActivated else { requestActivation(); return }
        action { [weak self] in await self?.installPet(product.id, title: product.title) }
    }
    private func installPet(_ id: String, title: String) async {
        guard !stopped, installing.insert(id).inserted else { return }
        installErrors[id] = nil; renderDetail(); renderOrderList()
        defer { installing.remove(id); renderProducts(); renderDetail(); renderOrderList() }
        do {
            let installation = try await client.install(id, title: title)
            try Task.checkCancellation()
            guard !stopped else { return }
            onInstallPet(installation); status.stringValue = "\(installation.title) 已安装"
        } catch is CancellationError { }
        catch { if !stopped { installErrors[id] = "安装未完成：\(error.localizedDescription)" } }
    }
    private func togglePet(_ installation: PetMarketInstallation, enable: Bool) {
        if enable && !licenses.isActivated { requestActivation(); return }
        action { [weak self] in
            guard let self else { return }
            if enable {
                let refreshed = try await client.authorizeUse(installation)
                try Task.checkCancellation()
                guard !stopped else { return }
                onRefreshPet(refreshed)
            }
            try onTogglePet(installation.productId, enable); renderProducts(); renderDetail()
        }
    }

    private func renderResources() {
        guard !stopped, isViewLoaded else { return }; clear(resourceStack)
        let items = packs.filter {
            let category = filter.indexOfSelectedItem
            return matches($0.title + $0.description) && (tabs.selectedSegment == 0 || category == 0
                || category == 1 && $0.isWords || category == 2 && !$0.isWords
                || category == 3 && resourceState($0.id, $0.sha256) != "not-installed")
        }
        if items.isEmpty { addFullWidth(label(loading ? "正在加载资源…" : "没有找到资源，试试清除筛选。", secondary: true), to: resourceStack) }
        let columns = max(1, resourceColumns)
        for offset in stride(from: 0, to: items.count, by: columns) {
            let line = NSStackView(); line.orientation = .horizontal; line.distribution = .fillEqually; line.spacing = 14; line.alignment = .top
            for pack in items[offset..<min(items.count, offset + columns)] {
                let content = column()
                let category = label(pack.isWords ? "✦ 互动词库 · 免费" : "◈ 小剧场 · 免费", bold: true)
                category.textColor = pack.isWords ? .systemTeal : .systemPurple
                content.addArrangedSubview(category); content.addArrangedSubview(label(pack.title, size: 18, bold: true))
                let description = label(pack.description, secondary: true); description.maximumNumberOfLines = 3
                description.lineBreakMode = .byTruncatingTail; content.addArrangedSubview(description)
                content.addArrangedSubview(label("\(pack.fileCount) 份内容 · v\(pack.version)", secondary: true))
                let state = resourceState(pack.id, pack.sha256), key = "resource:\(pack.id)"
                let installed = state != "not-installed"
                let button = PetShopButton(installing.contains(key) ? "正在安装…" : !installed ? "免费安装" : state == "enabled" ? "停用" : "启用") { [weak self] in
                    guard let self else { return }
                    if installed {
                        do { try onToggleResources(pack.id); renderResources() } catch { show(error) }
                    } else {
                        guard installing.insert(key).inserted else { return }; installErrors[key] = nil; renderResources()
                        action { [weak self] in
                            guard let self else { return }
                            defer { installing.remove(key); renderResources() }
                            do {
                                let parsed = try await resources.install(pack)
                                try Task.checkCancellation()
                                guard !stopped else { return }
                                try onInstallResources(parsed)
                                status.stringValue = "\(pack.title) 已安装 · 待启用"
                            } catch is CancellationError { }
                            catch { if !stopped { installErrors[key] = "安装未完成：\(error.localizedDescription)"; show(error) } }
                        }
                    }
                }
                button.identifier = NSUserInterfaceItemIdentifier("shop-resource-action-\(pack.id)")
                button.isEnabled = !installing.contains(key)
                content.addArrangedSubview(label(installing.contains(key) ? "正在下载并校验安装…" : installed ? (state == "enabled" ? "已安装 · 已启用" : "已安装 · 待启用") : "尚未安装 · 安装后手动启用", secondary: true))
                if let error = installErrors[key] {
                    let message = label(error, secondary: true); message.maximumNumberOfLines = 3; content.addArrangedSubview(message)
                }
                content.addArrangedSubview(button); fillColumn(content); line.addArrangedSubview(card(content))
            }
            if line.arrangedSubviews.count < columns { line.addArrangedSubview(NSView()) }
            resourceStack.addArrangedSubview(line); line.widthAnchor.constraint(equalTo: resourceStack.widthAnchor).isActive = true
        }
    }

    private func showOrders() {
        guard !stopped else { return }
        guard licenses.isActivated else { requestActivation(); return }
        if ordersWindow == nil {
            let controller = NSViewController(), stack = column()
            controller.view = NSView(); let scroller = scroll(stack)
            scroller.translatesAutoresizingMaskIntoConstraints = false; controller.view.addSubview(scroller)
            NSLayoutConstraint.activate([scroller.leadingAnchor.constraint(equalTo: controller.view.leadingAnchor, constant: 18),
                scroller.trailingAnchor.constraint(equalTo: controller.view.trailingAnchor, constant: -18),
                scroller.topAnchor.constraint(equalTo: controller.view.topAnchor, constant: 18), scroller.bottomAnchor.constraint(equalTo: controller.view.bottomAnchor, constant: -18)])
            let window = NSWindow(contentViewController: controller)
            window.title = "我的小铺订单"; window.setContentSize(NSSize(width: 700, height: 570)); window.minSize = NSSize(width: 520, height: 360)
            window.styleMask.insert(.resizable); window.isReleasedWhenClosed = false; window.center()
            ordersWindow = NSWindowController(window: window); orderStack = stack
        }
        renderOrderList(); ordersWindow?.showWindow(nil); ordersWindow?.window?.makeKeyAndOrderFront(nil)
        pollTask = Task { [weak self] in await self?.syncOrders() }
    }
    private func renderOrderList() {
        guard !stopped, let stack = orderStack else { return }; clear(stack)
        stack.addArrangedSubview(label("订单与安装记录", size: 22, bold: true))
        if orders.isEmpty { stack.addArrangedSubview(label("暂无订单", secondary: true)) }
        for order in orders {
            let content = column()
            content.addArrangedSubview(row([label(order.productTitle ?? order.productId, size: 16, bold: true), NSView(), label(order.statusLabel, bold: true)]))
            content.addArrangedSubview(label(String(format: "¥%.2f", Double(order.amountFen) / 100)))
            content.addArrangedSubview(label("订单号：\(order.id)", secondary: true))
            content.addArrangedSubview(label("下单：\(order.createdAt.formatted(date: .numeric, time: .shortened))"
                + (order.paidAt.map { " · 付款：\($0.formatted(date: .numeric, time: .shortened))" } ?? ""), secondary: true))
            var buttons: [NSView] = [PetShopButton("复制订单号") { Self.copy(order.id) }]
            if order.status == "pending_payment" {
                buttons.append(PetShopButton("查看付款") { [weak self] in
                    guard let self else { return }; selectedId = order.productId; selectedOrderId = order.id
                    tabs.selectedSegment = 0; updateFilters(); render(); ordersWindow?.close()
                    view.window?.makeKeyAndOrderFront(nil)
                })
            } else if order.status == "paid" {
                let installed = installedPets().contains { $0.productId == order.productId && $0.version == order.productVersion }
                content.addArrangedSubview(label(installed ? "付款验证成功 · 已安装" : installing.contains(order.productId) ? "付款验证成功 · 正在安装" : "付款验证成功 · 待安装", bold: true))
                if let error = installErrors[order.productId] { content.addArrangedSubview(label(error, secondary: true)) }
                if !installed {
                    let retry = PetShopButton("重试安装") { [weak self] in
                        self?.action { [weak self] in await self?.installPet(order.productId, title: order.productTitle ?? order.productId) }
                    }
                    retry.isEnabled = !installing.contains(order.productId); buttons.append(retry)
                }
            }
            content.addArrangedSubview(row(buttons)); let orderCard = card(content); stack.addArrangedSubview(orderCard)
            orderCard.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
        }
    }
    private func action(_ operation: @escaping @MainActor () async throws -> Void) {
        guard !stopped else { return }
        let id = UUID()
        actionTasks[id] = Task { [weak self] in
            guard let self else { return }
            defer { actionTasks[id] = nil }
            do { try Task.checkCancellation(); try await operation() }
            catch is CancellationError { }
            catch { if !stopped { show(error) } }
        }
    }
    private func show(_ error: Error) { status.stringValue = error.localizedDescription; presentFriendlyError(error, title: "小铺操作未完成") }
    private static func copy(_ text: String) { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(text, forType: .string) }
    private func clear(_ stack: NSStackView) { stack.arrangedSubviews.forEach { stack.removeArrangedSubview($0); $0.removeFromSuperview() } }
    private func label(_ text: String, size: CGFloat = 13, bold: Bool = false, secondary: Bool = false) -> NSTextField {
        let label = NSTextField(wrappingLabelWithString: text); label.font = .systemFont(ofSize: size, weight: bold ? .semibold : .regular)
        label.textColor = secondary ? .secondaryLabelColor : .labelColor; label.isSelectable = true
        label.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        label.setContentHuggingPriority(.defaultHigh, for: .vertical)
        return label
    }
    private func column() -> NSStackView {
        let stack = NSStackView(); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 9; return stack
    }
    private func row(_ views: [NSView]) -> NSStackView {
        let stack = NSStackView(views: views); stack.orientation = .horizontal; stack.alignment = .centerY; stack.spacing = 9; return stack
    }
    private func addFullWidth(_ child: NSView, to stack: NSStackView) {
        stack.addArrangedSubview(child)
        child.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
    }
    private func fillColumn(_ stack: NSStackView) {
        for child in stack.arrangedSubviews { child.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true }
    }
    private func scrollToTop(_ scroll: NSScrollView) {
        scroll.contentView.scroll(to: .zero); scroll.reflectScrolledClipView(scroll.contentView)
    }
    private func card(_ content: NSView, selected: Bool = false) -> NSView {
        let card = NSView(); card.wantsLayer = true; card.layer?.cornerRadius = 10
        card.layer?.backgroundColor = (selected ? NSColor.systemTeal.withAlphaComponent(0.09) : NSColor.controlBackgroundColor).cgColor
        card.layer?.borderWidth = 1; card.layer?.borderColor = (selected ? NSColor.systemTeal : NSColor.separatorColor).cgColor
        card.addSubview(content); content.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([content.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 14),
            content.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -14),
            content.topAnchor.constraint(equalTo: card.topAnchor, constant: 14), content.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -14)])
        card.setContentHuggingPriority(.defaultLow, for: .horizontal); return card
    }
    private func scroll(_ stack: NSStackView) -> NSScrollView { let scroll = NSScrollView(); configureScroll(scroll, stack: stack); return scroll }
    private func configureScroll(_ scroll: NSScrollView, stack: NSStackView) {
        scroll.hasVerticalScroller = true; scroll.hasHorizontalScroller = false; scroll.drawsBackground = false
        scroll.autohidesScrollers = true
        stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 12
        let document = PetShopDocumentView(); document.translatesAutoresizingMaskIntoConstraints = false
        scroll.documentView = document; stack.translatesAutoresizingMaskIntoConstraints = false; document.addSubview(stack)
        NSLayoutConstraint.activate([document.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
            document.leadingAnchor.constraint(equalTo: scroll.contentView.leadingAnchor),
            document.topAnchor.constraint(equalTo: scroll.contentView.topAnchor),
            stack.leadingAnchor.constraint(equalTo: document.leadingAnchor), stack.trailingAnchor.constraint(equalTo: document.trailingAnchor),
            stack.topAnchor.constraint(equalTo: document.topAnchor), stack.bottomAnchor.constraint(equalTo: document.bottomAnchor)])
    }
}
