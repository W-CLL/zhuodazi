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
    private var statusItem: NSStatusItem!
    private var visibilityItem: NSMenuItem!
    private var mouseItem: NSMenuItem!
    private var movementItem: NSMenuItem!
    private var interactionItem: NSMenuItem!
    private var randomPetItem: NSMenuItem!
    private var randomizeNowItem: NSMenuItem!
    private var theaterItem: NSMenuItem!
    private var sendCompanionItem: NSMenuItem!
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
                    } catch { }
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
        petController.startInteractionServices()
        startCompanionPolling()
        startReminderChecks()
        startOnboardingIfNeeded()
        scheduleDemoVisitIfNeeded()
        analytics.trackStartup()
        Task { @MainActor [weak self] in
            guard let self else { return }
            guard settings.autoCheckUpdates else { return }
            try? await Task.sleep(for: .seconds(3))
            _ = try? await updates.check()
            if let manifest = updates.availableManifest, manifest.version != settings.ignoredUpdateVersion {
                petController.showBubble("发现新版本 v\(manifest.version)，可在设置中安装。")
            }
        }
    }

    private func startOnboardingIfNeeded() {
        guard !settings.onboardingHintSeen else { return }
        if licenses.isTrialActive, !settings.demoVisitSeen {
            petController.showBubble("先待一会儿，马上有人来串门。")
            return
        }
        settings.onboardingHintSeen = true
        settingsStore.save(settings)
        let steps = [
            "拖我、点我，右键还有更多。",
            "来点互动，或上演一小段小剧场。",
            "有搭子的话，打开设置里的「搭子」交换一对码。"
        ]
        for (index, step) in steps.enumerated() {
            DispatchQueue.main.asyncAfter(deadline: .now() + Double(index) * 4.2) { [weak self] in
                self?.petController.showBubble(step)
            }
        }
    }

    private func markOnboardingSeen() {
        guard !settings.onboardingHintSeen else { return }
        settings.onboardingHintSeen = true
        settingsStore.save(settings)
    }

    private func scheduleDemoVisitIfNeeded() {
        guard !settings.demoVisitSeen, licenses.isTrialActive else { return }
        Task { @MainActor [weak self] in
            try? await Task.sleep(for: .seconds(12))
            await self?.showDemoVisitIfNeeded()
        }
    }

    private func showDemoVisitIfNeeded() async {
        guard !settings.demoVisitSeen, licenses.isTrialActive else { return }
        guard petController.isVisible, !petController.isBusyWithScene else {
            scheduleDemoVisitIfNeeded()
            return
        }
        guard let url = petController.demoVisitGIFURL() else {
            settings.demoVisitSeen = true
            markOnboardingSeen()
            settingsStore.save(settings)
            return
        }
        settings.demoVisitSeen = true
        markOnboardingSeen()
        settingsStore.save(settings)
        await petController.showVisitor(at: url, senderName: "桌搭子")
        petController.showBubble("刚才那只是演示。想让对象也派一只过来，激活后换一对码。")
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
                } catch { }
                self.licenses.endTrial()
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
        menu.addItem(withTitle: "功能设置…", action: #selector(openSettings), keyEquivalent: ",")
        menu.addItem(withTitle: "检查更新", action: #selector(checkUpdates), keyEquivalent: "")
        menu.addItem(withTitle: "搭子联机…", action: #selector(openCompanion), keyEquivalent: "")
        fishModeItem = menu.addItem(withTitle: "摸鱼广告", action: #selector(openFishMode), keyEquivalent: "")
        sendCompanionItem = menu.addItem(withTitle: "发送当前 GIF 给搭子", action: #selector(sendCompanionGIF), keyEquivalent: "")
        girlfriendVisitItem = menu.addItem(withTitle: "模仿女友来访", action: #selector(playGirlfriendVisit), keyEquivalent: "")
        friendVisitItem = menu.addItem(withTitle: "模仿好友来访", action: #selector(playFriendVisit), keyEquivalent: "")
        companionVisitItem = menu.addItem(withTitle: "模仿搭子来访", action: #selector(playCompanionVisit), keyEquivalent: "")
        menu.addItem(.separator())
        visibilityItem = menu.addItem(withTitle: "隐藏桌搭子", action: #selector(toggleVisibility(_:)), keyEquivalent: "")
        mouseItem = menu.addItem(withTitle: "跟随鼠标", action: #selector(toggleMouseInteraction(_:)), keyEquivalent: "")
        movementItem = menu.addItem(withTitle: "随机移动", action: #selector(toggleRandomMovement(_:)), keyEquivalent: "")
        menu.addItem(.separator())
        interactionItem = menu.addItem(withTitle: "随机互动", action: #selector(toggleRandomInteractions(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "立即互动", action: #selector(startRandomInteraction(_:)), keyEquivalent: "")
        menu.addItem(.separator())
        randomPetItem = menu.addItem(withTitle: "自动随机换宠", action: #selector(toggleRandomPet(_:)), keyEquivalent: "")
        randomizeNowItem = menu.addItem(withTitle: "立即换一只", action: #selector(randomizePet(_:)), keyEquivalent: "")
        menu.addItem(.separator())
        theaterItem = menu.addItem(withTitle: "随机小剧场", action: #selector(toggleTheater(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "立即上演", action: #selector(startTheater(_:)), keyEquivalent: "")
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
        visibilityItem.title = petController.isVisible ? "隐藏桌搭子" : "显示桌搭子"
        mouseItem.state = petController.mouseInteractionEnabled ? .on : .off
        movementItem.state = petController.randomMovementEnabled ? .on : .off
        interactionItem.state = petController.currentSettings.randomInteractionsEnabled ? .on : .off
        interactionItem.title = licenses.hasPremiumAccess ? "随机互动" : "随机互动"
        randomPetItem.state = petController.randomPetEnabled ? .on : .off
        randomPetItem.isEnabled = petController.canRandomizePet
        randomizeNowItem.isEnabled = petController.canRandomizePet
        theaterItem.state = petController.currentSettings.theaterEnabled ? .on : .off
        theaterItem.title = licenses.hasPremiumAccess ? "随机小剧场" : "随机小剧场"
        fishModeItem.title = licenses.isActivated
            ? "摸鱼广告"
            : licenses.isTrialActive ? "摸鱼广告（体验中）" : "摸鱼广告（激活后继续）"
        fishModeItem.isEnabled = true
        let trial = licenses.isTrialActive
        girlfriendVisitItem.isHidden = !trial
        friendVisitItem.isHidden = !trial
        companionVisitItem.isHidden = !trial
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
                dockVisibilityChanged: { [weak self] visible in self?.applyDockVisibility(visible) },
                openCompanion: { [weak self] in self?.openCompanion() },
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

    @objc private func playGirlfriendVisit() { playTrialVisit(category: "girlfriend") }
    @objc private func playFriendVisit() { playTrialVisit(category: "friend") }
    @objc private func playCompanionVisit() { playTrialVisit(category: "companion") }

    private func playTrialVisit(category: String) {
        Task { @MainActor [weak self] in
            guard let self else { return }
            guard licenses.isTrialActive else {
                petController.showBubble("体验结束后，点一下发给对象才需要激活。")
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
                await petController.showVisitor(at: visit.fileURL, senderName: visit.senderName, message: visit.message)
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
        guard licenses.isActivated else { return }
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
        guard !companionPolling, licenses.isActivated, petController.isVisible else { return }
        companionPolling = true
        defer { companionPolling = false }
        do {
            for visit in try await companions.receive() {
                await petController.showVisitor(at: visit.fileURL, senderName: visit.senderName, message: visit.message)
                try? FileManager.default.removeItem(at: visit.fileURL)
            }
        } catch {
            // Periodic polling retries without interrupting the desktop pet.
        }
    }

    private func checkReminders() {
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
