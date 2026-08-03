import AppKit

@MainActor
enum ActivationPrompts {
    static func activate(licenses: LicenseService, required: Bool, replacingExisting: Bool = false) async -> Bool {
        while true {
            let prompt = InvitationWindowController(required: required)
            guard let code = prompt.runModal() else { return false }

            do {
                try await licenses.activate(code, replacingExisting: replacingExisting)
                let success = NSAlert()
                success.messageText = "设备已完成绑定"
                success.informativeText = licenses.summary
                success.runModal()
                return true
            } catch {
                NSAlert(error: error).runModal()
            }
        }
    }
}

@MainActor
final class SettingsWindowController: NSWindowController {
    private let petController: PetWindowController
    private let licenses: LicenseService
    private let updates: UpdateService
    private let feedbackService: FeedbackService
    private let dockVisibilityChanged: (Bool) -> Void
    private var refreshing = false
    private var editingReminderId: String?
    private var feedbackLoading = false
    private var interactionLoading = false
    private var feedbackItems: [FeedbackItem] = []

    private let sizeSlider = NSSlider(value: 220, minValue: 140, maxValue: 300, target: nil, action: nil)
    private let sizeValue = NSTextField(labelWithString: "220")
    private let opacitySlider = NSSlider(value: 100, minValue: 20, maxValue: 100, target: nil, action: nil)
    private let opacityValue = NSTextField(labelWithString: "100%")
    private let personalityPopup = NSPopUpButton()
    private let mouseCheckbox = NSButton(checkboxWithTitle: "跟随与躲避鼠标", target: nil, action: nil)
    private let movementCheckbox = NSButton(checkboxWithTitle: "自动随机走动", target: nil, action: nil)
    private let randomInteractionCheckbox = NSButton(checkboxWithTitle: "允许桌搭子随机发起互动", target: nil, action: nil)
    private let interactionModePopup = NSPopUpButton()
    private let theaterCheckbox = NSButton(checkboxWithTitle: "自动随机上演小剧场", target: nil, action: nil)
    private let theaterIntervalPopup = NSPopUpButton()
    private let alwaysOnTopCheckbox = NSButton(checkboxWithTitle: "始终置顶", target: nil, action: nil)
    private let startupCheckbox = NSButton(checkboxWithTitle: "开机自动启动", target: nil, action: nil)
    private let mirrorCheckbox = NSButton(checkboxWithTitle: "水平镜像桌宠", target: nil, action: nil)
    private let clickThroughCheckbox = NSButton(checkboxWithTitle: "鼠标穿透（Control+Shift+P）", target: nil, action: nil)
    private let dockCheckbox = NSButton(checkboxWithTitle: "在 Dock 显示应用图标", target: nil, action: nil)

    private let petsPopup = NSPopUpButton()
    private let petsDetail = NSTextField(labelWithString: "")
    private let librariesPopup = NSPopUpButton()
    private let libraryDetail = NSTextField(labelWithString: "")
    private let randomPetCheckbox = NSButton(checkboxWithTitle: "自动随机切换桌宠", target: nil, action: nil)
    private let randomIntervalPopup = NSPopUpButton()

    private let wordPacksPopup = NSPopUpButton()
    private let wordPackDetail = NSTextField(labelWithString: "")
    private let scriptsPopup = NSPopUpButton()
    private let scriptDetail = NSTextField(labelWithString: "")
    private let interactionStatusLabel = NSTextField(wrappingLabelWithString: "")
    private let syncInteractionButton = NSButton(title: "在线补充", target: nil, action: nil)
    private let downloadInteractionButton = NSButton(title: "下载离线包", target: nil, action: nil)

    private let remindersPopup = NSPopUpButton()
    private let reminderDatePicker = NSDatePicker()
    private let reminderMessage = NSTextField(string: "休息一下吧")
    private let reminderEmotionPopup = NSPopUpButton()
    private let reminderEnabledCheckbox = NSButton(checkboxWithTitle: "启用提醒", target: nil, action: nil)
    private let reminderDailyCheckbox = NSButton(checkboxWithTitle: "每天重复", target: nil, action: nil)

    private let feedbackQuotaLabel = NSTextField(labelWithString: "正在获取当前设备的反馈记录…")
    private let feedbackHistoryPopup = NSPopUpButton()
    private let feedbackDetail = NSTextField(wrappingLabelWithString: "")
    private let feedbackTypePopup = NSPopUpButton()
    private let feedbackTitle = NSTextField(string: "")
    private let feedbackContent = NSTextView()
    private let feedbackReloadButton = NSButton(title: "刷新", target: nil, action: nil)
    private let feedbackSubmitButton = NSButton(title: "提交反馈", target: nil, action: nil)

    private let autoUpdateCheckbox = NSButton(checkboxWithTitle: "自动检查更新", target: nil, action: nil)
    private let activationLabel = NSTextField(labelWithString: "")
    private let updateLabel = NSTextField(labelWithString: "")
    private let updateProgress = NSProgressIndicator()
    private let checkUpdateButton = NSButton(title: "检查更新", target: nil, action: nil)
    private let downloadUpdateButton = NSButton(title: "下载更新", target: nil, action: nil)
    private let installUpdateButton = NSButton(title: "更新并重启", target: nil, action: nil)
    private let ignoreUpdateButton = NSButton(title: "忽略该版本", target: nil, action: nil)

    private let personalityValues = ["lively", "shy", "clingy", "chaotic"]
    private let interactionModes = ["quiet", "standard", "lively"]
    private let theaterIntervals = [60, 180, 300, 600, 1800]
    private let randomIntervals = [30, 60, 300, 600, 1800]
    private let emotionValues = ["happy", "cheer", "shy", "surprised", "angry", "confused", "sad", "sleepy", "calm"]

    init(
        petController: PetWindowController,
        licenses: LicenseService,
        updates: UpdateService,
        dockVisibilityChanged: @escaping (Bool) -> Void
    ) {
        self.petController = petController
        self.licenses = licenses
        self.updates = updates
        self.feedbackService = FeedbackService(licenses: licenses)
        self.dockVisibilityChanged = dockVisibilityChanged
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 780, height: 650),
            styleMask: [.titled, .closable, .miniaturizable],
            backing: .buffered,
            defer: false
        )
        window.title = "桌搭子设置"
        window.isReleasedWhenClosed = false
        window.center()
        super.init(window: window)
        buildInterface(in: window)
        updates.statusChanged = { [weak self] _ in DispatchQueue.main.async { self?.refreshUpdateState() } }
        refresh()
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func showWindow(_ sender: Any?) {
        refresh()
        super.showWindow(sender)
        window?.makeKeyAndOrderFront(sender)
        NSApp.activate(ignoringOtherApps: true)
        Task { @MainActor [weak self] in
            await self?.loadFeedback(showAlert: false)
        }
    }

    private func buildInterface(in window: NSWindow) {
        let tabs = NSTabViewController()
        tabs.tabStyle = .toolbar
        tabs.addChild(buildBehaviorPage())
        tabs.addChild(buildPetsPage())
        tabs.addChild(buildLibrariesPage())
        tabs.addChild(buildContentPage())
        tabs.addChild(buildRemindersPage())
        tabs.addChild(buildFeedbackPage())
        tabs.addChild(buildUpdatePage())
        window.contentViewController = tabs
    }

    private func buildBehaviorPage() -> NSViewController {
        let (page, stack) = makePage("外观与行为")
        addTitle("外观与行为", to: stack)

        sizeSlider.target = self
        sizeSlider.action = #selector(appearanceChanged(_:))
        sizeSlider.isContinuous = true
        sizeSlider.widthAnchor.constraint(equalToConstant: 360).isActive = true
        stack.addArrangedSubview(labeledRow("桌宠大小", controls: [sizeSlider, sizeValue]))

        opacitySlider.target = self
        opacitySlider.action = #selector(appearanceChanged(_:))
        opacitySlider.isContinuous = true
        opacitySlider.widthAnchor.constraint(equalToConstant: 360).isActive = true
        stack.addArrangedSubview(labeledRow("透明度", controls: [opacitySlider, opacityValue]))

        personalityPopup.addItems(withTitles: ["活泼", "害羞", "黏人", "混乱"])
        personalityPopup.target = self
        personalityPopup.action = #selector(behaviorChanged(_:))
        stack.addArrangedSubview(labeledRow("行为性格", controls: [personalityPopup]))
        stack.addArrangedSubview(separator())

        randomInteractionCheckbox.target = self
        randomInteractionCheckbox.action = #selector(behaviorChanged(_:))
        interactionModePopup.addItems(withTitles: ["安静（90–240 分钟）", "标准（45–120 分钟）", "活跃（20–60 分钟）"])
        interactionModePopup.target = self
        interactionModePopup.action = #selector(behaviorChanged(_:))
        let interactNow = NSButton(title: "立即互动", target: self, action: #selector(startRandomInteraction))
        stack.addArrangedSubview(randomInteractionCheckbox)
        stack.addArrangedSubview(labeledRow("互动频率", controls: [interactionModePopup, interactNow]))
        stack.addArrangedSubview(separator())

        for control in [mouseCheckbox, movementCheckbox, theaterCheckbox, alwaysOnTopCheckbox, startupCheckbox, mirrorCheckbox, clickThroughCheckbox, dockCheckbox] {
            control.target = self
            control.action = #selector(behaviorChanged(_:))
            stack.addArrangedSubview(control)
        }
        theaterIntervalPopup.addItems(withTitles: ["每 1 分钟", "每 3 分钟", "每 5 分钟", "每 10 分钟", "每 30 分钟"])
        theaterIntervalPopup.target = self
        theaterIntervalPopup.action = #selector(behaviorChanged(_:))
        let play = NSButton(title: "立即上演", target: self, action: #selector(startTheater))
        stack.addArrangedSubview(labeledRow("小剧场间隔", controls: [theaterIntervalPopup, play]))
        return page
    }

    private func buildPetsPage() -> NSViewController {
        let (page, stack) = makePage("我的桌宠")
        addTitle("我的桌宠", to: stack)
        stack.addArrangedSubview(hint("最多可添加 3 个自己的 GIF 桌宠，文件会复制到桌搭子目录中。"))
        petsPopup.target = self
        petsPopup.action = #selector(petSelectionChanged(_:))
        stack.addArrangedSubview(labeledRow("可用桌宠", controls: [petsPopup]))
        petsDetail.textColor = .secondaryLabelColor
        stack.addArrangedSubview(petsDetail)
        let add = NSButton(title: "添加 GIF", target: self, action: #selector(addPet))
        let use = NSButton(title: "使用所选", target: self, action: #selector(useSelectedPet))
        let useDefault = NSButton(title: "使用默认", target: self, action: #selector(useDefaultPet))
        let delete = NSButton(title: "删除所选", target: self, action: #selector(deleteSelectedPet))
        stack.addArrangedSubview(buttonRow([add, use, useDefault, delete]))
        return page
    }

    private func buildLibrariesPage() -> NSViewController {
        let (page, stack) = makePage("资源库")
        addTitle("GIF 资源库", to: stack)
        stack.addArrangedSubview(hint("可绑定 3 个包含 GIF 的目录；“内置资源库”始终可用。"))
        librariesPopup.target = self
        librariesPopup.action = #selector(librarySelectionChanged(_:))
        stack.addArrangedSubview(labeledRow("可用资源库", controls: [librariesPopup]))
        libraryDetail.textColor = .secondaryLabelColor
        libraryDetail.lineBreakMode = .byTruncatingMiddle
        stack.addArrangedSubview(libraryDetail)
        let bind = NSButton(title: "绑定目录", target: self, action: #selector(addLibrary))
        let delete = NSButton(title: "删除所选", target: self, action: #selector(deleteSelectedLibrary))
        stack.addArrangedSubview(buttonRow([bind, delete]))
        stack.addArrangedSubview(separator())
        addSection("随机切换", to: stack)
        randomPetCheckbox.target = self
        randomPetCheckbox.action = #selector(randomSettingsChanged(_:))
        stack.addArrangedSubview(randomPetCheckbox)
        randomIntervalPopup.addItems(withTitles: ["每 30 秒", "每 1 分钟", "每 5 分钟", "每 10 分钟", "每 30 分钟"])
        randomIntervalPopup.target = self
        randomIntervalPopup.action = #selector(randomSettingsChanged(_:))
        let now = NSButton(title: "立即换一只", target: self, action: #selector(randomizeNow))
        stack.addArrangedSubview(labeledRow("切换间隔", controls: [randomIntervalPopup, now]))
        return page
    }

    private func buildContentPage() -> NSViewController {
        let (page, stack) = makePage("互动内容")
        addTitle("互动内容", to: stack)
        interactionStatusLabel.textColor = .secondaryLabelColor
        interactionStatusLabel.maximumNumberOfLines = 2
        interactionStatusLabel.widthAnchor.constraint(equalToConstant: 430).isActive = true
        syncInteractionButton.target = self
        syncInteractionButton.action = #selector(syncInteractionContent)
        downloadInteractionButton.target = self
        downloadInteractionButton.action = #selector(downloadInteractionPack)
        stack.addArrangedSubview(buttonRow([interactionStatusLabel, syncInteractionButton, downloadInteractionButton]))
        stack.addArrangedSubview(separator())
        addSection("互动词包", to: stack)
        wordPacksPopup.target = self
        wordPacksPopup.action = #selector(wordPackSelectionChanged(_:))
        stack.addArrangedSubview(labeledRow("可用词包", controls: [wordPacksPopup]))
        wordPackDetail.textColor = .secondaryLabelColor
        stack.addArrangedSubview(wordPackDetail)
        let importWords = NSButton(title: "导入词包", target: self, action: #selector(importWordPack))
        let deleteWords = NSButton(title: "删除所选", target: self, action: #selector(deleteWordPack))
        let wordGuide = NSButton(title: "格式与 AI 生成", target: self, action: #selector(showWordGuide))
        stack.addArrangedSubview(buttonRow([importWords, deleteWords, wordGuide]))
        stack.addArrangedSubview(separator())
        addSection("小剧场剧本", to: stack)
        scriptsPopup.target = self
        scriptsPopup.action = #selector(scriptSelectionChanged(_:))
        stack.addArrangedSubview(labeledRow("可用剧本", controls: [scriptsPopup]))
        scriptDetail.textColor = .secondaryLabelColor
        stack.addArrangedSubview(scriptDetail)
        let importScript = NSButton(title: "导入剧本", target: self, action: #selector(importScriptFile))
        let deleteScript = NSButton(title: "删除所选", target: self, action: #selector(deleteScript))
        let scriptGuide = NSButton(title: "剧本格式与 AI 生成", target: self, action: #selector(showScriptGuide))
        stack.addArrangedSubview(buttonRow([importScript, deleteScript, scriptGuide]))
        return page
    }

    private func buildRemindersPage() -> NSViewController {
        let (page, stack) = makePage("提醒")
        addTitle("提醒", to: stack)
        stack.addArrangedSubview(hint("最多可保存 20 条提醒，到点后桌宠会显示消息并播放提示音。"))
        remindersPopup.target = self
        remindersPopup.action = #selector(reminderSelectionChanged(_:))
        let newButton = NSButton(title: "新建提醒", target: self, action: #selector(newReminder))
        stack.addArrangedSubview(labeledRow("已有提醒", controls: [remindersPopup, newButton]))
        stack.addArrangedSubview(separator())
        reminderDatePicker.datePickerStyle = .textFieldAndStepper
        reminderDatePicker.datePickerElements = [.yearMonthDay, .hourMinuteSecond]
        reminderDatePicker.dateValue = Date().addingTimeInterval(600)
        stack.addArrangedSubview(labeledRow("日期与时间", controls: [reminderDatePicker]))
        reminderMessage.placeholderString = "提醒内容（最多 40 字）"
        reminderMessage.widthAnchor.constraint(equalToConstant: 360).isActive = true
        stack.addArrangedSubview(labeledRow("提醒内容", controls: [reminderMessage]))
        reminderEmotionPopup.addItems(withTitles: ["开心", "加油", "害羞", "惊讶", "生气", "疑惑", "难过", "困倦", "安静"])
        stack.addArrangedSubview(labeledRow("情绪", controls: [reminderEmotionPopup]))
        reminderEnabledCheckbox.state = .on
        stack.addArrangedSubview(buttonRow([reminderEnabledCheckbox, reminderDailyCheckbox]))
        let save = NSButton(title: "保存提醒", target: self, action: #selector(saveReminder))
        let delete = NSButton(title: "删除提醒", target: self, action: #selector(deleteReminder))
        stack.addArrangedSubview(buttonRow([save, delete]))
        return page
    }

    private func buildFeedbackPage() -> NSViewController {
        let (page, stack) = makePage("反馈与建议")
        addTitle("问题反馈与建议", to: stack)

        feedbackQuotaLabel.textColor = .secondaryLabelColor
        feedbackQuotaLabel.maximumNumberOfLines = 2
        feedbackQuotaLabel.widthAnchor.constraint(equalToConstant: 560).isActive = true
        feedbackReloadButton.target = self
        feedbackReloadButton.action = #selector(reloadFeedback)
        stack.addArrangedSubview(buttonRow([feedbackQuotaLabel, feedbackReloadButton]))

        feedbackHistoryPopup.target = self
        feedbackHistoryPopup.action = #selector(feedbackSelectionChanged(_:))
        feedbackHistoryPopup.widthAnchor.constraint(equalToConstant: 500).isActive = true
        stack.addArrangedSubview(labeledRow("反馈记录", controls: [feedbackHistoryPopup]))
        feedbackDetail.textColor = .secondaryLabelColor
        feedbackDetail.maximumNumberOfLines = 7
        feedbackDetail.widthAnchor.constraint(equalToConstant: 650).isActive = true
        stack.addArrangedSubview(feedbackDetail)

        stack.addArrangedSubview(separator())
        addSection("提交新反馈", to: stack)
        feedbackTypePopup.addItems(withTitles: ["问题反馈", "功能建议"])
        stack.addArrangedSubview(labeledRow("类型", controls: [feedbackTypePopup]))
        feedbackTitle.placeholderString = "简要描述问题或建议"
        feedbackTitle.widthAnchor.constraint(equalToConstant: 500).isActive = true
        stack.addArrangedSubview(labeledRow("标题", controls: [feedbackTitle]))

        feedbackContent.isRichText = false
        feedbackContent.font = .systemFont(ofSize: 13)
        feedbackContent.frame = NSRect(x: 0, y: 0, width: 500, height: 110)
        feedbackContent.autoresizingMask = [.width, .height]
        let contentScroll = NSScrollView()
        contentScroll.borderType = .bezelBorder
        contentScroll.hasVerticalScroller = true
        contentScroll.documentView = feedbackContent
        contentScroll.widthAnchor.constraint(equalToConstant: 500).isActive = true
        contentScroll.heightAnchor.constraint(equalToConstant: 110).isActive = true
        let contentRow = labeledRow("详细说明", controls: [contentScroll])
        contentRow.alignment = .top
        stack.addArrangedSubview(contentRow)
        stack.addArrangedSubview(hint("待处理和进行中的反馈最多同时保留 3 条。"))
        feedbackSubmitButton.target = self
        feedbackSubmitButton.action = #selector(submitFeedback)
        stack.addArrangedSubview(feedbackSubmitButton)
        return page
    }

    private func buildUpdatePage() -> NSViewController {
        let (page, stack) = makePage("更新")
        addTitle("软件更新", to: stack)
        autoUpdateCheckbox.target = self
        autoUpdateCheckbox.action = #selector(autoUpdateChanged(_:))
        stack.addArrangedSubview(autoUpdateCheckbox)
        stack.addArrangedSubview(separator())
        addSection("使用授权", to: stack)
        activationLabel.textColor = .secondaryLabelColor
        stack.addArrangedSubview(activationLabel)
        stack.addArrangedSubview(NSButton(title: "更换邀请码", target: self, action: #selector(activateDevice)))
        stack.addArrangedSubview(separator())
        addSection("当前状态", to: stack)
        updateLabel.textColor = .secondaryLabelColor
        updateLabel.maximumNumberOfLines = 2
        stack.addArrangedSubview(updateLabel)
        updateProgress.minValue = 0
        updateProgress.maxValue = 100
        updateProgress.isIndeterminate = false
        updateProgress.widthAnchor.constraint(equalToConstant: 480).isActive = true
        stack.addArrangedSubview(updateProgress)
        checkUpdateButton.target = self
        checkUpdateButton.action = #selector(checkForUpdates)
        downloadUpdateButton.target = self
        downloadUpdateButton.action = #selector(downloadUpdate)
        installUpdateButton.target = self
        installUpdateButton.action = #selector(installUpdate)
        ignoreUpdateButton.target = self
        ignoreUpdateButton.action = #selector(ignoreUpdate)
        stack.addArrangedSubview(buttonRow([checkUpdateButton, downloadUpdateButton, installUpdateButton, ignoreUpdateButton]))
        return page
    }

    private func makePage(_ title: String) -> (NSViewController, NSStackView) {
        let controller = NSViewController()
        controller.title = title
        let view = NSView(frame: NSRect(x: 0, y: 0, width: 760, height: 590))
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 11
        stack.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 34),
            stack.trailingAnchor.constraint(lessThanOrEqualTo: view.trailingAnchor, constant: -34),
            stack.topAnchor.constraint(equalTo: view.topAnchor, constant: 26)
        ])
        controller.view = view
        return (controller, stack)
    }

    private func addTitle(_ text: String, to stack: NSStackView) {
        let label = NSTextField(labelWithString: text)
        label.font = .systemFont(ofSize: 22, weight: .bold)
        stack.addArrangedSubview(label)
    }

    private func addSection(_ text: String, to stack: NSStackView) {
        let label = NSTextField(labelWithString: text)
        label.font = .systemFont(ofSize: 15, weight: .semibold)
        stack.addArrangedSubview(label)
    }

    private func hint(_ text: String) -> NSTextField {
        let label = NSTextField(wrappingLabelWithString: text)
        label.textColor = .secondaryLabelColor
        label.maximumNumberOfLines = 2
        label.widthAnchor.constraint(equalToConstant: 650).isActive = true
        return label
    }

    private func labeledRow(_ title: String, controls: [NSView]) -> NSStackView {
        let label = NSTextField(labelWithString: title)
        label.font = .systemFont(ofSize: 13, weight: .medium)
        label.widthAnchor.constraint(equalToConstant: 125).isActive = true
        return buttonRow([label] + controls)
    }

    private func buttonRow(_ controls: [NSView]) -> NSStackView {
        let row = NSStackView(views: controls)
        row.orientation = .horizontal
        row.alignment = .centerY
        row.spacing = 9
        return row
    }

    private func separator() -> NSBox {
        let box = NSBox()
        box.boxType = .separator
        box.widthAnchor.constraint(equalToConstant: 700).isActive = true
        return box
    }

    private func refresh() {
        refreshing = true
        defer { refreshing = false }
        let settings = petController.currentSettings
        sizeSlider.integerValue = settings.size
        sizeValue.stringValue = "\(settings.size) px"
        opacitySlider.integerValue = settings.opacity
        opacityValue.stringValue = "\(settings.opacity)%"
        personalityPopup.selectItem(at: personalityValues.firstIndex(of: settings.personality) ?? 0)
        mouseCheckbox.state = settings.mouseInteractionEnabled ? .on : .off
        movementCheckbox.state = settings.randomMovementEnabled ? .on : .off
        randomInteractionCheckbox.state = settings.randomInteractionsEnabled ? .on : .off
        interactionModePopup.selectItem(at: interactionModes.firstIndex(of: settings.interactionMode) ?? 1)
        theaterCheckbox.state = settings.theaterEnabled ? .on : .off
        theaterIntervalPopup.selectItem(at: theaterIntervals.firstIndex(of: settings.theaterIntervalSeconds) ?? 2)
        alwaysOnTopCheckbox.state = settings.alwaysOnTop ? .on : .off
        startupCheckbox.state = LoginItemService.isEnabled ? .on : .off
        mirrorCheckbox.state = settings.mirrored ? .on : .off
        clickThroughCheckbox.state = settings.clickThrough ? .on : .off
        dockCheckbox.state = settings.dockIconVisible ? .on : .off
        randomPetCheckbox.state = settings.randomPetEnabled ? .on : .off
        randomIntervalPopup.selectItem(at: randomIntervals.firstIndex(of: settings.randomPetIntervalSeconds) ?? 2)
        autoUpdateCheckbox.state = settings.autoCheckUpdates ? .on : .off
        refreshPets(settings)
        refreshLibraries(settings)
        refreshContent(settings)
        refreshReminders(settings)
        refreshUpdateState()
    }

    private func refreshPets(_ settings: AppSettings) {
        let selected = petsPopup.selectedItem?.representedObject as? String
        petsPopup.removeAllItems()
        for pet in settings.pets {
            petsPopup.addItem(withTitle: pet.name)
            petsPopup.lastItem?.representedObject = pet.id
        }
        select(popup: petsPopup, id: selected ?? settings.activePetId)
        petsDetail.stringValue = settings.pets.isEmpty ? "尚未添加自定义桌宠" : "已添加 \(settings.pets.count)/3；当前使用：\(settings.pets.first(where: { $0.id == settings.activePetId })?.name ?? "资源库桌宠")"
    }

    private func refreshLibraries(_ settings: AppSettings) {
        librariesPopup.removeAllItems()
        librariesPopup.addItem(withTitle: "内置资源库")
        for library in settings.libraries {
            librariesPopup.addItem(withTitle: library.name)
            librariesPopup.lastItem?.representedObject = library.id
        }
        if let active = settings.activeLibraryId { select(popup: librariesPopup, id: active) } else { librariesPopup.selectItem(at: 0) }
        if let library = settings.libraries.first(where: { $0.id == settings.activeLibraryId }) {
            libraryDetail.stringValue = library.path
        } else {
            libraryDetail.stringValue = "正在使用随应用提供的 GIF 资源库"
        }
    }

    private func refreshContent(_ settings: AppSettings) {
        interactionStatusLabel.stringValue = petController.interactionStatus
        syncInteractionButton.isEnabled = !interactionLoading
        downloadInteractionButton.isEnabled = !interactionLoading
        wordPacksPopup.removeAllItems()
        wordPacksPopup.addItem(withTitle: "内置互动词包")
        for pack in settings.interactionWordPacks {
            wordPacksPopup.addItem(withTitle: pack.name)
            wordPacksPopup.lastItem?.representedObject = pack.id
        }
        if let active = settings.activeInteractionWordPackId { select(popup: wordPacksPopup, id: active) } else { wordPacksPopup.selectItem(at: 0) }
        if let pack = settings.interactionWordPacks.first(where: { $0.id == settings.activeInteractionWordPackId }) {
            wordPackDetail.stringValue = "\(pack.wordCount) 句互动台词"
        } else {
            wordPackDetail.stringValue = "正在使用内置互动台词"
        }
        let selectedScript = scriptsPopup.selectedItem?.representedObject as? String
        scriptsPopup.removeAllItems()
        scriptsPopup.addItem(withTitle: "内置小剧场（3 套）")
        for script in settings.theaterScripts {
            scriptsPopup.addItem(withTitle: script.name)
            scriptsPopup.lastItem?.representedObject = script.id
        }
        select(popup: scriptsPopup, id: selectedScript)
        if let id = scriptsPopup.selectedItem?.representedObject as? String,
           let script = settings.theaterScripts.first(where: { $0.id == id }) {
            scriptDetail.stringValue = "\(script.scenes.count) 轮对话"
        } else {
            scriptDetail.stringValue = "内置长对话剧本会参与随机上演"
        }
    }

    private func refreshReminders(_ settings: AppSettings) {
        let selected = editingReminderId
        remindersPopup.removeAllItems()
        remindersPopup.addItem(withTitle: "选择提醒…")
        let formatter = DateFormatter()
        formatter.dateFormat = "MM/dd HH:mm"
        for reminder in settings.reminders.sorted(by: { $0.at < $1.at }) {
            remindersPopup.addItem(withTitle: "\(reminder.enabled ? "●" : "○") \(formatter.string(from: reminder.at))  \(reminder.message)")
            remindersPopup.lastItem?.representedObject = reminder.id
        }
        if let selected { select(popup: remindersPopup, id: selected) }
    }

    private func refreshUpdateState() {
        activationLabel.stringValue = licenses.summary
        let status = updates.status
        updateLabel.stringValue = "当前版本 v\(AppVersion.current) - \(status.message)"
        updateProgress.doubleValue = Double(status.progress)
        let ignored = updates.availableManifest.map { $0.version == petController.currentSettings.ignoredUpdateVersion } ?? false
        checkUpdateButton.isEnabled = status.phase != .checking && status.phase != .downloading
        downloadUpdateButton.isHidden = status.phase != .available || ignored
        installUpdateButton.isHidden = status.phase != .downloaded
        ignoreUpdateButton.isHidden = ![.available, .downloaded].contains(status.phase)
        if ignored, let manifest = updates.availableManifest {
            updateLabel.stringValue = "当前版本 v\(AppVersion.current) - 已忽略 v\(manifest.version)"
        }
    }

    @MainActor
    private func loadFeedback(showAlert: Bool) async {
        guard !feedbackLoading else { return }
        feedbackLoading = true
        feedbackReloadButton.isEnabled = false
        feedbackSubmitButton.isEnabled = false
        defer {
            feedbackLoading = false
            feedbackReloadButton.isEnabled = true
        }
        do {
            applyFeedback(try await feedbackService.list())
        } catch {
            feedbackQuotaLabel.stringValue = "暂时无法获取反馈记录：\(error.localizedDescription)"
            setFeedbackFormEnabled(true)
            if showAlert { show(error) }
        }
    }

    @MainActor
    private func applyFeedback(_ response: FeedbackListResponse) {
        feedbackItems = response.items
        feedbackHistoryPopup.removeAllItems()
        if feedbackItems.isEmpty {
            feedbackHistoryPopup.addItem(withTitle: "暂无反馈记录")
            feedbackHistoryPopup.isEnabled = false
        } else {
            feedbackHistoryPopup.isEnabled = true
            for item in feedbackItems {
                feedbackHistoryPopup.addItem(withTitle: "\(feedbackStatus(item.status)) · \(feedbackType(item.type)) · \(item.title)")
                feedbackHistoryPopup.lastItem?.representedObject = item.id
            }
        }
        feedbackQuotaLabel.stringValue = response.quota.remaining > 0
            ? "当前设备有 \(response.quota.active)/\(response.quota.maximum) 条处理中反馈，还可提交 \(response.quota.remaining) 条"
            : "当前设备已有 \(response.quota.active) 条处理中反馈，请等待后台处理后再提交"
        setFeedbackFormEnabled(response.quota.remaining > 0)
        refreshFeedbackDetail()
    }

    private func refreshFeedbackDetail() {
        guard let id = feedbackHistoryPopup.selectedItem?.representedObject as? String,
              let item = feedbackItems.first(where: { $0.id == id }) else {
            feedbackDetail.stringValue = feedbackItems.isEmpty ? "提交后可在这里查看处理状态和后台回复。" : ""
            return
        }
        let timestamp = feedbackDate(item.updatedAt)
        var detail = "\(feedbackType(item.type)) · \(feedbackStatus(item.status)) · 更新于 \(timestamp)\n\(item.content)"
        if !item.adminNote.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            detail += "\n后台回复：\(item.adminNote)"
        }
        feedbackDetail.stringValue = detail
    }

    private func setFeedbackFormEnabled(_ enabled: Bool) {
        feedbackTypePopup.isEnabled = enabled
        feedbackTitle.isEnabled = enabled
        feedbackContent.isEditable = enabled
        feedbackSubmitButton.isEnabled = enabled
    }

    private func feedbackType(_ value: String) -> String {
        value == "suggestion" ? "功能建议" : "问题反馈"
    }

    private func feedbackStatus(_ value: String) -> String {
        switch value {
        case "in_progress": return "进行中"
        case "resolved": return "已处理"
        case "closed": return "已关闭"
        default: return "待处理"
        }
    }

    private func feedbackDate(_ value: String) -> String {
        let parser = ISO8601DateFormatter()
        parser.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let date = parser.date(from: value) ?? ISO8601DateFormatter().date(from: value)
        guard let date else { return "时间未知" }
        let formatter = DateFormatter()
        formatter.dateFormat = "MM/dd HH:mm"
        return formatter.string(from: date)
    }

    private func select(popup: NSPopUpButton, id: String?) {
        guard let id else { return }
        if let item = popup.itemArray.first(where: { ($0.representedObject as? String) == id }) { popup.select(item) }
    }

    @objc private func appearanceChanged(_ sender: NSSlider) {
        guard !refreshing else { return }
        petController.update {
            $0.size = sizeSlider.integerValue
            $0.opacity = opacitySlider.integerValue
        }
        sizeValue.stringValue = "\(sizeSlider.integerValue) px"
        opacityValue.stringValue = "\(opacitySlider.integerValue)%"
    }

    @objc private func behaviorChanged(_ sender: Any) {
        guard !refreshing else { return }
        if sender as AnyObject === startupCheckbox {
            do { try LoginItemService.setEnabled(startupCheckbox.state == .on) }
            catch { startupCheckbox.state = LoginItemService.isEnabled ? .on : .off; show(error) }
        }
        let previousDock = petController.currentSettings.dockIconVisible
        petController.update {
            $0.personality = personalityValues[personalityPopup.indexOfSelectedItem]
            $0.mouseInteractionEnabled = mouseCheckbox.state == .on
            $0.randomMovementEnabled = movementCheckbox.state == .on
            $0.randomInteractionsEnabled = randomInteractionCheckbox.state == .on
            $0.interactionMode = interactionModes[interactionModePopup.indexOfSelectedItem]
            $0.theaterEnabled = theaterCheckbox.state == .on
            $0.theaterIntervalSeconds = theaterIntervals[theaterIntervalPopup.indexOfSelectedItem]
            $0.alwaysOnTop = alwaysOnTopCheckbox.state == .on
            $0.startAtLogin = startupCheckbox.state == .on
            $0.mirrored = mirrorCheckbox.state == .on
            $0.clickThrough = clickThroughCheckbox.state == .on
            $0.dockIconVisible = dockCheckbox.state == .on
        }
        if previousDock != (dockCheckbox.state == .on) { dockVisibilityChanged(dockCheckbox.state == .on) }
    }

    @objc private func startTheater() { _ = petController.startTheater() }

    @objc private func startRandomInteraction() { petController.startRandomInteraction() }

    @objc private func syncInteractionContent() {
        guard !interactionLoading else { return }
        setInteractionLoading(true, status: "正在同步互动设置与内容…")
        Task { @MainActor [weak self] in
            guard let self else { return }
            do {
                let added = try await petController.syncInteractionContent()
                interactionLoading = false
                refresh()
                setInteractionLoading(false, status: "\(petController.interactionStatus) · 本次新增 \(added) 条")
            } catch {
                setInteractionLoading(false, status: petController.interactionStatus)
                show(error)
            }
        }
    }

    @objc private func downloadInteractionPack() {
        guard !interactionLoading else { return }
        setInteractionLoading(true, status: "正在下载并验证离线内容包…")
        Task { @MainActor [weak self] in
            guard let self else { return }
            do {
                let count = try await petController.downloadInteractionPack()
                interactionLoading = false
                refresh()
                setInteractionLoading(false, status: "\(petController.interactionStatus) · 离线包共 \(count) 条")
            } catch {
                setInteractionLoading(false, status: petController.interactionStatus)
                show(error)
            }
        }
    }

    private func setInteractionLoading(_ loading: Bool, status: String) {
        interactionLoading = loading
        interactionStatusLabel.stringValue = status
        syncInteractionButton.isEnabled = !loading
        downloadInteractionButton.isEnabled = !loading
    }

    @objc private func addPet() {
        guard petController.currentSettings.pets.count < 3 else { show(ContentImportError.limitReached("最多只能添加 3 个自定义桌宠")); return }
        let panel = NSOpenPanel()
        panel.title = "选择桌宠 GIF"
        panel.allowedFileTypes = ["gif"]
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let pet = try ContentStorage.importPet(from: url)
            petController.update { $0.pets.append(pet); $0.activePetId = pet.id; $0.activeLibraryId = nil }
            refresh()
        } catch { show(error) }
    }

    @objc private func petSelectionChanged(_ sender: NSPopUpButton) {
        if let id = sender.selectedItem?.representedObject as? String,
           let pet = petController.currentSettings.pets.first(where: { $0.id == id }) {
            petsDetail.stringValue = pet.path
        }
    }

    @objc private func useSelectedPet() {
        guard let id = petsPopup.selectedItem?.representedObject as? String else { return }
        petController.update { $0.activePetId = id; $0.activeLibraryId = nil }
        refresh()
    }

    @objc private func useDefaultPet() {
        petController.update { $0.activePetId = nil; $0.activeLibraryId = nil }
        refresh()
    }

    @objc private func deleteSelectedPet() {
        guard let id = petsPopup.selectedItem?.representedObject as? String,
              let pet = petController.currentSettings.pets.first(where: { $0.id == id }), confirmDelete(pet.name) else { return }
        ContentStorage.deleteImportedPet(pet)
        petController.update { $0.pets.removeAll { $0.id == id }; if $0.activePetId == id { $0.activePetId = nil } }
        refresh()
    }

    @objc private func addLibrary() {
        guard petController.currentSettings.libraries.count < 3 else { show(ContentImportError.limitReached("最多只能绑定 3 个 GIF 资源库")); return }
        let panel = NSOpenPanel()
        panel.title = "选择包含 GIF 的资源库目录"
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return }
        guard gifCount(in: url) > 0 else { show(ContentImportError.unsupportedGIF); return }
        if petController.currentSettings.libraries.contains(where: { $0.path.standardizedPath == url.path.standardizedPath }) { return }
        let library = LibraryDefinition(name: url.lastPathComponent.cleaned(limit: 40), path: url.path)
        petController.update { $0.libraries.append(library); $0.activeLibraryId = library.id; $0.activePetId = nil }
        refresh()
    }

    @objc private func librarySelectionChanged(_ sender: NSPopUpButton) {
        guard !refreshing else { return }
        let id = sender.selectedItem?.representedObject as? String
        petController.update { $0.activeLibraryId = id; $0.activePetId = nil }
        refresh()
    }

    @objc private func deleteSelectedLibrary() {
        guard let id = librariesPopup.selectedItem?.representedObject as? String,
              let library = petController.currentSettings.libraries.first(where: { $0.id == id }), confirmDelete(library.name) else { return }
        petController.update { $0.libraries.removeAll { $0.id == id }; if $0.activeLibraryId == id { $0.activeLibraryId = nil } }
        refresh()
    }

    @objc private func randomSettingsChanged(_ sender: Any) {
        guard !refreshing else { return }
        petController.update {
            $0.randomPetEnabled = randomPetCheckbox.state == .on
            $0.randomPetIntervalSeconds = randomIntervals[randomIntervalPopup.indexOfSelectedItem]
        }
    }

    @objc private func randomizeNow() { _ = petController.randomizePet() }

    @objc private func importWordPack() {
        guard petController.currentSettings.interactionWordPacks.count < 5 else { show(ContentImportError.limitReached("最多只能导入 5 个互动词包")); return }
        let panel = NSOpenPanel()
        panel.title = "导入互动词包"
        panel.allowedFileTypes = ["json", "txt"]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let pack = try InteractionWordPackImporter.load(from: url)
            petController.update { $0.interactionWordPacks.append(pack); $0.activeInteractionWordPackId = pack.id }
            refresh()
        } catch { show(error) }
    }

    @objc private func wordPackSelectionChanged(_ sender: NSPopUpButton) {
        guard !refreshing else { return }
        let id = sender.selectedItem?.representedObject as? String
        petController.update { $0.activeInteractionWordPackId = id }
        refresh()
    }

    @objc private func deleteWordPack() {
        guard let id = wordPacksPopup.selectedItem?.representedObject as? String,
              let pack = petController.currentSettings.interactionWordPacks.first(where: { $0.id == id }), confirmDelete(pack.name) else { return }
        petController.update { $0.interactionWordPacks.removeAll { $0.id == id }; if $0.activeInteractionWordPackId == id { $0.activeInteractionWordPackId = nil } }
        refresh()
    }

    @objc private func showWordGuide() { showGuide(title: "互动词包格式", text: InteractionWordPackImporter.guide) }

    @objc private func importScriptFile() {
        guard petController.currentSettings.theaterScripts.count < 10 else { show(ContentImportError.limitReached("最多只能导入 10 个小剧场剧本")); return }
        let panel = NSOpenPanel()
        panel.title = "导入小剧场剧本"
        panel.allowedFileTypes = ["json"]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let script = try TheaterScriptImporter.load(from: url)
            petController.update { $0.theaterScripts.append(script) }
            refresh()
        } catch { show(error) }
    }

    @objc private func scriptSelectionChanged(_ sender: NSPopUpButton) { refreshContent(petController.currentSettings) }

    @objc private func deleteScript() {
        guard let id = scriptsPopup.selectedItem?.representedObject as? String,
              let script = petController.currentSettings.theaterScripts.first(where: { $0.id == id }), confirmDelete(script.name) else { return }
        petController.update { $0.theaterScripts.removeAll { $0.id == id } }
        refresh()
    }

    @objc private func showScriptGuide() { showGuide(title: "小剧场剧本格式", text: TheaterScriptImporter.guide) }

    @objc private func reminderSelectionChanged(_ sender: NSPopUpButton) {
        guard !refreshing, let id = sender.selectedItem?.representedObject as? String,
              let reminder = petController.currentSettings.reminders.first(where: { $0.id == id }) else { return }
        editingReminderId = id
        reminderDatePicker.dateValue = reminder.at
        reminderMessage.stringValue = reminder.message
        reminderEmotionPopup.selectItem(at: emotionValues.firstIndex(of: reminder.emotion) ?? 0)
        reminderEnabledCheckbox.state = reminder.enabled ? .on : .off
        reminderDailyCheckbox.state = reminder.repeatDaily ? .on : .off
    }

    @objc private func newReminder() {
        editingReminderId = nil
        remindersPopup.selectItem(at: 0)
        reminderDatePicker.dateValue = Date().addingTimeInterval(600)
        reminderMessage.stringValue = "休息一下吧"
        reminderEmotionPopup.selectItem(at: 0)
        reminderEnabledCheckbox.state = .on
        reminderDailyCheckbox.state = .off
    }

    @objc private func saveReminder() {
        let message = reminderMessage.stringValue.cleaned(limit: 40)
        guard !message.isEmpty else { show(ContentImportError.limitReached("请输入提醒内容")); return }
        if editingReminderId == nil, petController.currentSettings.reminders.count >= 20 { show(ContentImportError.limitReached("最多只能保存 20 条提醒")); return }
        let reminder = ReminderDefinition(
            id: editingReminderId ?? "reminder-\(UUID().uuidString)",
            enabled: reminderEnabledCheckbox.state == .on,
            at: reminderDatePicker.dateValue,
            message: message,
            emotion: emotionValues[reminderEmotionPopup.indexOfSelectedItem],
            repeatDaily: reminderDailyCheckbox.state == .on
        )
        petController.update {
            if let index = $0.reminders.firstIndex(where: { $0.id == reminder.id }) { $0.reminders[index] = reminder }
            else { $0.reminders.append(reminder) }
        }
        editingReminderId = reminder.id
        refresh()
    }

    @objc private func deleteReminder() {
        guard let id = editingReminderId,
              let reminder = petController.currentSettings.reminders.first(where: { $0.id == id }), confirmDelete(reminder.message) else { return }
        petController.update { $0.reminders.removeAll { $0.id == id } }
        newReminder()
        refresh()
    }

    @objc private func feedbackSelectionChanged(_ sender: NSPopUpButton) {
        refreshFeedbackDetail()
    }

    @objc private func reloadFeedback() {
        Task { @MainActor [weak self] in
            await self?.loadFeedback(showAlert: true)
        }
    }

    @objc private func submitFeedback() {
        let title = feedbackTitle.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        let content = feedbackContent.string.trimmingCharacters(in: .whitespacesAndNewlines)
        guard title.count >= 2 else {
            show(FeedbackError.server("反馈标题至少需要 2 个字符"))
            return
        }
        guard content.count >= 5 else {
            show(FeedbackError.server("请补充至少 5 个字符的详细说明"))
            return
        }
        let type = feedbackTypePopup.indexOfSelectedItem == 1 ? "suggestion" : "problem"
        Task { @MainActor [weak self] in
            guard let self, !feedbackLoading else { return }
            feedbackLoading = true
            feedbackReloadButton.isEnabled = false
            setFeedbackFormEnabled(false)
            defer {
                feedbackLoading = false
                feedbackReloadButton.isEnabled = true
            }
            do {
                try await feedbackService.submit(
                    type: type,
                    title: String(title.prefix(80)),
                    content: String(content.prefix(2_000))
                )
                feedbackTitle.stringValue = ""
                feedbackContent.string = ""
                applyFeedback(try await feedbackService.list())
            } catch {
                setFeedbackFormEnabled(true)
                show(error)
            }
        }
    }

    @objc private func autoUpdateChanged(_ sender: NSButton) {
        petController.update { $0.autoCheckUpdates = sender.state == .on }
    }

    @objc private func activateDevice() {
        Task { @MainActor [weak self] in
            guard let self else { return }
            if licenses.isActivated {
                let confirm = NSAlert()
                confirm.messageText = "更换邀请码"
                confirm.informativeText = "输入新的邀请码后，当前设备会更新绑定记录。"
                confirm.addButton(withTitle: "继续")
                confirm.addButton(withTitle: "取消")
                guard confirm.runModal() == .alertFirstButtonReturn else { return }
            }
            if await ActivationPrompts.activate(licenses: licenses, required: false, replacingExisting: licenses.isActivated) {
                petController.startInteractionServices()
            }
            refresh()
        }
    }

    @objc private func checkForUpdates() {
        petController.update { $0.ignoredUpdateVersion = nil }
        Task { @MainActor [weak self] in
            guard let self else { return }
            do {
                if try await updates.check() == nil {
                    let alert = NSAlert(); alert.messageText = "已经是最新版本"; alert.runModal()
                }
            } catch { show(error) }
            refreshUpdateState()
        }
    }

    @objc private func downloadUpdate() {
        guard let manifest = updates.availableManifest else { return }
        Task { @MainActor [weak self] in
            do { try await self?.updates.download(manifest) }
            catch { self?.show(error) }
        }
    }

    @objc private func installUpdate() {
        do { try updates.installDownloaded() }
        catch { show(error) }
    }

    @objc private func ignoreUpdate() {
        guard let version = updates.availableManifest?.version else { return }
        petController.update { $0.ignoredUpdateVersion = version }
        refreshUpdateState()
    }

    @objc func checkForUpdatesFromMenu() { checkForUpdates() }

    private func gifCount(in directory: URL) -> Int {
        guard let enumerator = FileManager.default.enumerator(at: directory, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles]) else { return 0 }
        return enumerator.compactMap { $0 as? URL }.filter { $0.pathExtension.caseInsensitiveCompare("gif") == .orderedSame }.count
    }

    private func confirmDelete(_ name: String) -> Bool {
        let alert = NSAlert()
        alert.messageText = "确认删除“\(name)”？"
        alert.addButton(withTitle: "删除")
        alert.addButton(withTitle: "取消")
        return alert.runModal() == .alertFirstButtonReturn
    }

    private func show(_ error: Error) { NSAlert(error: error).runModal() }

    private func showGuide(title: String, text: String) {
        let textView = NSTextView(frame: NSRect(x: 0, y: 0, width: 560, height: 300))
        textView.string = text
        textView.isEditable = false
        textView.font = .monospacedSystemFont(ofSize: 12, weight: .regular)
        let scroll = NSScrollView(frame: textView.frame)
        scroll.documentView = textView
        scroll.hasVerticalScroller = true
        let alert = NSAlert()
        alert.messageText = title
        alert.accessoryView = scroll
        alert.addButton(withTitle: "完成")
        alert.addButton(withTitle: "复制示例")
        if alert.runModal() == .alertSecondButtonReturn {
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(text, forType: .string)
        }
    }
}
