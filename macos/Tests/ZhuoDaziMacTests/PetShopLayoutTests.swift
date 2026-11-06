import AppKit
import CryptoKit
import XCTest
@testable import ZhuoDaziMac

/// These checks exercise native Auto Layout with the real shop controller, rather than a screenshot mock.
final class PetShopLayoutTests: XCTestCase {
    @MainActor
    private final class Fixture {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("shop-layout-\(UUID().uuidString)")
        let controller: PetShopViewController

        init(activated: Bool = true, pets: [PetMarketInstallation] = [], resourceStates: [String: String] = [:]) throws {
            let key = Curve25519.Signing.PrivateKey().publicKey.rawRepresentation
            let spki = Data([0x30,0x2a,0x30,0x05,0x06,0x03,0x2b,0x65,0x70,0x03,0x21,0x00]) + key
            let configuration = PetShopPreviewConfiguration(baseUrl: "http://127.0.0.1:9/", dataDirectory: directory.path,
                accountId: "layout-review", licenseId: activated ? UUID().uuidString : "not-activated",
                installationId: String(repeating: "a", count: 24), credential: String(repeating: "b", count: 48),
                signingPublicKeySpki: spki.base64EncodedString())
            let license = try LicenseService(preview: configuration)
            let client = try PetMarketClient(licenses: license, preview: configuration)
            // Closures refer to a box to avoid capturing a partially initialized Fixture.
            let state = FixtureState(pets: pets)
            self.state = state
            controller = PetShopViewController(licenses: license, client: client,
                installedPets: { state.pets }, onInstallPet: { state.pets.append($0) }, onRefreshPet: { _ in },
                isPetEnabled: { _ in state.enabled }, onTogglePet: { _, value in state.enabled = value },
                resourceState: { id, _ in resourceStates[id] ?? "not-installed" },
                onInstallResources: { _ in }, onToggleResources: { _ in }, requestActivation: { state.activationRequests += 1 })
        }
        let state: FixtureState
        func cleanup() { controller.stop(); try? FileManager.default.removeItem(at: directory) }
    }

    @MainActor
    private final class FixtureState {
        var pets: [PetMarketInstallation]
        var enabled = false
        var activationRequests = 0
        init(pets: [PetMarketInstallation]) { self.pets = pets }
    }

    private func product(_ id: String = "paid-pet", free: Bool = false, owned: Bool = false, long: Bool = false) -> PetMarketProduct {
        PetMarketProduct(id: id, title: "\(free ? "免费" : "精选")陪伴小伙伴",
            description: long ? String(repeating: "下班回到桌面，换个动作陪你休息一会儿。", count: 100) : "挑一只喜欢的角色，安装后手动启用。",
            chargingMode: free ? "free" : "paid", priceFen: free ? 0 : 688, currency: "CNY", isDefault: false,
            isFeatured: true, tags: ["可爱", "陪伴", "日常"], sortOrder: 0, activeVersion: "1.0.0", assetCount: 30,
            previewFiles: [PetMarketPreview(path: "preview.gif", sha256: String(repeating: "0", count: 64), sizeBytes: 100)],
            owned: owned, ownedVersion: owned ? "1.0.0" : nil, entitlementVersion: nil)
    }

    private func payment() -> PetMarketPayment {
        PetMarketPayment(enabled: true, available: true, method: "personal_transfer", recipientName: "本地测试收款人",
            recipientAccount: "local-review", qrCodeDataUrl: "", instructions: "测试订单，不要实际付款。", unavailableReason: "", listenerOnline: true)
    }

    @MainActor
    private func thumbnail(tall: Bool = false) throws -> NSImage {
        let macOSRoot = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let image = try XCTUnwrap(NSImage(contentsOf: macOSRoot.appendingPathComponent("Resources/Pets/default-pet.gif")))
        if tall { image.size = NSSize(width: 160, height: 4000) }
        return image
    }

    @MainActor
    private func descendants(_ view: NSView) -> [NSView] { [view] + view.subviews.flatMap(descendants) }

    @MainActor
    private func find<T: NSView>(_ id: String, in view: NSView, as type: T.Type = T.self) throws -> T {
        try XCTUnwrap(descendants(view).first { $0.identifier?.rawValue == id } as? T, "Missing native control \(id)")
    }

    @MainActor
    private func layout(_ fixture: Fixture, size: NSSize) -> NSWindow {
        let window = NSWindow(contentViewController: fixture.controller)
        window.styleMask = [.titled, .closable, .resizable]
        window.setContentSize(size); window.isReleasedWhenClosed = false
        window.orderFront(nil)
        for _ in 0..<4 { fixture.controller.view.layoutSubtreeIfNeeded() }
        return window
    }

    @MainActor
    private func assertActionVisible(_ controller: PetShopViewController, expected: String, file: StaticString = #filePath, line: UInt = #line) throws {
        let view = controller.view
        let button: NSButton = try find("shop-primary-action", in: view)
        let rect = view.convert(button.bounds, from: button)
        XCTAssertEqual(button.title, expected, file: file, line: line)
        XCTAssertGreaterThanOrEqual(rect.height, 20, file: file, line: line)
        XCTAssertTrue(view.bounds.insetBy(dx: -1, dy: -1).contains(rect), "Action is outside the content viewport: \(rect) in \(view.bounds)", file: file, line: line)
        XCTAssertFalse(button.isHiddenOrHasHiddenAncestor, file: file, line: line)
        let detail: NSView = try find("shop-detail-panel", in: view)
        let catalog: NSScrollView = try find("shop-catalog-scroll", in: view)
        let scroll: NSScrollView = try find("shop-detail-scroll", in: view)
        XCTAssertEqual(detail.bounds.width, catalog.bounds.width, accuracy: 1, file: file, line: line)
        XCTAssertGreaterThan(scroll.bounds.height, 80, file: file, line: line)
        XCTAssertTrue(scroll.documentView?.isFlipped == true, file: file, line: line)
        XCTAssertLessThanOrEqual(scroll.contentView.bounds.minY, 1, file: file, line: line)
    }

    @MainActor
    private func capture(_ controller: PetShopViewController, name: String) throws {
        guard let output = ProcessInfo.processInfo.environment["ZHUODAZI_REVIEW_OUTPUT"], !output.isEmpty else { return }
        let directory = URL(fileURLWithPath: output, isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let view = controller.view
        let bitmap = try XCTUnwrap(view.bitmapImageRepForCachingDisplay(in: view.bounds))
        view.cacheDisplay(in: view.bounds, to: bitmap)
        let bytes = try XCTUnwrap(bitmap.representation(using: .png, properties: [:]))
        try bytes.write(to: directory.appendingPathComponent(name + ".png"))
    }

    @MainActor
    func testPurchaseStaysVisibleWithTallPreviewAndLongDescriptionAtSupportedSizes() throws {
        _ = NSApplication.shared
        for size in [NSSize(width: 780, height: 580), NSSize(width: 1040, height: 730), NSSize(width: 1400, height: 900)] {
            let fixture = try Fixture(); defer { fixture.cleanup() }
            let item = product(long: true), free = product("free-pet", free: true)
            fixture.controller.showLayoutPreview(catalog: PetMarketCatalog(products: [item, free], payment: payment()),
                thumbnails: [item.id: try thumbnail(tall: true), free.id: try thumbnail()])
            let window = layout(fixture, size: size); defer { window.close() }
            try assertActionVisible(fixture.controller, expected: "购买 ¥6.88")
            let scroll: NSScrollView = try find("shop-detail-scroll", in: fixture.controller.view)
            XCTAssertGreaterThan(try XCTUnwrap(scroll.documentView).bounds.height, scroll.contentView.bounds.height)
            try capture(fixture.controller, name: "shop-paid-\(Int(size.width))x\(Int(size.height))")
        }
    }

    @MainActor
    func testFreeOwnedInstallingAndEnableStatesHaveVisibleActions() throws {
        _ = NSApplication.shared
        let item = product("free-pet", free: true)
        let installation = PetMarketInstallation(productId: item.id, title: item.title, version: "1.0.0", directory: "/isolated-fixture",
            files: ["pet.gif"], manifestSha256: String(repeating: "0", count: 64), installedAt: Date(), useGrant: nil)
        for state in ["free", "owned", "installing", "installed", "enabled", "error"] {
            let fixture = try Fixture(pets: ["installed", "enabled"].contains(state) ? [installation] : [])
            defer { fixture.cleanup() }
            fixture.state.enabled = state == "enabled"
            var current = item; current.owned = state == "owned"
            fixture.controller.showLayoutPreview(catalog: PetMarketCatalog(products: [current], payment: payment()),
                thumbnails: [item.id: try thumbnail()], installingProductIds: state == "installing" ? [item.id] : [],
                errors: state == "error" ? [item.id: "安装未完成：网络中断，请重试。"] : [:])
            let window = layout(fixture, size: NSSize(width: 780, height: 580)); defer { window.close() }
            let expected = state == "owned" ? "下载安装" : state == "installing" ? "正在安装…" : state == "installed" ? "启用桌宠" : state == "enabled" ? "停用" : "免费领取并安装"
            try assertActionVisible(fixture.controller, expected: expected)
            let button: NSButton = try find("shop-primary-action", in: fixture.controller.view)
            XCTAssertEqual(button.isEnabled, state != "installing")
            try capture(fixture.controller, name: "shop-state-\(state)-780x580")
        }
    }

    @MainActor
    func testUnactivatedVisitorsSeePreviewsAndActionRequestsActivation() throws {
        _ = NSApplication.shared
        for free in [false, true] {
            let fixture = try Fixture(activated: false); defer { fixture.cleanup() }
            let item = product(free: free)
            fixture.controller.showLayoutPreview(catalog: PetMarketCatalog(products: [item], payment: payment()), thumbnails: [item.id: try thumbnail()])
            let window = layout(fixture, size: NSSize(width: 780, height: 580)); defer { window.close() }
            try assertActionVisible(fixture.controller, expected: free ? "免费领取并安装" : "购买 ¥6.88")
            let button: NSButton = try find("shop-primary-action", in: fixture.controller.view)
            button.performClick(nil)
            XCTAssertEqual(fixture.state.activationRequests, 1)
            try capture(fixture.controller, name: "shop-visitor-\(free ? "free" : "paid")-780x580")
        }
    }

    @MainActor
    func testPendingPaymentAndResourceCardsCanScrollWithoutHidingActions() throws {
        _ = NSApplication.shared
        let fixture = try Fixture(); defer { fixture.cleanup() }
        let item = product()
        let order = PetMarketOrder(id: "layout-order", productId: item.id, productTitle: item.title, productVersion: "1.0.0",
            status: "pending_payment", amountFen: 688, currency: "CNY", paymentReference: "123AB", payment: payment(),
            createdAt: Date(), expiresAt: Date().addingTimeInterval(600), paidAt: nil, canceledAt: nil)
        fixture.controller.showLayoutPreview(catalog: PetMarketCatalog(products: [item], payment: payment()), orders: [order], thumbnails: [item.id: try thumbnail()])
        let window = layout(fixture, size: NSSize(width: 780, height: 580)); defer { window.close() }
        try assertActionVisible(fixture.controller, expected: "等待付款确认")
        try capture(fixture.controller, name: "shop-pending-payment-780x580")

        let resourceFixture = try Fixture(resourceStates: ["words-installed": "installed-disabled", "scripts-enabled": "enabled"])
        defer { resourceFixture.cleanup() }
        let packs = ["words-free", "words-installed", "scripts-enabled"].enumerated().map { index, id in
            ResourcePackProduct(id: id, category: index == 2 ? "theater-scripts" : "interaction-words", title: "\(index == 2 ? "小剧场" : "互动词库") · 下班小日常",
                description: String(repeating: "给日常换一点轻松的对话。", count: 20), version: "1.0.0", size: 1024,
                sha256: String(repeating: "0", count: 64), fileCount: 10)
        }
        resourceFixture.controller.showLayoutPreview(catalog: PetMarketCatalog(products: [], payment: nil), resourcePacks: packs, showResources: true)
        let resourcesWindow = layout(resourceFixture, size: NSSize(width: 1040, height: 730)); defer { resourcesWindow.close() }
        let resourceScroll: NSScrollView = try find("shop-resource-scroll", in: resourceFixture.controller.view)
        XCTAssertFalse(resourceScroll.isHiddenOrHasHiddenAncestor)
        let free: NSButton = try find("shop-resource-action-words-free", in: resourceFixture.controller.view)
        let installed: NSButton = try find("shop-resource-action-words-installed", in: resourceFixture.controller.view)
        let enabled: NSButton = try find("shop-resource-action-scripts-enabled", in: resourceFixture.controller.view)
        XCTAssertEqual(free.title, "免费安装"); XCTAssertEqual(installed.title, "启用"); XCTAssertEqual(enabled.title, "停用")
        for button in [free, installed, enabled] {
            XCTAssertGreaterThan(button.bounds.width, 100)
            let card = try XCTUnwrap(button.superview?.superview)
            XCTAssertTrue(card.bounds.insetBy(dx: -1, dy: -1).contains(card.convert(button.bounds, from: button)))
        }
        XCTAssertTrue(resourceScroll.documentView?.isFlipped == true)
        try capture(resourceFixture.controller, name: "shop-resources-1040x730")
    }
}
