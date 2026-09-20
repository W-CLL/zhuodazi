import AppKit
import UserNotifications

@MainActor
func presentFriendlyError(_ error: Error, title: String) {
    let alert = NSAlert()
    alert.messageText = title
    if let localized = error as? LocalizedError,
       let message = localized.errorDescription,
       !message.isEmpty {
        alert.informativeText = message
    } else {
        alert.informativeText = "请稍后重试；如果问题持续，请检查网络或联系支持。"
    }
    alert.alertStyle = .warning
    alert.addButton(withTitle: "知道了")
    alert.runModal()
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let settingsStore = SettingsStore()
    private let remoteConfig = RemoteConfigService()
    private var settings = AppSettings()
    private var petController: PetWindowController!
    private var licenses: LicenseService!
    private var analytics: AnalyticsService!
    private var interactions: InteractionService!
    private var companions: CompanionService!
    private var updates: UpdateService!
    private var settingsWindow: SettingsWindowController?
    private var fakeAdWindow: FakeAdWindowController?
    private var companionWindow: CompanionWindowController?
    private var hallWindow: HallWindowController?
    private var guideWindow: GuideWindowController?
    private var guideMenuItem: NSMenuItem!
    private var statusItem: NSStatusItem!
    private var visibilityItem: NSMenuItem!
    private var mouseItem: NSMenuItem!
    private var movementItem: NSMenuItem!
    private var interactionItem: NSMenuItem!
    private var randomPetItem: NSMenuItem!
    private var randomizeNowItem: NSMenuItem!
    private var theaterItem: NSMenuItem!
    private var sendCompanionItem: NSMenuItem!
    private var hallItem: NSMenuItem!
    private var quietItem: NSMenuItem!
    private var speechItem: NSMenuItem!
    private var girlfriendVisitItem: NSMenuItem!
    private var friendVisitItem: NSMenuItem!
    private var companionVisitItem: NSMenuItem!
    private var fishModeItem: NSMenuItem!
    private var trialVisitBusy = false
    private var topmostItem: NSMenuItem!
    private var clickThroughItem: NSMenuItem!
    private var reminderTimer: Timer?
    private var trialTimer: Timer?
    private var companionTimer: Timer?
    private var companionPolling = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        do {
            settings = settingsStore.load()
            licenses = try LicenseService()
            analytics = AnalyticsService(licenses: licenses)
            interactions = InteractionService(licenses: licenses)
            companions = CompanionService(licenses: licenses)
            updates = UpdateService(licenses: licenses)
            petController = PetWindowController(
                settings: settings,
                interactions: interactions,
                premiumAccess: { [weak self] in self?.licenses?.hasPremiumAccess ?? false }
            ) { [weak self] settings in
                    self?.settings = settings
                    self?.settingsStore.save(settings)
                    self?.refreshMenuState()
                    self?.settingsWindow?.refreshCurrentPetPreview()
                    self?.hallWindow?.refreshAccessState()
                }
            applyDockVisibility(settings.dockIconVisible)
            configureStatusMenu()

            if !licenses.isActivated {
                Task { @MainActor [weak self] in
                    guard let self else { return }
                    do {
                        let trial = try await licenses.checkTrial()
                        if trial.allowed {
                            scheduleTrialCheck(trial.remainingSeconds)
                        } else {
                            if await ActivationPrompts.activate(
                                licenses: licenses,
                                required: true,
                                statusMessage: "刚才试过的互动和小剧场还可以接着用。想慢慢玩，先留下基础陪伴也完全没问题。",
                                trialEnded: true
                            ) {
                                petController.refreshPremiumAccess()
                                startCompanionPolling()
                            }
                        }
                    } catch {
                        // 网络验证失败时不长期信任本地状态：最迟 1 小时后重新验证
                        if licenses.isTrialActive {
                            scheduleTrialCheck(min(licenses.remainingTrialSecondsNow, 3600))
                        }
                    }
                    petController.refreshPremiumAccess()
                    refreshMenuState()
                    startPet()
                }
            } else {
                startPet()
            }
        } catch {
            presentFriendlyError(error, title: "启动失败")
            NSApplication.shared.terminate(nil)
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showSettings()
        return true
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    func applicationWillTerminate(_ notification: Notification) {
        fakeAdWindow?.close()
    }

    private func startPet() {
        petController.show()
        presentGuideIfNeeded()
        startReminderChecks()
        analytics.trackStartup()
        Task { @MainActor [weak self] in
            guard let self else { return }
            await refreshRemoteConfig()
            petController.startInteractionServices()
            startCompanionPolling()
            guard settings.autoCheckUpdates, remoteConfig.current.autoUpdates else { return }
            try? await Task.sleep(for: .seconds(3))
            _ = try? await updates.check()
            if let manifest = updates.availableManifest, manifest.version != settings.ignoredUpdateVersion {
                if !petController.isQuiet { petController.showBubble("发现新版本 v\(manifest.version)，可在设置中安装。") }
            }
        }
    }

    private func refreshRemoteConfig() async {
        if await remoteConfig.refresh() {
            applyRemoteDefaultsIfNeeded()
        }
        refreshMenuState()
        settingsWindow?.refreshAccessState()
        hallWindow?.refreshAccessState()
    }

    private func applyRemoteDefaultsIfNeeded() {
        guard !settings.remoteDefaultsApplied else { return }
        let config = remoteConfig.current
        var next = petController.currentSettings
        next.personality = config.personality
        next.interactionMode = config.interactionMode
        next.theaterIntervalSeconds = config.theaterIntervalSeconds
        next.remoteDefaultsApplied = true
        petController.apply(next, userInitiated: false)
    }

    private func presentGuideIfNeeded() {
        if settings.guideUpgradeNoticePending {
            petController.update { $0.guideUpgradeNoticePending = false }
            let alert = NSAlert()
            alert.messageText = "3.3.0：一起玩一分钟"
            alert.informativeText = "新增可继续、可跳过的四步体验，带你试一次互动和一段本地短剧。原来的设置会保留，不会自动重播完整教学。以后可从菜单或设置打开。"
            alert.addButton(withTitle: "开始体验")
            alert.addButton(withTitle: "以后再说")
            if alert.runModal() == .alertFirstButtonReturn { openExperienceGuide() }
        } else if settings.guide.shouldAutoPresent {
            openExperienceGuide()
        }
    }

    @objc private func openExperienceGuide() {
        if settings.guide.isDone { petController.update { $0.guide.replay() } }
        if guideWindow == nil {
            guideWindow = GuideWindowController(
                pet: petController,
                openSettings: { [weak self] in self?.showSettings() },
                openHall: { [weak self] in self?.openHall() },
                playInteraction: { [weak self] in
                    self?.requestPremiumAccess("互动内容") { [weak self] in self?.petController.startRandomInteraction() }
                },
                playTheater: { [weak self] in
                    self?.requestPremiumAccess("小剧场") { [weak self] in _ = self?.petController.startTheater() }
                }
            )
        }
        guideWindow?.showWindow(nil)
    }

    private func scheduleTrialCheck(_ remainingSeconds: Int) {
        trialTimer?.invalidate()
        trialTimer = Timer.scheduledTimer(withTimeInterval: Double(max(1, min(24 * 60 * 60, remainingSeconds))), repeats: false) { [weak self] _ in
            Task { @MainActor [weak self] in
                guard let self else { return }
                guard !self.licenses.isActivated else { return }
                do {
                    let trial = try await self.licenses.checkTrial()
                    if trial.allowed {
                        self.scheduleTrialCheck(trial.remainingSeconds)
                        return
                    }
                } catch {
                    if self.licenses.isTrialActive {
                        self.scheduleTrialCheck(min(self.licenses.remainingTrialSecondsNow, 3600))
                        return
                    }
                }
                self.fakeAdWindow?.close()
                self.fakeAdWindow = nil
                self.petController.refreshPremiumAccess()
                self.petController.showBubble("七天完整体验结束啦，基础陪伴继续。")
                self.settingsWindow?.refreshAccessState()
                if await ActivationPrompts.activate(
                    licenses: self.licenses,
                    required: true,
                    statusMessage: "刚才试过的互动、小剧场和摸鱼模式，激活后都可以继续使用。",
                    trialEnded: true
                ) {
                    self.petController.refreshPremiumAccess()
                    self.settingsWindow?.refreshAccessState()
                    self.startCompanionPolling()
                }
                self.refreshMenuState()
            }
        }
    }

    private func configureStatusMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = NSImage(systemSymbolName: "sparkles", accessibilityDescription: "桌搭子")
        statusItem.button?.toolTip = "桌搭子"

        let menu = NSMenu()
        menu.addItem(withTitle: "陪伴设置…", action: #selector(openSettings), keyEquivalent: ",")
        menu.addItem(withTitle: "检查更新", action: #selector(checkUpdates), keyEquivalent: "")
        hallItem = menu.addItem(withTitle: "桌宠大厅…", action: #selector(openHall), keyEquivalent: "")
        menu.addItem(withTitle: "私人搭子…", action: #selector(openCompanion), keyEquivalent: "")
        fishModeItem = menu.addItem(withTitle: "摸鱼广告", action: #selector(openFishMode), keyEquivalent: "")
        sendCompanionItem = menu.addItem(withTitle: "发送当前 GIF 给搭子", action: #selector(sendCompanionGIF), keyEquivalent: "")
        girlfriendVisitItem = menu.addItem(withTitle: "演示来访：女友", action: #selector(playGirlfriendVisit), keyEquivalent: "")
        friendVisitItem = menu.addItem(withTitle: "演示来访：好友", action: #selector(playFriendVisit), keyEquivalent: "")
        companionVisitItem = menu.addItem(withTitle: "演示来访：搭子", action: #selector(playCompanionVisit), keyEquivalent: "")
        menu.addItem(.separator())
        quietItem = menu.addItem(withTitle: "暂停打扰 1 小时", action: #selector(toggleQuiet), keyEquivalent: "")
        speechItem = menu.addItem(withTitle: "日常气泡", action: #selector(toggleDailySpeech), keyEquivalent: "")
        menu.addItem(withTitle: "停止当前剧场或来访", action: #selector(stopCurrentScene), keyEquivalent: "")
        guideMenuItem = menu.addItem(withTitle: "开始体验…", action: #selector(openExperienceGuide), keyEquivalent: "")
        menu.addItem(withTitle: "文字使用指南…", action: #selector(replayUsageGuide), keyEquivalent: "")
        menu.addItem(.separator())
        visibilityItem = menu.addItem(withTitle: "隐藏桌搭子", action: #selector(toggleVisibility(_:)), keyEquivalent: "")
        mouseItem = menu.addItem(withTitle: "跟随鼠标", action: #selector(toggleMouseInteraction(_:)), keyEquivalent: "")
        movementItem = menu.addItem(withTitle: "随机移动", action: #selector(toggleRandomMovement(_:)), keyEquivalent: "")
        menu.addItem(.separator())
        interactionItem = menu.addItem(withTitle: "主动找你玩", action: #selector(toggleRandomInteractions(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "陪我玩一会", action: #selector(startRandomInteraction(_:)), keyEquivalent: "")
        menu.addItem(.separator())
        randomPetItem = menu.addItem(withTitle: "自动随机换宠", action: #selector(toggleRandomPet(_:)), keyEquivalent: "")
        randomizeNowItem = menu.addItem(withTitle: "立即换一只", action: #selector(randomizePet(_:)), keyEquivalent: "")
        menu.addItem(.separator())
        theaterItem = menu.addItem(withTitle: "自动上演小剧场", action: #selector(toggleTheater(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "看一场小剧场", action: #selector(startTheater(_:)), keyEquivalent: "")
        menu.addItem(.separator())
        topmostItem = menu.addItem(withTitle: "始终置顶", action: #selector(toggleTopmost(_:)), keyEquivalent: "")
        clickThroughItem = menu.addItem(withTitle: "鼠标穿透", action: #selector(toggleClickThrough(_:)), keyEquivalent: "")
        menu.addItem(.separator())
        menu.addItem(withTitle: "退出桌搭子", action: #selector(quit), keyEquivalent: "q")

        for item in menu.items where item.action != nil {
            item.target = self
        }
        statusItem.menu = menu
        petController.contextMenu = menu
        refreshMenuState()
    }

    private func refreshMenuState() {
        guard petController != nil, visibilityItem != nil else { return }
        hallItem.isHidden = !remoteConfig.current.companionHall
        quietItem.title = petController.isQuiet ? "恢复主动陪伴" : "暂停打扰 1 小时"
        quietItem.state = petController.isQuiet ? .on : .off
        speechItem.state = petController.currentSettings.dailySpeechEnabled ? .on : .off
        let guide = petController.currentSettings.guide
        guideMenuItem.title = guide.isDone ? "重新体验…" : guide.step == 0 ? "开始体验…" : "继续体验…"
        visibilityItem.title = petController.isVisible ? "隐藏桌搭子" : "显示桌搭子"
        mouseItem.state = petController.mouseInteractionEnabled ? .on : .off
        movementItem.state = petController.randomMovementEnabled ? .on : .off
        interactionItem.state = petController.currentSettings.randomInteractionsEnabled ? .on : .off
        interactionItem.title = "主动找你玩"
        randomPetItem.state = petController.randomPetEnabled ? .on : .off
        randomPetItem.isEnabled = petController.canRandomizePet
        randomizeNowItem.isEnabled = petController.canRandomizePet
        theaterItem.state = petController.currentSettings.theaterEnabled ? .on : .off
        theaterItem.title = "自动上演小剧场"
        fishModeItem.isHidden = !remoteConfig.current.fishMode
        fishModeItem.title = licenses.isActivated
            ? "摸鱼广告"
            : licenses.isTrialActive ? "摸鱼广告（体验中）" : "摸鱼广告（激活后继续）"
        fishModeItem.isEnabled = remoteConfig.current.fishMode
        let trial = licenses.isTrialActive
        let trialVisits = trial && remoteConfig.current.trialVisits
        girlfriendVisitItem.isHidden = !trialVisits
        friendVisitItem.isHidden = !trialVisits
        companionVisitItem.isHidden = !trialVisits
        sendCompanionItem.isHidden = trial
        if let partner = companions?.profile?.partner {
            sendCompanionItem.title = "发送当前 GIF 给 \(partner.displayName)"
            sendCompanionItem.isEnabled = licenses.isActivated && petController.currentGIFURL != nil
        } else {
            sendCompanionItem.title = "发送当前 GIF 给搭子（先绑定）"
            sendCompanionItem.isEnabled = false
        }
        topmostItem.state = petController.alwaysOnTop ? .on : .off
        clickThroughItem.state = petController.clickThrough ? .on : .off
    }

    private func applyDockVisibility(_ visible: Bool) {
        NSApplication.shared.setActivationPolicy(visible ? .regular : .accessory)
    }

    private func showSettings(checkForUpdates: Bool = false) {
        if settingsWindow == nil {
            settingsWindow = SettingsWindowController(
                petController: petController,
                licenses: licenses,
                updates: updates,
                remoteConfig: { [weak self] in self?.remoteConfig.current ?? RemoteConfig() },
                dockVisibilityChanged: { [weak self] visible in self?.applyDockVisibility(visible) },
                openCompanion: { [weak self] in self?.openCompanion() },
                openHall: { [weak self] in self?.openHall() },
                showUsageGuide: { [weak self] in self?.replayUsageGuide() },
                openExperienceGuide: { [weak self] in self?.openExperienceGuide() },
                openFakeAd: { [weak self] in self?.openFishMode() }
            )
        }
        settingsWindow?.showWindow(nil)
        if checkForUpdates {
            settingsWindow?.checkForUpdatesFromMenu()
        }
    }

    @objc private func openSettings() {
        showSettings()
    }

    @objc private func checkUpdates() {
        showSettings(checkForUpdates: true)
    }

    @objc private func openCompanion() {
        guard licenses.isActivated else {
            Task { @MainActor [weak self] in
                guard let self else { return }
                if await ActivationPrompts.activate(
                    licenses: licenses,
                    required: true,
                    statusMessage: "激活完整版本后可以使用搭子联机。"
                ) {
                    petController.refreshPremiumAccess()
                    settingsWindow?.refreshAccessState()
                    startCompanionPolling()
                    showCompanionWindow()
                }
            }
            return
        }
        showCompanionWindow()
    }

    @objc private func openFishMode() {
        guard remoteConfig.current.fishMode else {
            petController.showBubble("摸鱼广告暂时关掉了。")
            return
        }
        guard licenses.hasPremiumAccess else {
            Task { @MainActor [weak self] in
                guard let self else { return }
                if await ActivationPrompts.activate(
                    licenses: licenses,
                    required: true,
                    statusMessage: "摸鱼模式可完整体验七天，激活后可以继续使用。"
                ) {
                    petController.refreshPremiumAccess()
                    settingsWindow?.refreshAccessState()
                    refreshMenuState()
                    openFishMode()
                }
            }
            return
        }
        if fakeAdWindow == nil { fakeAdWindow = FakeAdWindowController() }
        fakeAdWindow?.show()
    }

    private func showCompanionWindow() {
        if companionWindow == nil {
            companionWindow = CompanionWindowController(
                service: companions,
                sendCurrentGIF: { [weak self] in
                    guard let self else { return }
                    try await self.sendCurrentGIF()
                },
                currentGIFURL: { [weak self] in self?.petController.currentGIFURL },
                isActivated: { [weak self] in self?.licenses.isActivated == true },
                stateChanged: { [weak self] in self?.refreshMenuState() }
            )
        }
        companionWindow?.showWindow(nil)
    }

    @objc private func openHall() {
        guard remoteConfig.current.companionHall else { petController.showBubble("大厅暂时关闭，请稍后再来。"); return }
        requestPremiumAccess("桌宠大厅") { [weak self] in
            guard let self else { return }
            if hallWindow == nil {
                hallWindow = HallWindowController(
                    service: companions,
                    hasAccess: { [weak self] in self?.licenses.hasPremiumAccess == true },
                    isTrial: { [weak self] in self?.licenses.isTrialActive == true },
                    hallAvailable: { [weak self] in self?.remoteConfig.current.companionHall == true },
                    currentGIFURL: { [weak self] in self?.petController.currentGIFURL },
                    stateChanged: { [weak self] in self?.refreshMenuState() }
                )
            }
            hallWindow?.showWindow(nil)
        }
    }

    @objc private func toggleQuiet() {
        if petController.isQuiet { petController.resumeProactive() }
        else { petController.pauseProactiveForOneHour() }
        settingsWindow?.refreshAccessState()
        refreshMenuState()
    }

    @objc private func toggleDailySpeech() {
        petController.update { $0.dailySpeechEnabled.toggle() }
        settingsWindow?.refreshAccessState()
    }

    @objc private func stopCurrentScene() { petController.stopCurrentScene() }

    @objc private func replayUsageGuide() {
        let alert = NSAlert()
        alert.messageText = "桌搭子使用指南"
        alert.informativeText = "① 拖动移动桌宠，轻点获得回应；右键或菜单栏打开设置。\n\n② 更改通常自动保存。手动换一只、演一次不会打开自动播放。暂停打扰会暂停主动气泡、随机互动、自动剧场和来访；提醒与手动操作仍可用。\n\n③ 体验期间可以主动加入大厅，公开昵称并向在线用户发送 GIF。私人搭子需双方正式激活；激活码授权设备，搭子码用来找好友。\n\n④ 鼠标穿透后可用 Control+Shift+P 恢复。关闭设置不会退出，菜单“退出桌搭子”才会完全结束。"
        alert.addButton(withTitle: "知道了")
        alert.runModal()
    }

    @objc private func playGirlfriendVisit() { playTrialVisit(category: "girlfriend") }
    @objc private func playFriendVisit() { playTrialVisit(category: "friend") }
    @objc private func playCompanionVisit() { playTrialVisit(category: "companion") }

    private func playTrialVisit(category: String) {
        Task { @MainActor [weak self] in
            guard let self else { return }
            guard !petController.isTeaching else {
                presentFriendlyError(CompanionError.server("请先完成或关闭体验卡，再开始演示来访。"), title: "正在体验教学")
                return
            }
            guard remoteConfig.current.trialVisits else {
                petController.showBubble("体验来访暂时关掉了。")
                return
            }
            guard licenses.isTrialActive else {
                petController.showBubble("演示来访仅在有效体验期间提供。大厅与私人搭子请从对应入口进入。")
                return
            }
            guard !trialVisitBusy else {
                petController.showBubble("来访还在演，稍等一下。")
                return
            }
            trialVisitBusy = true
            defer { trialVisitBusy = false }
            do {
                let visit = try await companions.playTrialVisit(category: category)
                let completed = await petController.showVisitor(at: visit.fileURL, senderName: "演示来访 · \(visit.senderName)", message: visit.message, manual: true)
                if completed, !petController.isTeaching {
                    let alert = NSAlert()
                    alert.messageText = "刚才是软件模拟的来访"
                    alert.informativeText = "这不是真实用户消息。体验期可以去桌宠大厅；正式激活后可绑定私人搭子。打开大厅不会自动加入或发送。"
                    alert.addButton(withTitle: "知道了")
                    alert.runModal()
                }
                try? FileManager.default.removeItem(at: visit.fileURL)
            } catch {
                presentFriendlyError(error, title: "暂时叫不来")
            }
        }
    }

    @objc private func sendCompanionGIF() {
        Task { @MainActor [weak self] in
            guard let self else { return }
            do { try await sendCurrentGIF() }
            catch { presentFriendlyError(error, title: "发送失败") }
        }
    }

    private func sendCurrentGIF() async throws {
        guard licenses.isActivated else { throw LicenseError.inactive }
        guard companions.profile?.partner != nil else { throw CompanionError.server("请先绑定搭子") }
        guard let url = petController.currentGIFURL else { throw CompanionError.server("当前没有可发送的 GIF") }
        let recipient = try await companions.sendCurrentGIF(url)
        petController.showBubble("已经去找 \(recipient) 啦。")
    }

    @objc private func toggleVisibility(_ sender: NSMenuItem) {
        if petController.isVisible { petController.hide() } else { petController.show() }
        refreshMenuState()
    }

    @objc private func toggleMouseInteraction(_ sender: NSMenuItem) {
        petController.mouseInteractionEnabled.toggle()
        refreshMenuState()
    }

    @objc private func toggleRandomMovement(_ sender: NSMenuItem) {
        petController.randomMovementEnabled.toggle()
        refreshMenuState()
    }

    @objc private func toggleRandomInteractions(_ sender: NSMenuItem) {
        requestPremiumAccess("随机互动") { [weak self] in
            self?.petController.update { $0.randomInteractionsEnabled.toggle() }
            self?.refreshMenuState()
        }
    }

    @objc private func startRandomInteraction(_ sender: NSMenuItem) {
        requestPremiumAccess("互动内容") { [weak self] in self?.petController.startRandomInteraction() }
    }

    @objc private func toggleRandomPet(_ sender: NSMenuItem) {
        petController.randomPetEnabled.toggle()
        refreshMenuState()
    }

    @objc private func randomizePet(_ sender: NSMenuItem) {
        _ = petController.randomizePet()
    }

    @objc private func toggleTheater(_ sender: NSMenuItem) {
        requestPremiumAccess("小剧场") { [weak self] in
            self?.petController.update { $0.theaterEnabled.toggle() }
            self?.refreshMenuState()
        }
    }

    @objc private func startTheater(_ sender: NSMenuItem) {
        requestPremiumAccess("小剧场") { [weak self] in _ = self?.petController.startTheater() }
    }

    private func requestPremiumAccess(_ feature: String, action: @escaping () -> Void) {
        if licenses.hasPremiumAccess {
            action()
            return
        }
        Task { @MainActor [weak self] in
            guard let self else { return }
            if await ActivationPrompts.activate(
                licenses: licenses,
                required: true,
                statusMessage: "\(feature)可以在完整体验里接着用，桌宠会一直在。"
            ) {
                petController.refreshPremiumAccess()
                settingsWindow?.refreshAccessState()
                refreshMenuState()
                startCompanionPolling()
                action()
            }
        }
    }

    @objc private func toggleTopmost(_ sender: NSMenuItem) {
        petController.alwaysOnTop.toggle()
        refreshMenuState()
    }

    @objc private func toggleClickThrough(_ sender: NSMenuItem) {
        petController.clickThrough.toggle()
        refreshMenuState()
    }

    private func startReminderChecks() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
        reminderTimer?.invalidate()
        checkReminders()
        let timer = Timer(timeInterval: 15, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in self?.checkReminders() }
        }
        RunLoop.main.add(timer, forMode: .common)
        reminderTimer = timer
    }

    private func startCompanionPolling() {
        companionTimer?.invalidate()
        companionTimer = nil
        guard licenses.isActivated || licenses.isTrialActive else { return }
        Task { @MainActor [weak self] in
            guard let self else { return }
            _ = try? await companions.refreshProfile()
            refreshMenuState()
            await pollCompanion()
        }
        let timer = Timer(timeInterval: 4, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in await self?.pollCompanion() }
        }
        RunLoop.main.add(timer, forMode: .common)
        companionTimer = timer
    }

    private func pollCompanion() async {
        guard !companionPolling, licenses.hasPremiumAccess, petController.isVisible,
              !petController.isQuiet, !petController.clickThrough, !petController.isTeaching else { return }
        companionPolling = true
        defer { companionPolling = false }
        do {
            let visits = try await companions.receive()
            for visit in visits {
                guard await petController.showVisitor(
                    at: visit.fileURL, senderName: visit.senderName, message: visit.message,
                    canPresent: { [weak self] in
                        guard let self else { return false }
                        return licenses.hasPremiumAccess && companions.isCurrentAccountVisit(visit)
                    }
                ) else { break }
                companions.completeVisit(visit)
            }
        } catch {
            // Periodic polling retries without interrupting the desktop pet.
        }
    }

    private func checkReminders() {
        refreshMenuState()
        for reminder in petController.fireDueReminders() {
            petController.showReminder(reminder)
            let content = UNMutableNotificationContent()
            content.title = "桌搭子提醒"
            content.body = reminder.message
            content.sound = .default
            let request = UNNotificationRequest(identifier: reminder.id, content: content, trigger: nil)
            UNUserNotificationCenter.current().add(request)
        }
    }

    @objc private func quit() {
        NSApplication.shared.terminate(nil)
    }
}
