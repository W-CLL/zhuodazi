import AppKit
import ZhuoDaziCore

@MainActor
final class GuideWindowController: NSWindowController, NSWindowDelegate {
    private let pet: PetWindowController
    private let openSettings: () -> Void
    private let openHall: () -> Void
    private let playInteraction: () -> Void
    private let playTheater: () -> Void
    private var busy = false
    private var statusText = ""
    private var generation = 0

    init(pet: PetWindowController, openSettings: @escaping () -> Void, openHall: @escaping () -> Void,
         playInteraction: @escaping () -> Void, playTheater: @escaping () -> Void) {
        self.pet = pet
        self.openSettings = openSettings
        self.openHall = openHall
        self.playInteraction = playInteraction
        self.playTheater = playTheater
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 650, height: 610),
                              styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        window.title = "和桌搭子玩一分钟"
        window.minSize = NSSize(width: 600, height: 540)
        window.isReleasedWhenClosed = false
        window.center()
        super.init(window: window)
        window.delegate = self
        pet.guideControlsUsed = { [weak self] in
            guard let self, self.window?.isVisible == true, self.pet.currentSettings.guide.step == 1 else { return }
            completeCurrentStep()
        }
        pet.guideInterrupted = { [weak self] in self?.handleInterruption() }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func showWindow(_ sender: Any?) {
        if !pet.currentSettings.guide.isDone {
            pet.update { $0.guide.dismissed = false }
            pet.beginTeaching()
        }
        render()
        super.showWindow(sender)
        window?.makeKeyAndOrderFront(sender)
        NSApp.activate(ignoringOtherApps: true)
    }

    func windowWillClose(_ notification: Notification) {
        generation += 1
        busy = false
        pet.update { $0.guide.dismissed = true }
        pet.endTeaching()
    }

    private func handleInterruption() {
        guard window?.isVisible == true else { return }
        generation += 1
        busy = false
        statusText = pet.isVisible ? "当前演示已结束，进度已保留。手动演示仍可开始；也可以稍后继续。" : "桌宠已隐藏，进度已保留。点“显示桌宠”后可继续。"
        render()
    }

    private func render() {
        guard let window else { return }
        let progress = pet.currentSettings.guide
        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.drawsBackground = false
        let document = GuideDocumentView()
        document.translatesAutoresizingMaskIntoConstraints = false
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 18
        stack.translatesAutoresizingMaskIntoConstraints = false
        document.addSubview(stack)
        scroll.documentView = document
        window.contentView = scroll
        NSLayoutConstraint.activate([
            document.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
            stack.leadingAnchor.constraint(equalTo: document.leadingAnchor, constant: 28),
            stack.trailingAnchor.constraint(equalTo: document.trailingAnchor, constant: -28),
            stack.topAnchor.constraint(equalTo: document.topAnchor, constant: 26),
            stack.bottomAnchor.constraint(equalTo: document.bottomAnchor, constant: -26)
        ])
        addText(progress.step == 0 ? "欢迎来到桌角" : progress.isDone ? "体验已结束，随时可以再来" : "第 \(progress.step) 步 / 4", to: stack, size: 13, secondary: true)
        let titles = ["我已经到你的屏幕角落啦", "认识这只桌宠", "让它回应你", "看一场小剧场", "学会安静，也能找回", "接下来，想玩点什么？"]
        addText(titles[progress.step], to: stack, size: 25, weight: .bold)
        let descriptions = [
            "花一分钟，一起玩点什么？四步体验都可以跳过，关闭后会保留进度。\n\n教学时我会暂时停下自动走动、换形象和主动打扰，结束后恢复你原来的设置。",
            "拖动桌宠可以换位置；右键或 macOS 菜单栏能找到设置和玩法。\n\n试着移动一下，或点“下一步”。关闭设置窗口不会退出应用，完全退出请用菜单“退出桌搭子”。",
            "我会带来问候、笑话和小问题。现在先试一次明确的回应。\n\n这是不需要网络的本地演示，不会计入日常答题或心情记录。显示后选择一个回应，或关闭演示框，才能进入下一步。",
            "两只小搭子会演一段对白，不需要邀请真实好友，也不用先导入剧本。\n\n接下来是约 12 秒的小短剧，不用联网，可以随时结束。试看不会开启自动上演；日后可从菜单“看一场小剧场”继续看。",
            "日常台词：自动移动、换宠时偶尔说一句。\n趣味互动：问候、笑话和小问题；主动互动关闭后仍可手动玩。\n自动剧场：按单独设置的间隔演出，默认关闭。\n\n“暂停打扰 1 小时”是临时状态，提醒与手动操作仍可用；互动频率“安静”只是减少次数。\n\n隐藏后从菜单栏“显示桌搭子”找回；鼠标穿透可按 Control+Shift+P 恢复。无需现在真的暂停一小时。",
            "体验已结束，原来的自动行为已经恢复。以后可以在设置继续玩，也可以重新体验这四步。\n\n大厅只是可选探索：打开不会自动加入或发送。有效体验期间可以加入大厅；私人搭子需要正式激活。"
        ]
        addText(descriptions[progress.step], to: stack, size: 14)

        if progress.isDone {
            addButtons([button("再互动一下", #selector(openInteractionAction)), button("去看小剧场", #selector(openTheaterAction)), button("看看桌宠大厅", #selector(openHallAction))], to: stack)
            addButtons([button("重新体验", #selector(replay)), button("完成", #selector(dismissGuide))], to: stack)
        } else {
            let primaryTitles = ["带我体验一下", "下一步", "让它回应我", "看一小段", "知道了"]
            let primary = button(primaryTitles[progress.step], #selector(primaryAction))
            primary.isEnabled = !busy
            primary.keyEquivalent = "\r"
            let skip = button(progress.step == 0 ? "先自己玩" : "跳过这一步", #selector(skipAction))
            addButtons([primary, skip, button("稍后体验", #selector(dismissGuide))], to: stack)
            if busy { addButtons([button("结束当前演示", #selector(stopDemo))], to: stack) }
            if progress.step > 0 {
                addButtons([button("显示桌宠", #selector(showPet)), button("先停一下", #selector(stopScene)), button("打开设置", #selector(settingsAction))], to: stack)
            }
            addText("进度自动保存在这台设备。稍后可从菜单或设置“继续体验”回来；文字使用指南始终保留。", to: stack, size: 12, secondary: true)
        }
        if !statusText.isEmpty { addText(statusText, to: stack, size: 13, secondary: true) }
    }

    private func addText(_ text: String, to stack: NSStackView, size: CGFloat, weight: NSFont.Weight = .regular, secondary: Bool = false) {
        let label = NSTextField(wrappingLabelWithString: text)
        label.font = .systemFont(ofSize: size, weight: weight)
        label.textColor = secondary ? .secondaryLabelColor : .labelColor
        label.maximumNumberOfLines = 0
        label.isSelectable = true
        stack.addArrangedSubview(label)
        label.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
    }

    private func button(_ title: String, _ action: Selector) -> NSButton {
        let button = NSButton(title: title, target: self, action: action)
        button.bezelStyle = .rounded
        button.controlSize = .large
        return button
    }

    private func addButtons(_ buttons: [NSButton], to stack: NSStackView) {
        let row = NSStackView(views: buttons)
        row.orientation = .horizontal
        row.spacing = 10
        row.alignment = .centerY
        stack.addArrangedSubview(row)
    }

    @objc private func primaryAction() {
        guard !busy else { return }
        switch pet.currentSettings.guide.step {
        case 0:
            pet.update { $0.guide.resume() }
            statusText = ""
            render()
        case 1, 4: completeCurrentStep()
        case 2: runInteractionDemo()
        case 3: runTheaterDemo()
        default: break
        }
    }

    private func completeCurrentStep() {
        let step = pet.currentSettings.guide.step
        pet.update { $0.guide.recordCompletion(step); $0.guide.advance() }
        statusText = ""
        if pet.currentSettings.guide.isDone { pet.endTeaching() }
        render()
    }

    private func runInteractionDemo() {
        if let reason = pet.guideAvailabilityMessage { statusText = reason; render(); return }
        generation += 1
        let token = generation
        busy = true
        let started = pet.startGuideInteraction { [weak self] completed in
            guard let self, token == generation else { return }
            busy = false
            if completed { completeCurrentStep() }
            else { statusText = "演示已中断，当前步骤保留。可以重新体验或跳过。"; render() }
        }
        if !started { busy = false; statusText = "演示还没有显示。请先显示桌宠，等它忙完眼前的事后再试一次。" }
        else { statusText = "请在桌宠旁的“初次见面”卡片里回应，或关闭卡片。" }
        render()
    }

    private func runTheaterDemo() {
        if let reason = pet.guideAvailabilityMessage { statusText = reason; render(); return }
        generation += 1
        let token = generation
        busy = true
        let started = pet.startGuideTheater { [weak self] completed in
            guard let self, token == generation else { return }
            busy = false
            if completed { completeCurrentStep() }
            else { statusText = "短剧已中断，当前步骤保留。可以重新观看或明确跳过。"; render() }
        }
        if started {
            statusText = "小短剧正在桌宠旁上演，约 12 秒。可以随时结束。"
        } else {
            busy = false
            statusText = "短剧还没开始。等桌宠准备好、忙完眼前的事后，再点一次。"
        }
        render()
    }

    @objc private func skipAction() {
        if pet.currentSettings.guide.step == 0 { dismissGuide(); return }
        generation += 1
        busy = false
        pet.stopGuideDemonstration()
        pet.update { $0.guide.advance(skipping: true) }
        statusText = ""
        if pet.currentSettings.guide.isDone { pet.endTeaching() }
        render()
    }

    @objc private func dismissGuide() { close() }
    @objc private func stopDemo() { pet.stopGuideDemonstration() }
    @objc private func showPet() { pet.show(); statusText = "桌宠已显示，可以继续体验。"; render() }
    @objc private func stopScene() { pet.stopCurrentScene(); statusText = "正在停下眼前的活动，结束后可以继续体验。"; render() }
    @objc private func settingsAction() {
        if pet.currentSettings.guide.step == 1 || pet.currentSettings.guide.step == 4 { completeCurrentStep() }
        openSettings()
    }
    @objc private func replay() {
        generation += 1
        busy = false
        pet.update { $0.guide.replay() }
        pet.beginTeaching()
        statusText = ""
        render()
    }
    @objc private func openInteractionAction() { close(); playInteraction() }
    @objc private func openTheaterAction() { close(); playTheater() }
    @objc private func openHallAction() { close(); openHall() }
}

private final class GuideDocumentView: NSView {
    override var isFlipped: Bool { true }
}
