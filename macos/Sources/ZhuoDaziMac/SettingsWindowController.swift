import AppKit
import ZhuoDaziCore

@MainActor
enum ActivationPrompts {
    static func activate(
        licenses: LicenseService,
        required: Bool,
        replacingExisting: Bool = false,
        statusMessage: String? = nil,
        trialEnded: Bool = false
    ) async -> Bool {
        while true {
            let prompt = InvitationWindowController(licenses: licenses, replacingExisting: replacingExisting,
                required: required, statusMessage: statusMessage, trialEnded: trialEnded)
            guard let code = prompt.runModal() else { return prompt.activatedRemotely && licenses.isActivated }

            do {
                try await licenses.activate(code, replacingExisting: replacingExisting)
                let success = NSAlert()
                success.messageText = "设备已完成绑定"
                success.informativeText = licenses.activationSuccessMessage
                success.runModal()
                return true
            } catch {
                presentFriendlyError(error, title: "绑定未完成")
            }
        }
    }
}

@MainActor
final class SettingsWindowController: NSWindowController {
    private let petController: PetWindowController
    private let licenses: LicenseService
    private let updates: UpdateService
    private let remoteConfig: () -> RemoteConfig
    private let refreshDailyConfig: () async -> Bool
    private let feedbackService: FeedbackService
    private let uninstallFeedbackService: UninstallFeedbackService
    private let dockVisibilityChanged: (Bool) -> Void
    private let openCompanion: () -> Void
    private let openHall: () -> Void
    private let showUsageGuide: () -> Void
    private let openExperienceGuide: () -> Void
    private let experienceGuideButton = NSButton(title: "开始体验", target: nil, action: nil)
    private let openFakeAd: () -> Void
    private let announcementLabel = NSTextField(wrappingLabelWithString: "")
    private let fishModeButton = NSButton(title: "打开摸鱼广告", target: nil, action: nil)
    private var fishModeSection: [NSView] = []
    private var refreshing = false
    private var editingReminderId: String?
    private var feedbackLoading = false
    private var feedbackItems: [FeedbackItem] = []

    private let sizeSlider = NSSlider(value: 140, minValue: 60, maxValue: 300, target: nil, action: nil)
    private let sizeValue = NSTextField(labelWithString: "220")
    private let opacitySlider = NSSlider(value: 100, minValue: 20, maxValue: 100, target: nil, action: nil)
    private let opacityValue = NSTextField(labelWithString: "100%")
    private let personalityPopup = NSPopUpButton()
    private let mouseCheckbox = NSButton(checkboxWithTitle: "跟随与躲避鼠标", target: nil, action: nil)
    private let movementCheckbox = NSButton(checkboxWithTitle: "自动随机走动", target: nil, action: nil)
    private let randomInteractionCheckbox = NSButton(checkboxWithTitle: "让桌搭子主动找我玩", target: nil, action: nil)
    private let dailySpeechCheckbox = NSButton(checkboxWithTitle: "日常气泡（自动移动与换宠时说话）", target: nil, action: nil)
    private let quietButton = NSButton(title: "暂停打扰 1 小时", target: nil, action: nil)
    private let quietLabel = NSTextField(labelWithString: "")
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
    private let libraryEnabledCheckbox = NSButton(checkboxWithTitle: "启用所选来源", target: nil, action: nil)
    private let randomPetCheckbox = NSButton(checkboxWithTitle: "自动随机切换桌宠", target: nil, action: nil)
    private let randomIntervalPopup = NSPopUpButton()

    private let wordPacksPopup = NSPopUpButton()
    private let wordPackDetail = NSTextField(labelWithString: "")
    private let wordPackEnabledCheckbox = NSButton(checkboxWithTitle: "启用所选词库", target: nil, action: nil)
    private let scriptsPopup = NSPopUpButton()
    private let scriptDetail = NSTextField(labelWithString: "")
    private let scriptEnabledCheckbox = NSButton(checkboxWithTitle: "启用所选剧本", target: nil, action: nil)
    private let companionButton = NSButton(title: "", target: nil, action: nil)
    private let companionStatusIcon = NSImageView()
    private let companionStatusLabel = NSTextField(labelWithString: "")

    private let remindersPopup = NSPopUpButton()
    private let reminderDatePicker = NSDatePicker()
    private let reminderMessage = NSTextField(string: "休息一下吧")
    private let reminderExpressionPath = NSTextField(string: "")
    private let reminderEnabledCheckbox = NSButton(checkboxWithTitle: "启用提醒", target: nil, action: nil)
    private let reminderDailyCheckbox = NSButton(checkboxWithTitle: "每天重复", target: nil, action: nil)

    private let feedbackQuotaLabel = NSTextField(labelWithString: "正在获取当前设备的反馈记录…")
    private let feedbackHistoryPopup = NSPopUpButton()
    private let feedbackDetail = NSTextView()
    private let feedbackTypePopup = NSPopUpButton()
    private let feedbackTitle = NSTextField(string: "")
    private let feedbackContent = NSTextView()
    private let feedbackReloadButton = NSButton(title: "刷新", target: nil, action: nil)
    private let feedbackSubmitButton = NSButton(title: "提交反馈", target: nil, action: nil)

    private let uninstallReasonPopup = NSPopUpButton()
    private let uninstallComment = NSTextField(string: "")
    private let uninstallSubmitButton = NSButton(title: "提交反馈并卸载", target: nil, action: nil)
    private let uninstallSkipButton = NSButton(title: "跳过反馈并卸载", target: nil, action: nil)
    private let uninstallGuideButton = NSButton(title: "查看手动卸载说明", target: nil, action: nil)
    private let uninstallStatus = NSTextField(wrappingLabelWithString: "")

    private let autoUpdateCheckbox = NSButton(checkboxWithTitle: "自动检查更新", target: nil, action: nil)
    private let activationLabel = NSTextField(labelWithString: "")
    private let updateLabel = NSTextField(labelWithString: "")
    private let releaseNotes = NSTextView()
    private let hallButton = NSButton(title: "打开桌宠大厅", target: nil, action: nil)
    private let hallAccessLabel = NSTextField(wrappingLabelWithString: "")
    private let currentPetLabel = NSTextField(wrappingLabelWithString: "")
    private let currentPetPreview = NSImageView()
    private let updateProgress = NSProgressIndicator()
    private let checkUpdateButton = NSButton(title: "检查更新", target: nil, action: nil)
    private let downloadUpdateButton = NSButton(title: "下载更新", target: nil, action: nil)
    private let installUpdateButton = NSButton(title: "更新并重启", target: nil, action: nil)
    private let ignoreUpdateButton = NSButton(title: "忽略该版本", target: nil, action: nil)

    private let personalityValues = ["lively", "shy", "clingy", "chaotic"]
    private let interactionModes = DailyFrequency.presets
    private var dailyPage: DailyPageController?
    private let theaterIntervals = [60, 180, 300, 600, 1800]
    private let randomIntervals = [30, 60, 300, 600, 1800]
    private let petShopController: NSViewController?
    init(
        petController: PetWindowController,
        licenses: LicenseService,
        updates: UpdateService,
        remoteConfig: @escaping () -> RemoteConfig,
        refreshDailyConfig: @escaping () async -> Bool,
        dockVisibilityChanged: @escaping (Bool) -> Void,
        openCompanion: @escaping () -> Void,
        openHall: @escaping () -> Void,
        showUsageGuide: @escaping () -> Void,
        openExperienceGuide: @escaping () -> Void,
        openFakeAd: @escaping () -> Void,
        petShopController: NSViewController? = nil
    ) {
        self.petController = petController
        self.licenses = licenses
        self.updates = updates
        self.remoteConfig = remoteConfig
        self.refreshDailyConfig = refreshDailyConfig
        self.feedbackService = FeedbackService(licenses: licenses)
        self.uninstallFeedbackService = UninstallFeedbackService(licenses: licenses)
        self.dockVisibilityChanged = dockVisibilityChanged
        self.openCompanion = openCompanion
        self.openHall = openHall
        self.showUsageGuide = showUsageGuide
        self.openExperienceGuide = openExperienceGuide
        self.openFakeAd = openFakeAd
        self.petShopController = petShopController
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 900, height: 680),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "桌搭子 · 陪伴设置"
        window.isReleasedWhenClosed = false
        window.minSize = NSSize(width: 820, height: 620)
        window.toolbarStyle = .preference
        window.tabbingMode = .disallowed
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
        let daily = DailyPageController(pet: petController, config: remoteConfig, refreshConfig: refreshDailyConfig)
        dailyPage = daily
        tabs.addChild(daily)
        petController.dailyChanged = { [weak daily] in daily?.refresh() }
        tabs.addChild(buildPetsPage())
        tabs.addChild(buildLibrariesPage())
        if let petShopController {
            petShopController.title = "桌宠小铺"
            tabs.addChild(petShopController)
        }
        tabs.addChild(buildContentPage())
        tabs.addChild(buildCompanionPage())
        tabs.addChild(buildHallPage())
        tabs.addChild(buildRemindersPage())
        tabs.addChild(buildFeedbackPage())
        tabs.addChild(buildUninstallPage())
        tabs.addChild(buildUpdatePage())
        window.contentViewController = tabs
    }

    private func buildCompanionPage() -> NSViewController {
        let (page, stack) = makePage("私人搭子")
        addTitle("私人搭子", to: stack)
        companionStatusIcon.image = NSImage(systemSymbolName: "person.2.fill", accessibilityDescription: "搭子联机状态")
        companionStatusIcon.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 18, weight: .medium)
        companionStatusLabel.font = .systemFont(ofSize: 14, weight: .medium)
        stack.addArrangedSubview(buttonRow([companionStatusIcon, companionStatusLabel]))
        stack.addArrangedSubview(separator())
        addSection("搭子管理", to: stack)
        stack.addArrangedSubview(hint("绑定一位搭子后，可以发送当前 GIF；收到的 GIF 会作为独立来访展示。"))
        companionButton.target = self
        companionButton.action = #selector(openCompanionAction)
        companionButton.image = NSImage(systemSymbolName: "person.crop.circle.badge.plus", accessibilityDescription: nil)
        companionButton.imagePosition = .imageLeading
        companionButton.controlSize = .large
        stack.addArrangedSubview(companionButton)
        return page
    }

    private func buildHallPage() -> NSViewController {
        let (page, stack) = makePage("桌宠大厅")
        addTitle("桌宠大厅", to: stack)
        stack.addArrangedSubview(hint("体验期间和正式激活后都可加入大厅，向在线用户发送当前 GIF 和一句留言。"))
        stack.addArrangedSubview(hint("只有你主动加入后，昵称与在线状态才会公开。退出大厅不会解除私人搭子绑定。"))
        hallAccessLabel.maximumNumberOfLines = 0
        hallAccessLabel.textColor = .secondaryLabelColor
        stack.addArrangedSubview(hallAccessLabel)
        hallButton.target = self
        hallButton.action = #selector(openHallAction)
        hallButton.controlSize = .large
        hallButton.bezelStyle = .rounded
        stack.addArrangedSubview(hallButton)
        return page
    }

    private func buildBehaviorPage() -> NSViewController {
        let (page, stack) = makePage("陪伴日常")
        addTitle("把陪伴调成喜欢的样子", to: stack)
        stack.addArrangedSubview(hint("设置会自动保存。关闭设置后桌宠继续运行；菜单栏中的“退出桌搭子”会完全退出。"))
        experienceGuideButton.target = self
        experienceGuideButton.action = #selector(openExperienceGuideAction)
        stack.addArrangedSubview(buttonRow([experienceGuideButton, NSButton(title: "文字使用指南", target: self, action: #selector(showUsageGuideAction))]))
        announcementLabel.textColor = .systemOrange
        announcementLabel.maximumNumberOfLines = 3
        announcementLabel.widthAnchor.constraint(equalToConstant: 650).isActive = true
        announcementLabel.isHidden = true
        stack.addArrangedSubview(announcementLabel)

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
        interactionModePopup.addItems(withTitles: interactionModes.map(DailyFrequency.label))
        interactionModePopup.target = self
        interactionModePopup.action = #selector(behaviorChanged(_:))
        let interactNow = NSButton(title: "陪我玩一会", target: self, action: #selector(startRandomInteraction))
        stack.addArrangedSubview(randomInteractionCheckbox)
        stack.addArrangedSubview(labeledRow("互动频率", controls: [interactionModePopup, interactNow]))
        stack.addArrangedSubview(hint("一起聊心情、猜小问题、听笑话；关闭主动互动后，仍可点“陪我玩一会”。“安静”频率只是减少次数，不会关闭日常台词。"))
        dailySpeechCheckbox.target = self
        dailySpeechCheckbox.action = #selector(dailySpeechChanged)
        stack.addArrangedSubview(dailySpeechCheckbox)
        quietButton.target = self
        quietButton.action = #selector(toggleQuiet)
        stack.addArrangedSubview(buttonRow([quietButton, quietLabel]))
        stack.addArrangedSubview(hint("暂停期间不主动说话、发起互动、自动演剧场或展示来访。提醒与手动操作继续有效；来访会排队等待恢复。"))
        stack.addArrangedSubview(separator())

        addSection("小剧场 · 先看一场", to: stack)
        stack.addArrangedSubview(hint("两只小搭子演一段小故事，不需要邀请好友，也不必先准备剧本。看一次不会开启自动上演；想先试试，可点“开始体验”。"))
        stack.addArrangedSubview(NSButton(title: "看一场小剧场", target: self, action: #selector(startTheater)))
        theaterCheckbox.target = self
        theaterCheckbox.action = #selector(behaviorChanged(_:))
        stack.addArrangedSubview(theaterCheckbox)
        theaterIntervalPopup.addItems(withTitles: ["每 1 分钟", "每 3 分钟", "每 5 分钟", "每 10 分钟", "每 30 分钟"])
        theaterIntervalPopup.target = self
        theaterIntervalPopup.action = #selector(behaviorChanged(_:))
        stack.addArrangedSubview(labeledRow("自动上演间隔", controls: [theaterIntervalPopup]))
        stack.addArrangedSubview(NSButton(title: "停止当前剧场或来访", target: self, action: #selector(stopCurrentScene)))
        stack.addArrangedSubview(hint("想自己写对白，可到“灵感口袋”页的“小剧场 · 自编小故事”导入剧本。也可以直接欣赏内置小故事。"))
        stack.addArrangedSubview(separator())
        addSection("桌宠行动与窗口", to: stack)
        for control in [mouseCheckbox, movementCheckbox, alwaysOnTopCheckbox, startupCheckbox, mirrorCheckbox, clickThroughCheckbox, dockCheckbox] {
            control.target = self
            control.action = #selector(behaviorChanged(_:))
            stack.addArrangedSubview(control)
        }
        stack.addArrangedSubview(separator())
        let fishTitle = NSTextField(labelWithString: "摸鱼模式")
        fishTitle.font = .systemFont(ofSize: 15, weight: .semibold)
        let fishHint = hint("七天完整体验可用，激活后继续使用；首次使用需要允许辅助功能权限。")
        fishModeButton.target = self
        fishModeButton.action = #selector(openFakeAdAction)
        fishModeSection = [fishTitle, fishHint, fishModeButton]
        stack.addArrangedSubview(fishTitle)
        stack.addArrangedSubview(fishHint)
        stack.addArrangedSubview(fishModeButton)
        return page
    }

    private func buildPetsPage() -> NSViewController {
        let (page, stack) = makePage("桌宠小窝")
        addTitle("桌宠小窝", to: stack)
        currentPetPreview.imageScaling = .scaleProportionallyUpOrDown
        currentPetPreview.animates = true
        currentPetPreview.widthAnchor.constraint(equalToConstant: 100).isActive = true
        currentPetPreview.heightAnchor.constraint(equalToConstant: 100).isActive = true
        currentPetLabel.widthAnchor.constraint(equalToConstant: 440).isActive = true
        currentPetLabel.maximumNumberOfLines = 0
        stack.addArrangedSubview(buttonRow([currentPetPreview, currentPetLabel]))
        stack.addArrangedSubview(hint("把喜欢的 GIF 接回小窝，最多可添加 3 只。添加后会为你保存一份副本。"))
        petsPopup.target = self
        petsPopup.action = #selector(petSelectionChanged(_:))
        stack.addArrangedSubview(labeledRow("可用桌宠", controls: [petsPopup]))
        petsDetail.textColor = .secondaryLabelColor
        stack.addArrangedSubview(petsDetail)
        let add = NSButton(title: "接一只回家", target: self, action: #selector(addPet))
        let use = NSButton(title: "让它陪我", target: self, action: #selector(useSelectedPet))
        let useDefault = NSButton(title: "使用默认桌宠", target: self, action: #selector(useDefaultPet))
        let delete = NSButton(title: "删除所选", target: self, action: #selector(deleteSelectedPet))
        stack.addArrangedSubview(buttonRow([add, use, useDefault, delete]))
        return page
    }

    private func buildLibrariesPage() -> NSViewController {
        let (page, stack) = makePage("桌宠图鉴")
        addTitle("桌宠图鉴", to: stack)
        stack.addArrangedSubview(hint("本地 GIF 文件夹与小铺桌宠合计最多同时启用 3 个来源；未启用的图鉴会继续保留。"))
        librariesPopup.target = self
        librariesPopup.action = #selector(librarySelectionChanged(_:))
        stack.addArrangedSubview(labeledRow("选择图鉴", controls: [librariesPopup]))
        libraryDetail.textColor = .secondaryLabelColor
        stack.addArrangedSubview(libraryDetail)
        libraryEnabledCheckbox.target = self
        libraryEnabledCheckbox.action = #selector(libraryEnabledChanged(_:))
        stack.addArrangedSubview(libraryEnabledCheckbox)
        let bind = NSButton(title: "添加 GIF 文件夹", target: self, action: #selector(addLibrary))
        let delete = NSButton(title: "删除所选", target: self, action: #selector(deleteSelectedLibrary))
        stack.addArrangedSubview(buttonRow([bind, delete]))
        stack.addArrangedSubview(separator())
        addSection("小搭子轮流陪你", to: stack)
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
        let (page, stack) = makePage("灵感口袋")
        addTitle("灵感口袋", to: stack)
        stack.addArrangedSubview(hint("听个笑话、猜个小问题，或收到一句关心。用“悄悄话”换一套日常台词，还能给两只桌宠写小故事。互动偏好随账号同步，自己导入的台词和剧本保存在这台设备。"))
        stack.addArrangedSubview(separator())
        addSection("悄悄话", to: stack)
        wordPacksPopup.target = self
        wordPacksPopup.action = #selector(wordPackSelectionChanged(_:))
        stack.addArrangedSubview(labeledRow("选择悄悄话", controls: [wordPacksPopup]))
        wordPackDetail.textColor = .secondaryLabelColor
        stack.addArrangedSubview(wordPackDetail)
        wordPackEnabledCheckbox.target = self
        wordPackEnabledCheckbox.action = #selector(wordPackEnabledChanged(_:))
        stack.addArrangedSubview(wordPackEnabledCheckbox)
        let importWords = NSButton(title: "带来新台词", target: self, action: #selector(importWordPack))
        let deleteWords = NSButton(title: "删除所选", target: self, action: #selector(deleteWordPack))
        let wordGuide = NSButton(title: "怎么写悄悄话", target: self, action: #selector(showWordGuide))
        stack.addArrangedSubview(buttonRow([importWords, deleteWords, wordGuide]))
        stack.addArrangedSubview(separator())
        addSection("小剧场 · 自编小故事", to: stack)
        scriptsPopup.target = self
        scriptsPopup.action = #selector(scriptSelectionChanged(_:))
        stack.addArrangedSubview(labeledRow("可用剧本", controls: [scriptsPopup]))
        scriptDetail.textColor = .secondaryLabelColor
        stack.addArrangedSubview(scriptDetail)
        scriptEnabledCheckbox.target = self
        scriptEnabledCheckbox.action = #selector(scriptEnabledChanged(_:))
        stack.addArrangedSubview(scriptEnabledCheckbox)
        let importScript = NSButton(title: "导入剧本", target: self, action: #selector(importScriptFile))
        let deleteScript = NSButton(title: "删除所选", target: self, action: #selector(deleteScript))
        let scriptGuide = NSButton(title: "怎么写小故事", target: self, action: #selector(showScriptGuide))
        stack.addArrangedSubview(buttonRow([importScript, deleteScript, scriptGuide]))
        return page
    }

    private func buildRemindersPage() -> NSViewController {
        let (page, stack) = makePage("贴心提醒")
        addTitle("贴心提醒", to: stack)
        stack.addArrangedSubview(hint("最多 20 条。需要保持应用运行；到点后请求系统通知，桌宠忙碌时排队展示。暂停打扰不会暂停提醒，隐藏时在恢复显示后补看。"))
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
        reminderExpressionPath.isEditable = false
        reminderExpressionPath.placeholderString = "不指定则保持当前桌宠"
        reminderExpressionPath.widthAnchor.constraint(equalToConstant: 360).isActive = true
        let chooseExpression = NSButton(title: "选择 GIF", target: self, action: #selector(chooseReminderExpression))
        let clearExpression = NSButton(title: "清除", target: self, action: #selector(clearReminderExpression))
        stack.addArrangedSubview(labeledRow("指定表情", controls: [reminderExpressionPath, chooseExpression, clearExpression]))
        reminderEnabledCheckbox.state = .on
        stack.addArrangedSubview(buttonRow([reminderEnabledCheckbox, reminderDailyCheckbox]))
        let save = NSButton(title: "保存提醒", target: self, action: #selector(saveReminder))
        let delete = NSButton(title: "删除提醒", target: self, action: #selector(deleteReminder))
        stack.addArrangedSubview(buttonRow([save, delete]))
        return page
    }

    private func buildFeedbackPage() -> NSViewController {
        let (page, stack) = makePage("心愿信箱")
        addTitle("心愿信箱 · 问题与建议", to: stack)

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
        stack.addArrangedSubview(readOnlyTextArea(feedbackDetail, height: 180))

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

    private func buildUninstallPage() -> NSViewController {
        let (page, stack) = makePage("卸载与反馈")
        addTitle("卸载与反馈", to: stack)
        stack.addArrangedSubview(hint("原因和说明都可以跳过。点击卸载后，桌搭子会退出并由独立卸载助手移到废纸篓；确认移除成功后才会上报完成。取消不会卸载。"))
        uninstallReasonPopup.addItem(withTitle: "不选择原因")
        for reason in UninstallReason.allCases {
            uninstallReasonPopup.addItem(withTitle: reason.title)
            uninstallReasonPopup.lastItem?.representedObject = reason.rawValue
        }
        uninstallReasonPopup.target = self
        uninstallReasonPopup.action = #selector(uninstallReasonChanged(_:))
        stack.addArrangedSubview(labeledRow("可选原因", controls: [uninstallReasonPopup]))
        uninstallComment.placeholderString = "选择“其他”后可补充说明（最多 500 字）"
        uninstallComment.widthAnchor.constraint(equalToConstant: 500).isActive = true
        uninstallComment.isEnabled = false
        stack.addArrangedSubview(labeledRow("补充说明", controls: [uninstallComment]))
        uninstallSubmitButton.target = self
        uninstallSubmitButton.action = #selector(submitUninstallIntent)
        uninstallSkipButton.target = self
        uninstallSkipButton.action = #selector(uninstallWithoutFeedback)
        stack.addArrangedSubview(buttonRow([uninstallSubmitButton, uninstallSkipButton]))
        uninstallGuideButton.target = self
        uninstallGuideButton.action = #selector(showUninstallGuideOnly)
        stack.addArrangedSubview(uninstallGuideButton)
        uninstallStatus.textColor = .secondaryLabelColor
        uninstallStatus.maximumNumberOfLines = 0
        uninstallStatus.widthAnchor.constraint(equalToConstant: 620).isActive = true
        stack.addArrangedSubview(uninstallStatus)
        stack.addArrangedSubview(separator())
        addSection("macOS 卸载方式", to: stack)
        stack.addArrangedSubview(hint("也可以退出桌搭子后，在 Finder 的“应用程序”中手动移到废纸篓。macOS 无法保证这种删除发生时应用还能运行，所以手动删除不会冒充“已报告卸载”；以后重新下载并联网启动时会继续记录实际活跃状态。"))
        return page
    }

    private func buildUpdatePage() -> NSViewController {
        let (page, stack) = makePage("成长日记")
        addTitle("成长日记 · 软件更新", to: stack)
        autoUpdateCheckbox.target = self
        autoUpdateCheckbox.action = #selector(autoUpdateChanged(_:))
        stack.addArrangedSubview(autoUpdateCheckbox)
        stack.addArrangedSubview(separator())
        addSection("使用授权", to: stack)
        activationLabel.textColor = .secondaryLabelColor
        stack.addArrangedSubview(activationLabel)
        stack.addArrangedSubview(NSButton(title: "更换激活码", target: self, action: #selector(activateDevice)))
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
        addSection("这个版本更新了什么", to: stack)
        stack.addArrangedSubview(readOnlyTextArea(releaseNotes, height: 180))
        return page
    }

    private func makePage(_ title: String) -> (NSViewController, NSStackView) {
        let controller = NSViewController()
        controller.title = title
        let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 880, height: 620))
        scroll.hasVerticalScroller = true
        scroll.drawsBackground = false
        let view = SettingsDocumentView()
        view.translatesAutoresizingMaskIntoConstraints = false
        scroll.documentView = view
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 13
        stack.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 42),
            stack.trailingAnchor.constraint(lessThanOrEqualTo: view.trailingAnchor, constant: -42),
            stack.topAnchor.constraint(equalTo: view.topAnchor, constant: 32),
            stack.bottomAnchor.constraint(equalTo: view.bottomAnchor, constant: -32),
            view.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor)
        ])
        controller.view = scroll
        return (controller, stack)
    }

    private func readOnlyTextArea(_ text: NSTextView, height: CGFloat) -> NSScrollView {
        text.isEditable = false
        text.isSelectable = true
        text.isRichText = false
        text.font = .systemFont(ofSize: 13)
        text.textContainerInset = NSSize(width: 10, height: 10)
        text.isVerticallyResizable = true
        text.isHorizontallyResizable = false
        text.autoresizingMask = [.width]
        text.frame = NSRect(x: 0, y: 0, width: 650, height: height)
        text.textContainer?.widthTracksTextView = true
        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        scroll.documentView = text
        scroll.widthAnchor.constraint(equalToConstant: 650).isActive = true
        scroll.heightAnchor.constraint(equalToConstant: height).isActive = true
        return scroll
    }

    private func addTitle(_ text: String, to stack: NSStackView) {
        let label = NSTextField(labelWithString: text)
        label.font = .systemFont(ofSize: 22, weight: .bold)
        label.textColor = .labelColor
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
        label.maximumNumberOfLines = 0
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
        let premium = licenses.hasPremiumAccess
        companionButton.title = licenses.isActivated ? "打开搭子联机" : "激活后使用"
        companionStatusLabel.stringValue = licenses.isActivated ? "搭子联机已可用" : "激活完整版本后可使用搭子联机"
        companionStatusIcon.contentTintColor = licenses.isActivated ? .systemGreen : .secondaryLabelColor
        sizeSlider.integerValue = settings.size
        sizeValue.stringValue = "\(settings.size) px"
        opacitySlider.integerValue = settings.opacity
        opacityValue.stringValue = "\(settings.opacity)%"
        personalityPopup.selectItem(at: personalityValues.firstIndex(of: settings.personality) ?? 0)
        mouseCheckbox.state = settings.mouseInteractionEnabled ? .on : .off
        movementCheckbox.state = settings.randomMovementEnabled ? .on : .off
        randomInteractionCheckbox.state = settings.randomInteractionsEnabled ? .on : .off
        dailySpeechCheckbox.state = settings.dailySpeechEnabled ? .on : .off
        quietButton.title = petController.isQuiet ? "恢复主动陪伴" : "暂停打扰 1 小时"
        quietLabel.stringValue = petController.isQuiet && settings.quietUntilUtc != nil
            ? "暂停至 \(DateFormatter.localizedString(from: settings.quietUntilUtc!, dateStyle: .none, timeStyle: .short))" : "主动陪伴正常"
        interactionModePopup.isEnabled = settings.randomInteractionsEnabled
        interactionModePopup.selectItem(at: interactionModes.firstIndex(of: settings.dailyFrequency) ?? 1)
        theaterCheckbox.state = settings.theaterEnabled ? .on : .off
        theaterIntervalPopup.selectItem(at: theaterIntervals.firstIndex(of: settings.theaterIntervalSeconds) ?? 2)
        alwaysOnTopCheckbox.state = settings.alwaysOnTop ? .on : .off
        startupCheckbox.state = LoginItemService.isEnabled ? .on : .off
        mirrorCheckbox.state = settings.mirrored ? .on : .off
        clickThroughCheckbox.state = settings.clickThrough ? .on : .off
        dockCheckbox.state = settings.dockIconVisible ? .on : .off
        randomPetCheckbox.state = settings.randomPetEnabled ? .on : .off
        randomIntervalPopup.selectItem(at: randomIntervals.firstIndex(of: settings.randomPetIntervalSeconds) ?? 0)
        autoUpdateCheckbox.state = settings.autoCheckUpdates ? .on : .off
        let config = remoteConfig()
        hallButton.isEnabled = config.companionHall
        hallAccessLabel.stringValue = !config.companionHall ? "大厅暂时关闭。" : premium ? "现在可以打开大厅并选择加入。" : "有效体验或正式激活后可以加入大厅。"
        announcementLabel.stringValue = config.announcement
        announcementLabel.isHidden = config.announcement.isEmpty
        autoUpdateCheckbox.isHidden = !config.autoUpdates
        for view in fishModeSection { view.isHidden = !config.fishMode }
        refreshPets(settings)
        refreshLibraries(settings)
        refreshContent(settings)
        refreshReminders(settings)
        refreshUpdateState()
        dailyPage?.refresh()
        if !premium {
            libraryDetail.stringValue = "基础陪伴可使用内置图鉴；激活后可添加自己的 GIF 文件夹"
        }
    }

    func refreshCurrentPetPreview() {
        let guide = petController.currentSettings.guide
        experienceGuideButton.title = guide.isDone ? "重新体验" : guide.step == 0 ? "开始体验" : "继续体验"
        currentPetLabel.stringValue = petController.currentPetSummary
        currentPetPreview.image = petController.currentGIFURL.flatMap { NSImage(contentsOf: $0) }
    }

    private func refreshPets(_ settings: AppSettings) {
        refreshCurrentPetPreview()
        let selected = petsPopup.selectedItem?.representedObject as? String
        petsPopup.removeAllItems()
        for pet in settings.pets {
            petsPopup.addItem(withTitle: pet.name)
            petsPopup.lastItem?.representedObject = pet.id
        }
        select(popup: petsPopup, id: selected ?? settings.activePetId)
        petsDetail.stringValue = "小窝里有 \(settings.pets.count)/3 只自选桌宠；选好后点“让它陪我”。"
    }

    private func refreshLibraries(_ settings: AppSettings) {
        let selected = librariesPopup.selectedItem?.representedObject as? String
        librariesPopup.removeAllItems()
        librariesPopup.addItem(withTitle: "内置图鉴")
        for library in settings.libraries {
            let enabled = settings.enabledLibraryIds?.contains(library.id) == true
            librariesPopup.addItem(withTitle: "\(enabled ? "✓ " : "")\(library.name) · 本地桌宠")
            librariesPopup.lastItem?.representedObject = library.id
        }
        if let selected, settings.libraries.contains(where: { $0.id == selected }) {
            select(popup: librariesPopup, id: selected)
        } else {
            librariesPopup.selectItem(at: 0)
        }
        if let id = librariesPopup.selectedItem?.representedObject as? String,
           let library = settings.libraries.first(where: { $0.id == id }) {
            let enabled = settings.enabledLibraryIds?.contains(id) == true
            libraryDetail.stringValue = "\(library.name) · 本地桌宠 · \(enabled ? "已启用" : "待启用")。已启用来源 \(petController.enabledPetSourceCount)/3。"
            libraryEnabledCheckbox.title = "启用所选来源"
            libraryEnabledCheckbox.state = enabled ? .on : .off
            libraryEnabledCheckbox.isEnabled = true
        } else {
            let usingBuiltIn = petController.enabledPetSourceCount == 0
            libraryDetail.stringValue = usingBuiltIn
                ? "正在由内置图鉴里的小搭子陪你。"
                : "启用内置图鉴会停用当前全部本地与小铺来源。"
            libraryEnabledCheckbox.title = "使用内置图鉴"
            libraryEnabledCheckbox.state = usingBuiltIn ? .on : .off
            libraryEnabledCheckbox.isEnabled = !usingBuiltIn
        }
    }

    private func refreshContent(_ settings: AppSettings) {
        let selectedWords = wordPacksPopup.selectedItem?.representedObject as? String
        wordPacksPopup.removeAllItems()
        wordPacksPopup.addItem(withTitle: "内置悄悄话")
        for pack in settings.interactionWordPacks {
            wordPacksPopup.addItem(withTitle: "\(pack.enabled ? "✓ " : "")\(pack.name)")
            wordPacksPopup.lastItem?.representedObject = pack.id
        }
        if let selectedWords, settings.interactionWordPacks.contains(where: { $0.id == selectedWords }) {
            select(popup: wordPacksPopup, id: selectedWords)
        } else {
            wordPacksPopup.selectItem(at: 0)
        }
        let enabledWordCount = settings.interactionWordPacks.filter(\.enabled).count
        if let id = wordPacksPopup.selectedItem?.representedObject as? String,
           let pack = settings.interactionWordPacks.first(where: { $0.id == id }) {
            wordPackDetail.stringValue = "\(pack.wordCount) 句悄悄话 · \(pack.enabled ? "已启用" : "待启用") · 已启用 \(enabledWordCount)/10"
            wordPackEnabledCheckbox.state = pack.enabled ? .on : .off
            wordPackEnabledCheckbox.isEnabled = true
        } else {
            wordPackDetail.stringValue = enabledWordCount == 0
                ? "正在说内置悄悄话"
                : "\(enabledWordCount) 套词库会按动作合并、去重后随机取词；没有匹配内容时回退内置台词。"
            wordPackEnabledCheckbox.state = .off
            wordPackEnabledCheckbox.isEnabled = false
        }
        let selectedScript = scriptsPopup.selectedItem?.representedObject as? String
        scriptsPopup.removeAllItems()
        scriptsPopup.addItem(withTitle: "内置小剧场（3 套）")
        for script in settings.theaterScripts {
            scriptsPopup.addItem(withTitle: "\(script.enabled ? "✓ " : "")\(script.name)")
            scriptsPopup.lastItem?.representedObject = script.id
        }
        select(popup: scriptsPopup, id: selectedScript)
        let enabledScriptCount = settings.theaterScripts.filter(\.enabled).count
        if let id = scriptsPopup.selectedItem?.representedObject as? String,
           let script = settings.theaterScripts.first(where: { $0.id == id }) {
            scriptDetail.stringValue = "\(script.scenes.count) 轮对话 · \(script.enabled ? "已启用" : "待启用") · 已启用 \(enabledScriptCount)/10"
            scriptEnabledCheckbox.state = script.enabled ? .on : .off
            scriptEnabledCheckbox.isEnabled = true
        } else {
            scriptDetail.stringValue = enabledScriptCount == 0
                ? "没有启用自定义剧本时，内置长对话剧本会参与随机上演。"
                : "已启用 \(enabledScriptCount)/10 个自定义剧本；自动演出仍由小剧场总开关控制。"
            scriptEnabledCheckbox.state = .off
            scriptEnabledCheckbox.isEnabled = false
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
        activationLabel.stringValue = licenses.isActivated
            ? licenses.summary
            : licenses.hasPremiumAccess ? "七天完整功能体验中" : "免费版 - 激活可解锁完整功能"
        let status = updates.status
        let notes = updates.availableManifest?.notes.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        releaseNotes.string = notes.isEmpty ? "检查更新后，会在这里展示本次新增与修复说明。" : notes
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
            feedbackDetail.string = feedbackItems.isEmpty ? "提交后可在这里查看处理状态和后台回复。" : ""
            return
        }
        let timestamp = feedbackDate(item.updatedAt)
        var detail = "\(item.title)\n\(feedbackType(item.type)) · \(feedbackStatus(item.status)) · 更新于 \(timestamp)\n\n\(item.content)"
        if !item.adminNote.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            detail += "\n后台回复：\(item.adminNote)"
        }
        feedbackDetail.string = detail
        feedbackDetail.scrollToBeginningOfDocument(nil)
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

    private func requirePremium(_ feature: String, continuation: (@MainActor () -> Void)? = nil) -> Bool {
        if licenses.hasPremiumAccess { return true }
        Task { @MainActor [weak self] in
            guard let self else { return }
            if await ActivationPrompts.activate(
                licenses: licenses,
                required: true,
                statusMessage: "\(feature)需要激活完整功能；基础陪伴仍可免费使用。"
            ) {
                petController.refreshPremiumAccess()
                refresh()
                continuation?()
            }
        }
        return false
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
        let premiumControl = sender as AnyObject === randomInteractionCheckbox
            || sender as AnyObject === interactionModePopup
            || sender as AnyObject === theaterCheckbox
            || sender as AnyObject === theaterIntervalPopup
        if premiumControl, !requirePremium("互动与小剧场") { refresh(); return }
        interactionModePopup.isEnabled = randomInteractionCheckbox.state == .on
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
            $0.dailyFrequency = interactionModes[interactionModePopup.indexOfSelectedItem]
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

    @objc private func startTheater() {
        guard requirePremium("小剧场", continuation: { [weak self] in _ = self?.petController.startTheater() }) else { return }
        _ = petController.startTheater()
    }

    @objc private func startRandomInteraction() {
        guard requirePremium("互动内容", continuation: { [weak self] in self?.petController.startRandomInteraction() }) else { return }
        petController.startRandomInteraction()
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
            petController.update {
                $0.pets.append(pet)
                $0.activePetId = pet.id
                $0.enabledLibraryIds = []
                $0.enabledMarketProductIds = []
                $0.activeLibraryId = nil
                $0.activeMarketProductId = nil
            }
            refresh()
        } catch { show(error) }
    }

    @objc private func petSelectionChanged(_ sender: NSPopUpButton) {
        if let id = sender.selectedItem?.representedObject as? String,
           let pet = petController.currentSettings.pets.first(where: { $0.id == id }) {
            petsDetail.stringValue = "\(pet.name) · 本地桌宠。选好后点“让它陪我”。"
        }
    }

    @objc private func useSelectedPet() {
        guard let id = petsPopup.selectedItem?.representedObject as? String else { return }
        petController.update {
            $0.activePetId = id
            $0.enabledLibraryIds = []
            $0.enabledMarketProductIds = []
            $0.activeLibraryId = nil
            $0.activeMarketProductId = nil
        }
        refresh()
    }

    @objc private func useDefaultPet() {
        try? petController.setLibraryEnabled(nil, enabled: true)
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
        guard requirePremium("自选桌宠图鉴") else { return }
        let panel = NSOpenPanel()
        panel.title = "为桌宠图鉴选择 GIF 文件夹"
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return }
        guard gifCount(in: url) > 0 else { show(ContentImportError.unsupportedGIF); return }
        if petController.currentSettings.libraries.contains(where: { $0.path.standardizedPath == url.path.standardizedPath }) { return }
        let library = LibraryDefinition(name: url.lastPathComponent.cleaned(limit: 40), path: url.path)
        petController.update { $0.libraries.append(library) }
        refresh()
        select(popup: librariesPopup, id: library.id)
        refreshLibraries(petController.currentSettings)
    }

    @objc private func librarySelectionChanged(_ sender: NSPopUpButton) {
        guard !refreshing else { return }
        refreshLibraries(petController.currentSettings)
    }

    @objc private func libraryEnabledChanged(_ sender: NSButton) {
        guard !refreshing else { return }
        let id = librariesPopup.selectedItem?.representedObject as? String
        let enabled = sender.state == .on
        if id != nil, enabled, !requirePremium("自选桌宠图鉴", continuation: { [weak self] in
            guard let self else { return }
            do { try self.petController.setLibraryEnabled(id, enabled: true); self.refresh() }
            catch { self.show(error) }
        }) { refresh(); return }
        do { try petController.setLibraryEnabled(id, enabled: enabled); refresh() }
        catch { show(error); refresh() }
    }

    @objc private func deleteSelectedLibrary() {
        guard let id = librariesPopup.selectedItem?.representedObject as? String,
              let library = petController.currentSettings.libraries.first(where: { $0.id == id }), confirmDelete(library.name) else { return }
        petController.update {
            $0.libraries.removeAll { $0.id == id }
            $0.enabledLibraryIds?.removeAll { $0 == id }
            if $0.activeLibraryId == id { $0.activeLibraryId = $0.enabledLibraryIds?.first }
        }
        refresh()
    }

    @objc private func randomSettingsChanged(_ sender: Any) {
        guard !refreshing else { return }
        petController.update {
            $0.randomPetEnabled = randomPetCheckbox.state == .on
            $0.randomPetIntervalSeconds = randomIntervals[randomIntervalPopup.indexOfSelectedItem]
        }
    }

    @objc private func randomizeNow() { _ = petController.randomizePet(); refresh() }

    @objc private func importWordPack() {
        guard requirePremium("自选悄悄话") else { return }
        let panel = NSOpenPanel()
        panel.title = "导入悄悄话（JSON 或 TXT）"
        panel.allowedFileTypes = ["json", "txt"]
        panel.allowsMultipleSelection = true
        guard panel.runModal() == .OK else { return }
        do {
            let packs = try panel.urls.map { try InteractionWordPackImporter.load(from: $0) }
            petController.update { $0.interactionWordPacks.append(contentsOf: packs) }
            refresh()
        } catch { show(error) }
    }

    @objc private func wordPackSelectionChanged(_ sender: NSPopUpButton) {
        guard !refreshing else { return }
        refreshContent(petController.currentSettings)
    }

    @objc private func wordPackEnabledChanged(_ sender: NSButton) {
        guard !refreshing, let id = wordPacksPopup.selectedItem?.representedObject as? String else { return }
        let enabled = sender.state == .on
        if enabled, !requirePremium("互动词库", continuation: { [weak self] in
            guard let self else { return }
            do { try self.petController.setInteractionWordPackEnabled(id, enabled: true); self.refresh() }
            catch { self.show(error) }
        }) { refresh(); return }
        do { try petController.setInteractionWordPackEnabled(id, enabled: enabled); refresh() }
        catch { show(error); refresh() }
    }

    @objc private func deleteWordPack() {
        guard let id = wordPacksPopup.selectedItem?.representedObject as? String,
              let pack = petController.currentSettings.interactionWordPacks.first(where: { $0.id == id }), confirmDelete(pack.name) else { return }
        petController.update { $0.interactionWordPacks.removeAll { $0.id == id } }
        refresh()
    }

    @objc private func showWordGuide() { showGuide(title: "怎么写悄悄话", text: InteractionWordPackImporter.guide) }

    @objc private func importScriptFile() {
        guard requirePremium("小剧场剧本导入") else { return }
        let panel = NSOpenPanel()
        panel.title = "导入小剧场剧本"
        panel.allowedFileTypes = ["json"]
        panel.allowsMultipleSelection = true
        guard panel.runModal() == .OK else { return }
        do {
            let scripts = try panel.urls.map { try TheaterScriptImporter.load(from: $0) }
            petController.update { $0.theaterScripts.append(contentsOf: scripts) }
            refresh()
        } catch { show(error) }
    }

    @objc private func scriptSelectionChanged(_ sender: NSPopUpButton) { refreshContent(petController.currentSettings) }

    @objc private func scriptEnabledChanged(_ sender: NSButton) {
        guard !refreshing, let id = scriptsPopup.selectedItem?.representedObject as? String else { return }
        let enabled = sender.state == .on
        if enabled, !requirePremium("小剧场", continuation: { [weak self] in
            guard let self else { return }
            do { try self.petController.setTheaterScriptEnabled(id, enabled: true); self.refresh() }
            catch { self.show(error) }
        }) { refresh(); return }
        do { try petController.setTheaterScriptEnabled(id, enabled: enabled); refresh() }
        catch { show(error); refresh() }
    }

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
        reminderExpressionPath.stringValue = reminder.expressionPath ?? ""
        reminderEnabledCheckbox.state = reminder.enabled ? .on : .off
        reminderDailyCheckbox.state = reminder.repeatDaily ? .on : .off
    }

    @objc private func newReminder() {
        guard requirePremium("提醒") else { return }
        editingReminderId = nil
        remindersPopup.selectItem(at: 0)
        reminderDatePicker.dateValue = Date().addingTimeInterval(600)
        reminderMessage.stringValue = "休息一下吧"
        reminderExpressionPath.stringValue = ""
        reminderEnabledCheckbox.state = .on
        reminderDailyCheckbox.state = .off
    }

    @objc private func saveReminder() {
        guard requirePremium("提醒") else { return }
        let message = reminderMessage.stringValue.cleaned(limit: 40)
        guard !message.isEmpty else { show(ContentImportError.limitReached("请输入提醒内容")); return }
        if reminderEnabledCheckbox.state == .on, reminderDailyCheckbox.state != .on, reminderDatePicker.dateValue <= Date() {
            show(ContentImportError.limitReached("一次性提醒需要选择未来的日期与时间。"))
            return
        }
        if editingReminderId == nil, petController.currentSettings.reminders.count >= 20 { show(ContentImportError.limitReached("最多只能保存 20 条提醒")); return }
        let reminder = ReminderDefinition(
            id: editingReminderId ?? "reminder-\(UUID().uuidString)",
            enabled: reminderEnabledCheckbox.state == .on,
            at: reminderDatePicker.dateValue,
            message: message,
            expressionPath: reminderExpressionPath.stringValue.isEmpty ? nil : reminderExpressionPath.stringValue,
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

    @objc private func chooseReminderExpression() {
        guard requirePremium("提醒表情") else { return }
        let panel = NSOpenPanel()
        panel.title = "选择提醒出现时展示的 GIF"
        panel.allowedFileTypes = ["gif"]
        panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url {
            reminderExpressionPath.stringValue = url.path
        }
    }

    @objc private func clearReminderExpression() {
        reminderExpressionPath.stringValue = ""
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

    @objc private func submitUninstallIntent() {
        let comment = uninstallComment.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard comment.unicodeScalars.count <= 500 else {
            show(UninstallFeedbackError.invalidComment)
            return
        }
        let reason = (uninstallReasonPopup.selectedItem?.representedObject as? String)
            .flatMap(UninstallReason.init(rawValue:))
        guard comment.isEmpty || reason == .other else {
            show(UninstallFeedbackError.invalidCommentReason)
            return
        }
        beginControlledUninstall(reason: reason, comment: comment)
    }

    @objc private func uninstallWithoutFeedback() {
        beginControlledUninstall(reason: nil, comment: nil)
    }

    @objc private func uninstallReasonChanged(_ sender: NSPopUpButton) {
        let allowsComment = (sender.selectedItem?.representedObject as? String) == UninstallReason.other.rawValue
        uninstallComment.isEnabled = allowsComment
        if !allowsComment { uninstallComment.stringValue = "" }
    }

    private func beginControlledUninstall(reason: UninstallReason?, comment: String?) {
        let confirmation = NSAlert()
        confirmation.messageText = "确认卸载桌搭子？"
        confirmation.informativeText = "桌搭子将退出并移到废纸篓。卸载不会解除授权、删除购买权益或自动释放设备名额。网络异常不会阻止卸载，但可能无法在后台标记完成。"
        confirmation.alertStyle = .warning
        confirmation.addButton(withTitle: "卸载")
        confirmation.addButton(withTitle: "取消")
        guard confirmation.runModal() == .alertFirstButtonReturn else {
            uninstallStatus.stringValue = "已取消，桌搭子仍保留在本机。"
            return
        }

        let installBatchId = uninstallFeedbackService.currentInstallBatchId
        setUninstallControlsEnabled(false)
        uninstallStatus.stringValue = reason == nil && comment == nil ? "正在准备卸载…" : "正在提交可选反馈并准备卸载…"
        Task { @MainActor [weak self] in
            guard let self else { return }
            var receipt: UninstallIntentReceipt?
            do {
                receipt = try await uninstallFeedbackService.reportIntent(
                    reason: reason,
                    comment: comment,
                    installBatchId: installBatchId
                )
            } catch {
                uninstallStatus.stringValue = "网络记录未完成，仍将继续卸载；后台不会据此猜测完成状态。"
            }
            do {
                try uninstallFeedbackService.launchControlledUninstall(
                    receipt: receipt,
                    installBatchId: installBatchId
                )
                NSApplication.shared.terminate(nil)
            } catch {
                setUninstallControlsEnabled(true)
                uninstallStatus.stringValue = "未能启动受控卸载，应用仍保留；可以改用 Finder 手动移到废纸篓。"
                show(error)
            }
        }
    }

    @objc private func showUninstallGuideOnly() {
        showUninstallGuide()
    }

    private func setUninstallControlsEnabled(_ enabled: Bool) {
        uninstallSubmitButton.isEnabled = enabled
        uninstallSkipButton.isEnabled = enabled
        uninstallGuideButton.isEnabled = enabled
        uninstallReasonPopup.isEnabled = enabled
        let otherSelected = (uninstallReasonPopup.selectedItem?.representedObject as? String) == UninstallReason.other.rawValue
        uninstallComment.isEnabled = enabled && otherSelected
    }

    private func showUninstallGuide() {
        let alert = NSAlert()
        alert.messageText = "从 macOS 手动卸载桌搭子"
        alert.informativeText = "先从菜单栏退出桌搭子，再在 Finder 中把应用移到废纸篓。手动删除通常无法可靠回传完成状态，因此后台不会仅凭离线推测你已卸载；以后重新下载并联网启动时会继续记录实际活跃状态。"
        alert.addButton(withTitle: "在 Finder 中显示桌搭子")
        alert.addButton(withTitle: "稍后处理")
        if alert.runModal() == .alertFirstButtonReturn {
            NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL])
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
                confirm.messageText = "更换激活码"
                confirm.informativeText = "输入新的激活码后，当前设备会更新绑定记录。"
                confirm.addButton(withTitle: "继续")
                confirm.addButton(withTitle: "取消")
                guard confirm.runModal() == .alertFirstButtonReturn else { return }
            }
            if await ActivationPrompts.activate(licenses: licenses, required: false, replacingExisting: licenses.isActivated) {
                petController.refreshPremiumAccess()
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

    @objc private func openCompanionAction() { openCompanion() }
    @objc private func openHallAction() { openHall() }
    @objc private func showUsageGuideAction() { showUsageGuide() }
    @objc private func openExperienceGuideAction() { openExperienceGuide() }
    @objc private func dailySpeechChanged() { petController.update { $0.dailySpeechEnabled = dailySpeechCheckbox.state == .on } }
    @objc private func toggleQuiet() {
        if petController.isQuiet { petController.resumeProactive() } else { petController.pauseProactiveForOneHour() }
        refresh()
    }
    @objc private func stopCurrentScene() { petController.stopCurrentScene() }

    @objc private func openFakeAdAction() { openFakeAd() }

    func refreshAccessState() { refresh() }

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

    private func show(_ error: Error) {
        let alert = NSAlert()
        alert.messageText = "操作未完成"
        if let localized = error as? LocalizedError, let message = localized.errorDescription,
           !message.isEmpty {
            alert.informativeText = message
        } else {
            alert.informativeText = "请稍后重试；如果问题持续，请检查网络或联系支持。"
        }
        alert.alertStyle = .warning
        alert.addButton(withTitle: "知道了")
        alert.runModal()
    }

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

private final class SettingsDocumentView: NSView {
    override var isFlipped: Bool { true }
}
