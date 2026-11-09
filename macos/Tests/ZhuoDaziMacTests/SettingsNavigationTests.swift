import AppKit
import XCTest
@testable import ZhuoDaziMac

@MainActor
private final class SettingsReviewWindow: NSWindow {
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect { frameRect }
}

@MainActor
private final class HallServiceStub: HallServing {
    private(set) var profile: CompanionProfile? = CompanionProfile(
        displayName: "本地验收名片", pairingCode: "PREVIEW1", partner: nil, hallEnabled: false, online: true)
    private(set) var hallPeople: [CompanionHallPerson] = []
    private(set) var profileRequests = 0
    private(set) var hallRequests = 0
    private(set) var membershipChanges = 0

    func refreshProfile() async throws -> CompanionProfile { profileRequests += 1; return profile! }
    func refreshHall() async throws -> [CompanionHallPerson] { hallRequests += 1; return hallPeople }
    func updateName(_ displayName: String) async throws -> CompanionProfile { profile! }
    func setHallEnabled(_ enabled: Bool) async throws -> CompanionProfile { membershipChanges += 1; return profile! }
    func sendToHall(fileURL: URL, recipientId: String, message: String) async throws -> String { "本地验收用户" }
}

final class SettingsNavigationTests: XCTestCase {
    func testPrimaryDestinationTitlesAndOrderMatchWindows() {
        XCTAssertEqual(SettingsPage.allCases.dropLast().map(\.title), [
            "今日桌宠", "桌宠小铺", "陪伴日常", "桌宠小窝", "灵感口袋", "贴心提醒",
            "搭子", "桌宠大厅", "心愿信箱", "我们的日常", "作者分享", "版本更新"
        ])
        XCTAssertEqual(SettingsPage.allCases.last, .uninstall, "Keep the existing macOS uninstall flow reachable")
    }

    @MainActor
    private func placeholder(_ title: String) -> NSViewController {
        let controller = NSViewController()
        let root = NSView()
        let heading = NSTextField(labelWithString: title)
        heading.font = .systemFont(ofSize: 26, weight: .bold)
        let note = NSTextField(wrappingLabelWithString: "本地设置导航验收：此页正文为测试占位，不连接服务。桌宠小铺内容另有独立截图验收。")
        note.textColor = .secondaryLabelColor
        for field in [heading, note] {
            field.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(field)
        }
        NSLayoutConstraint.activate([
            heading.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 28),
            heading.topAnchor.constraint(equalTo: root.topAnchor, constant: 32),
            note.leadingAnchor.constraint(equalTo: heading.leadingAnchor),
            note.topAnchor.constraint(equalTo: heading.bottomAnchor, constant: 20),
            note.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -28)
        ])
        controller.view = root
        return controller
    }

    @MainActor
    private func capture(_ view: NSView, name: String) throws {
        guard let directory = ProcessInfo.processInfo.environment["ZHUODAZI_REVIEW_OUTPUT"], !directory.isEmpty else { return }
        let output = URL(fileURLWithPath: directory, isDirectory: true)
        try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        for _ in 0..<4 { view.layoutSubtreeIfNeeded() }
        XCTAssertGreaterThan(view.bounds.width, 0, "Screenshot host must have a laid out content width")
        XCTAssertGreaterThan(view.bounds.height, 0, "Screenshot host must have a laid out content height")
        let bitmap = try XCTUnwrap(view.bitmapImageRepForCachingDisplay(in: view.bounds))
        view.cacheDisplay(in: view.bounds, to: bitmap)
        let data = try XCTUnwrap(bitmap.representation(using: .png, properties: [:]))
        try data.write(to: output.appendingPathComponent(name))
    }

    @MainActor
    func testHallSharesSettingsWindowAndNavigationPreservesItsController() async throws {
        _ = NSApplication.shared
        let service = HallServiceStub()
        let hall = HallViewController(service: service, hasAccess: { true }, isTrial: { true },
            hallAvailable: { true }, currentGIFURL: { nil }, stateChanged: {})
        let destinations = SettingsPage.allCases.map { page in
            SettingsSidebarController.Destination(page: page, controller: page == .hall ? hall : placeholder(page.title))
        }
        let navigation = SettingsSidebarController(destinations: destinations)
        navigation.selectionChanged = { page in hall.setPagePresented(page == .hall) }
        let window = SettingsReviewWindow(contentRect: NSRect(x: 0, y: 0, width: 1100, height: 760),
            styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.title = "设置导航验收 · 大厅为本地模拟数据"
        window.contentViewController = navigation
        defer { hall.setPagePresented(false); window.close() }
        window.setContentSize(NSSize(width: 1100, height: 760))
        window.orderFront(nil)
        for _ in 0..<4 { navigation.view.layoutSubtreeIfNeeded() }
        XCTAssertEqual(navigation.view.bounds.width, 1100, accuracy: 1, "Review host must exercise the requested content width even on a smaller CI display")
        XCTAssertEqual(navigation.view.bounds.height, 760, accuracy: 1, "Review host must exercise the requested content height")
        _ = navigation.view
        XCTAssertEqual(navigation.selectedPage, .today)
        try capture(navigation.view, name: "settings-sidebar-mock.png")

        navigation.select(.hall)
        await Task.yield()
        XCTAssertEqual(navigation.selectedPage, .hall)
        XCTAssertTrue(hall.view.window === window, "Hall must be embedded in the existing settings window")
        XCTAssertTrue(hall.parent is NSTabViewController)
        XCTAssertTrue(hall.pagePresented)
        XCTAssertTrue(hall.isPolling)
        XCTAssertEqual(service.membershipChanges, 0, "Opening the page must not opt the user into the public hall")
        XCTAssertEqual(service.hallRequests, 0, "A user who has not joined must not request the public member list")
        try capture(navigation.view, name: "settings-hall-mock.png")

        navigation.select(.shop)
        XCTAssertFalse(hall.pagePresented)
        XCTAssertFalse(hall.isPolling, "Hidden hall must stop its refresh timer")
        try capture(navigation.view, name: "settings-shop-navigation-mock.png")
        navigation.select(.hall)
        await Task.yield()
        XCTAssertTrue(hall.view.window === window)
        XCTAssertTrue(hall.isPolling)
        navigation.select(.updates)
        XCTAssertEqual(navigation.selectedPage, .updates)
        XCTAssertFalse(hall.isPolling)
    }

    @MainActor
    func testHallAccessAndPresentationLifecycleDoNotChangeMembership() async throws {
        let service = HallServiceStub()
        var allowed = false
        let hall = HallViewController(service: service, hasAccess: { allowed }, isTrial: { true },
            hallAvailable: { true }, currentGIFURL: { nil }, stateChanged: {})
        hall.setPagePresented(true)
        await Task.yield()
        XCTAssertEqual(service.profileRequests, 0, "Expired access must not issue a hall request")
        allowed = true
        hall.refreshAccessState()
        await Task.yield()
        XCTAssertEqual(service.profileRequests, 1, "Regaining trial/activation access refreshes the embedded page")
        hall.setPagePresented(false)
        XCTAssertFalse(hall.isPolling)
        hall.setPagePresented(true)
        await Task.yield()
        XCTAssertEqual(service.profileRequests, 2)
        hall.setPagePresented(true)
        await Task.yield()
        XCTAssertEqual(service.profileRequests, 2, "Repeated show notifications must not duplicate polling")
        hall.viewDidDisappear()
        XCTAssertFalse(hall.pagePresented)
        XCTAssertFalse(hall.isPolling)
        XCTAssertEqual(service.membershipChanges, 0)
    }
}
