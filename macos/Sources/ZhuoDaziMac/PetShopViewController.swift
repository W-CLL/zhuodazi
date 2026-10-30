import AppKit

@MainActor
private final class PetShopButton: NSButton {
    private var actionHandler: (() -> Void)?
    init(_ title: String, action: @escaping () -> Void) {
        super.init(frame: .zero)
        self.title = title; actionHandler = action; target = self; self.action = #selector(run)
        bezelStyle = .rounded
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }
    @objc private func run() { actionHandler?() }
}

@MainActor
final class PetShopViewController: NSViewController, NSTextFieldDelegate {
    private let licenses: LicenseService
    private let client: PetMarketClient
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
    private var thumbnailCache: [String: NSImage] = [:]
    private var thumbnailTasks: [String: Task<Void, Never>] = [:]
    private var thumbnailViews: [String: NSImageView] = [:]
    private let tabs = NSSegmentedControl(labels: ["桌宠图鉴", "小剧场 / 互动词库"], trackingMode: .selectOne, target: nil, action: nil)
    private let search = NSSearchField()
    private let filter = NSPopUpButton()
    private let status = NSTextField(wrappingLabelWithString: "")
    private let productStack = NSStackView(), detailStack = NSStackView(), resourceStack = NSStackView()
    private let market = NSView(), resourceScroll = NSScrollView()
    private var countdownLabel: NSTextField?
    private var ordersWindow: NSWindowController?
    private var orderStack: NSStackView?
    private var resourceColumns = 0

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
        let root = NSStackView(); root.orientation = .vertical; root.alignment = .leading; root.spacing = 12
        root.translatesAutoresizingMaskIntoConstraints = false; view.addSubview(root)
        NSLayoutConstraint.activate([root.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 20),
            root.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -20),
            root.topAnchor.constraint(equalTo: view.topAnchor, constant: 18), root.bottomAnchor.constraint(equalTo: view.bottomAnchor, constant: -18)])
        let heading = row([label("桌宠小铺", size: 24, bold: true), NSView(),
            PetShopButton("订单记录") { [weak self] in self?.showOrders() },
            PetShopButton("刷新") { [weak self] in self?.reload() }])
        root.addArrangedSubview(heading); heading.widthAnchor.constraint(equalTo: root.widthAnchor).isActive = true
        if client.isPreview { root.addArrangedSubview(label("本地独立预览 · 只连接配置的本机后台", secondary: true)) }
        tabs.selectedSegment = 0; tabs.target = self; tabs.action = #selector(changeTab)
        root.addArrangedSubview(tabs)
        search.placeholderString = "搜索名称、介绍或标签"; search.delegate = self
        filter.target = self; filter.action = #selector(changeFilter); updateFilters()
        let controls = row([search, filter]); root.addArrangedSubview(controls)
        controls.widthAnchor.constraint(equalTo: root.widthAnchor).isActive = true
        filter.widthAnchor.constraint(equalToConstant: 120).isActive = true
        status.textColor = .secondaryLabelColor; root.addArrangedSubview(status)
        let content = NSView(); root.addArrangedSubview(content)
        content.widthAnchor.constraint(equalTo: root.widthAnchor).isActive = true
        content.heightAnchor.constraint(greaterThanOrEqualToConstant: 280).isActive = true
        content.translatesAutoresizingMaskIntoConstraints = false

        // Both columns are constrained to exactly the same width, with their own scrollbars.
        let horizontal = NSScrollView(); horizontal.hasHorizontalScroller = true; horizontal.hasVerticalScroller = false
        horizontal.drawsBackground = false; horizontal.documentView = market
        market.translatesAutoresizingMaskIntoConstraints = false
        let left = scroll(productStack), right = scroll(detailStack)
        for panel in [left, right] { market.addSubview(panel); panel.translatesAutoresizingMaskIntoConstraints = false }
        NSLayoutConstraint.activate([market.widthAnchor.constraint(greaterThanOrEqualToConstant: 720),
            market.widthAnchor.constraint(greaterThanOrEqualTo: horizontal.contentView.widthAnchor),
            market.heightAnchor.constraint(equalTo: horizontal.contentView.heightAnchor),
            left.leadingAnchor.constraint(equalTo: market.leadingAnchor), left.topAnchor.constraint(equalTo: market.topAnchor),
            left.bottomAnchor.constraint(equalTo: market.bottomAnchor), right.trailingAnchor.constraint(equalTo: market.trailingAnchor),
            right.topAnchor.constraint(equalTo: market.topAnchor), right.bottomAnchor.constraint(equalTo: market.bottomAnchor),
            right.leadingAnchor.constraint(equalTo: left.trailingAnchor, constant: 14), left.widthAnchor.constraint(equalTo: right.widthAnchor)])
        let preferredWidth = market.widthAnchor.constraint(equalTo: horizontal.contentView.widthAnchor)
        preferredWidth.priority = .defaultHigh; preferredWidth.isActive = true
        configureScroll(resourceScroll, stack: resourceStack)
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
        guard !stopped else { return }
        if !started {
            started = true; reload()
            timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
                Task { @MainActor [weak self] in self?.tick() }
            }
        } else { render() }
    }
    override func viewDidLayout() {
        super.viewDidLayout()
        let columns = view.bounds.width >= 900 ? 2 : 1
        if columns != resourceColumns { resourceColumns = columns; if started { renderResources() } }
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
            let updatedOrders = try await client.orders()
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
        if items.isEmpty { productStack.addArrangedSubview(label(loading ? "正在加载桌宠…" : "没有找到桌宠，试试其他关键词。", secondary: true)) }
        for product in items {
            let image = NSImageView(); image.imageScaling = .scaleProportionallyUpOrDown; image.animates = true
            image.image = thumbnailCache[product.id]; image.widthAnchor.constraint(equalToConstant: 84).isActive = true
            image.heightAnchor.constraint(equalToConstant: 84).isActive = true; thumbnailViews[product.id] = image
            let text = column(); text.addArrangedSubview(label(product.title, size: 16, bold: true))
            text.addArrangedSubview(label(product.description, secondary: true))
            let badges = ([product.isFeatured ? "精选" : "", product.isDefault ? "默认" : ""] + product.tags).filter { !$0.isEmpty }.joined(separator: " · ")
            if !badges.isEmpty { text.addArrangedSubview(label(badges, secondary: true)) }
            text.addArrangedSubview(label("\(product.assetCount) 个动作 · \(owns(product) ? "已拥有" : product.priceLabel)", bold: true))
            let choose = PetShopButton(selectedId == product.id ? "正在预览" : "查看桌宠") { [weak self] in
                self?.selectedId = product.id; self?.selectedOrderId = nil; self?.previewIndex = 0; self?.render()
            }
            text.addArrangedSubview(choose)
            let item = card(row([image, text]), selected: selectedId == product.id)
            productStack.addArrangedSubview(item); item.widthAnchor.constraint(equalTo: productStack.widthAnchor).isActive = true
            if thumbnailCache[product.id] == nil, thumbnailTasks[product.id] == nil, !product.previewFiles.isEmpty {
                thumbnailTasks[product.id] = Task { [weak self] in
                    guard let self else { return }
                    if let bytes = try? await client.preview(product, index: 0), !stopped, !Task.isCancelled, let image = NSImage(data: bytes) {
                        thumbnailCache[product.id] = image; thumbnailViews[product.id]?.image = image
                    }
                    thumbnailTasks[product.id] = nil
                }
            }
        }
    }

    private func renderDetail() {
        guard !stopped, isViewLoaded else { return }
        clear(detailStack); countdownLabel = nil
        previewGeneration += 1; let generation = previewGeneration; previewTask?.cancel()
        guard let product = products.first(where: { $0.id == selectedId }) else {
            detailStack.addArrangedSubview(label("选择一只桌宠，看看它的动作。", secondary: true)); return
        }
        if let order = displayedOrder, order.status == "pending_payment", order.expiresAt > client.serverNow {
            renderPayment(order)
        } else if let order = displayedOrder {
            detailStack.addArrangedSubview(label(order.status == "paid" ? "付款验证成功" : order.statusLabel, bold: true))
        }
        detailStack.addArrangedSubview(label(product.title, size: 22, bold: true))
        let image = NSImageView(); image.imageScaling = .scaleProportionallyUpOrDown; image.animates = true
        image.heightAnchor.constraint(equalToConstant: 200).isActive = true
        image.image = thumbnailCache[product.id]; detailStack.addArrangedSubview(image)
        image.widthAnchor.constraint(equalTo: detailStack.widthAnchor).isActive = true
        if !product.previewFiles.isEmpty {
            previewIndex = min(previewIndex, product.previewFiles.count - 1)
            detailStack.addArrangedSubview(row([PetShopButton("上一个") { [weak self] in self?.movePreview(-1) },
                label("预览 \(previewIndex + 1) / \(product.previewFiles.count)", secondary: true),
                PetShopButton("下一个") { [weak self] in self?.movePreview(1) }]))
            let index = previewIndex
            previewTask = Task { [weak self, weak image] in
                guard let self else { return }
                do {
                    let bytes = try await client.preview(product, index: index)
                    guard !Task.isCancelled, generation == previewGeneration else { return }
                    if let bytes { image?.image = NSImage(data: bytes) }
                } catch is CancellationError { }
                catch { if generation == previewGeneration { status.stringValue = "预览加载失败：\(error.localizedDescription)" } }
            }
        } else { detailStack.addArrangedSubview(label("这款桌宠暂未开放动作预览。", secondary: true)) }
        detailStack.addArrangedSubview(label(product.description))
        detailStack.addArrangedSubview(label("同一角色 · \(product.assetCount) 个动作/表情 · \(product.priceLabel)", secondary: true))
        if let error = installErrors[product.id] { detailStack.addArrangedSubview(label(error, secondary: true)) }
        if installing.contains(product.id) { detailStack.addArrangedSubview(label("正在下载并校验安装…", bold: true)); return }
        if let installation = installedPets().first(where: { $0.productId == product.id }) {
            let enabled = isPetEnabled(product.id)
            detailStack.addArrangedSubview(label(enabled ? "已安装 · 已启用" : "已安装 · 待启用", bold: true))
            detailStack.addArrangedSubview(PetShopButton(enabled ? "停用" : "启用桌宠") { [weak self] in
                self?.togglePet(installation, enable: !enabled)
            })
            detailStack.addArrangedSubview(PetShopButton("重新安装已拥有版本") { [weak self] in self?.beginInstall(product) })
        } else if owns(product) {
            detailStack.addArrangedSubview(PetShopButton("下载安装") { [weak self] in self?.beginInstall(product) })
        } else {
            let button = PetShopButton(product.isFree ? "免费领取并安装" : "购买 \(product.priceLabel)") { [weak self] in self?.purchase(product) }
            button.isEnabled = displayedOrder?.status != "pending_payment" || displayedOrder!.expiresAt <= client.serverNow
            detailStack.addArrangedSubview(button)
            if !product.isFree, let payment, !payment.canOrder { detailStack.addArrangedSubview(label(payment.unavailableReason, secondary: true)) }
        }
        detailStack.addArrangedSubview(label("安装后由你手动启用。试用期间可以浏览，正式激活后可领取和购买桌宠。", secondary: true))
    }
    private func movePreview(_ step: Int) {
        guard let product = products.first(where: { $0.id == selectedId }), !product.previewFiles.isEmpty else { return }
        previewIndex = (previewIndex + step + product.previewFiles.count) % product.previewFiles.count; renderDetail()
    }

    private func renderPayment(_ order: PetMarketOrder) {
        let content = column()
        let countdown = label("", bold: true); countdownLabel = countdown
        content.addArrangedSubview(row([label("完成付款", size: 18, bold: true), NSView(), countdown]))
        content.addArrangedSubview(label(String(format: "应付 ¥%.2f", Double(order.amountFen) / 100), size: 20, bold: true))
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
        action { [weak self] in
            guard let self else { return }
            if product.isFree {
                try await client.claim(product.id); await installPet(product.id, title: product.title); reload()
            } else {
                let order = try await client.createOrder(product.id)
                try Task.checkCancellation()
                guard !stopped else { return }
                orders.removeAll { $0.id == order.id }; orders.insert(order, at: 0); selectedOrderId = order.id
                nextPoll = .distantPast; renderDetail(); renderOrderList()
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
        defer { installing.remove(id); renderDetail(); renderOrderList() }
        do {
            let installation = try await client.install(id, title: title)
            try Task.checkCancellation()
            guard !stopped else { return }
            onInstallPet(installation); status.stringValue = "\(installation.title) 已安装，等待你启用。"
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
            try onTogglePet(installation.productId, enable); renderDetail()
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
        if items.isEmpty { resourceStack.addArrangedSubview(label(loading ? "正在加载资源…" : "没有找到资源，试试清除筛选。", secondary: true)) }
        let columns = max(1, resourceColumns)
        for offset in stride(from: 0, to: items.count, by: columns) {
            let line = NSStackView(); line.orientation = .horizontal; line.distribution = .fillEqually; line.spacing = 14; line.alignment = .top
            for pack in items[offset..<min(items.count, offset + columns)] {
                let content = column()
                let category = label(pack.isWords ? "✦ 互动词库 · 免费" : "◈ 小剧场 · 免费", bold: true)
                category.textColor = pack.isWords ? .systemTeal : .systemPurple
                content.addArrangedSubview(category); content.addArrangedSubview(label(pack.title, size: 18, bold: true))
                content.addArrangedSubview(label(pack.description, secondary: true))
                content.addArrangedSubview(label("\(pack.fileCount) 份内容 · v\(pack.version)", secondary: true))
                let state = resourceState(pack.id, pack.sha256), key = "resource:\(pack.id)"
                let installed = state != "not-installed"
                let button = PetShopButton(installing.contains(key) ? "正在安装…" : !installed ? "免费安装" : state == "enabled" ? "停用" : "启用") { [weak self] in
                    guard let self else { return }
                    if installed {
                        do { try onToggleResources(pack.id); renderResources() } catch { show(error) }
                    } else {
                        guard installing.insert(key).inserted else { return }; renderResources()
                        action { [weak self] in
                            guard let self else { return }
                            defer { installing.remove(key); renderResources() }
                            let parsed = try await resources.install(pack)
                            try Task.checkCancellation()
                            guard !stopped else { return }
                            try onInstallResources(parsed)
                            status.stringValue = "\(pack.title) 已安装 · 待启用"
                        }
                    }
                }
                button.isEnabled = !installing.contains(key)
                if installed { content.addArrangedSubview(label(state == "enabled" ? "已安装 · 已启用" : "已安装 · 待启用", secondary: true)) }
                content.addArrangedSubview(button); line.addArrangedSubview(card(content))
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
        stack.addArrangedSubview(label("付款记录保存在账号中；安装与启用状态只代表这台 Mac。", secondary: true))
        if orders.isEmpty { stack.addArrangedSubview(label("暂无付款订单。免费领取不会产生付款记录。", secondary: true)) }
        for order in orders {
            let content = column()
            content.addArrangedSubview(row([label(order.productTitle ?? order.productId, size: 16, bold: true), NSView(), label(order.statusLabel, bold: true)]))
            content.addArrangedSubview(label(String(format: "¥%.2f · v%@ · 备注 %@", Double(order.amountFen) / 100, order.productVersion, order.paymentReference)))
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
        label.textColor = secondary ? .secondaryLabelColor : .labelColor; label.isSelectable = true; return label
    }
    private func column() -> NSStackView {
        let stack = NSStackView(); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 9; return stack
    }
    private func row(_ views: [NSView]) -> NSStackView {
        let stack = NSStackView(views: views); stack.orientation = .horizontal; stack.alignment = .centerY; stack.spacing = 9; return stack
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
        stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 12
        stack.translatesAutoresizingMaskIntoConstraints = false; scroll.documentView = stack
        stack.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor).isActive = true
        stack.leadingAnchor.constraint(equalTo: scroll.contentView.leadingAnchor).isActive = true
        stack.topAnchor.constraint(equalTo: scroll.contentView.topAnchor).isActive = true
    }
}
