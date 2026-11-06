import AppKit

/// Keep the main destinations in the same order as the Windows settings sidebar.
enum SettingsPage: String, CaseIterable {
    case today, shop, companionship, pets, inspiration, reminders, companion, hall, feedback, daily, updates, uninstall

    var title: String {
        switch self {
        case .today: return "今日桌宠"
        case .shop: return "桌宠小铺"
        case .companionship: return "陪伴日常"
        case .pets: return "桌宠小窝"
        case .inspiration: return "灵感口袋"
        case .reminders: return "贴心提醒"
        case .companion: return "搭子"
        case .hall: return "桌宠大厅"
        case .feedback: return "心愿信箱"
        case .daily: return "我们的日常"
        case .updates: return "版本更新"
        case .uninstall: return "卸载与反馈"
        }
    }
}

@MainActor
final class SettingsSidebarController: NSViewController, NSTableViewDataSource, NSTableViewDelegate {
    struct Destination {
        let page: SettingsPage
        let controller: NSViewController
    }

    private let destinations: [Destination]
    private let table = NSTableView()
    private let pages = NSTabViewController()
    private(set) var selectedPage: SettingsPage
    var selectionChanged: ((SettingsPage) -> Void)?

    init(destinations: [Destination]) {
        precondition(!destinations.isEmpty)
        precondition(Set(destinations.map(\.page)).count == destinations.count)
        self.destinations = destinations
        self.selectedPage = destinations[0].page
        super.init(nibName: nil, bundle: nil)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func loadView() {
        let root = NSView()
        root.wantsLayer = true
        root.layer?.backgroundColor = NSColor.windowBackgroundColor.cgColor
        view = root

        let sidebar = NSScrollView()
        sidebar.hasVerticalScroller = true
        sidebar.autohidesScrollers = true
        sidebar.borderType = .noBorder
        sidebar.drawsBackground = false
        sidebar.translatesAutoresizingMaskIntoConstraints = false
        table.headerView = nil
        table.rowHeight = 42
        table.intercellSpacing = NSSize(width: 0, height: 2)
        table.style = .sourceList
        table.selectionHighlightStyle = .sourceList
        table.allowsMultipleSelection = false
        table.allowsEmptySelection = false
        table.backgroundColor = NSColor.controlBackgroundColor
        table.frame = NSRect(x: 0, y: 0, width: 186, height: 0)
        table.autoresizingMask = [.width]
        table.setAccessibilityLabel("设置菜单")
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("settings-page"))
        column.resizingMask = .autoresizingMask
        table.addTableColumn(column)
        table.columnAutoresizingStyle = .uniformColumnAutoresizingStyle
        table.dataSource = self
        table.delegate = self
        sidebar.documentView = table
        root.addSubview(sidebar)

        // NSTabViewController owns child containment and view appearance callbacks,
        // while the table provides the only visible navigation control.
        pages.tabStyle = .unspecified
        for destination in destinations {
            destination.controller.title = destination.page.title
            pages.addTabViewItem(NSTabViewItem(viewController: destination.controller))
        }
        addChild(pages)
        let content = pages.view
        pages.tabView.tabViewType = .noTabsNoBorder
        content.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(content)
        NSLayoutConstraint.activate([
            sidebar.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            sidebar.topAnchor.constraint(equalTo: root.topAnchor),
            sidebar.bottomAnchor.constraint(equalTo: root.bottomAnchor),
            sidebar.widthAnchor.constraint(equalToConstant: 186),
            content.leadingAnchor.constraint(equalTo: sidebar.trailingAnchor),
            content.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            content.topAnchor.constraint(equalTo: root.topAnchor),
            content.bottomAnchor.constraint(equalTo: root.bottomAnchor)
        ])
        table.reloadData()
        select(selectedPage)
    }

    func select(_ page: SettingsPage) {
        guard let index = destinations.firstIndex(where: { $0.page == page }) else { return }
        _ = view
        selectedPage = page
        pages.selectedTabViewItemIndex = index
        if table.selectedRow != index {
            table.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false)
        }
        table.scrollRowToVisible(index)
        selectionChanged?(page)
    }

    func numberOfRows(in tableView: NSTableView) -> Int { destinations.count }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let cell = NSTableCellView()
        let text = NSTextField(labelWithString: destinations[row].page.title)
        text.font = .systemFont(ofSize: 13, weight: .medium)
        text.lineBreakMode = .byTruncatingTail
        text.translatesAutoresizingMaskIntoConstraints = false
        cell.addSubview(text)
        cell.textField = text
        NSLayoutConstraint.activate([
            text.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 14),
            text.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -10),
            text.centerYAnchor.constraint(equalTo: cell.centerYAnchor)
        ])
        return cell
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        guard destinations.indices.contains(table.selectedRow) else { return }
        let page = destinations[table.selectedRow].page
        guard selectedPage != page else { return }
        select(page)
    }
}
