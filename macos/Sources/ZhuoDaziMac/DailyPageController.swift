import AppKit
import ZhuoDaziCore

private final class DailyDocumentView: NSView {
    override var isFlipped: Bool { true }
}

/// Deliberately presents aggregates only: there is no individual history/edit/delete UI.
@MainActor
final class DailyPageController: NSViewController {
    private let pet: PetWindowController
    private let config: () -> RemoteConfig
    private let refreshConfig: () async -> Bool
    private let refreshConfigButton = NSButton(title: "同步总结设置", target: nil, action: nil)
    private let configStatus = NSTextField(wrappingLabelWithString: "")
    private let statsLabel = NSTextField(wrappingLabelWithString: "")
    private let moodsLabel = NSTextField(wrappingLabelWithString: "")
    private let statusLabel = NSTextField(wrappingLabelWithString: "")
    private let period = NSPopUpButton()
    private let calendarTitle = NSTextField(labelWithString: "")
    private let calendarStack = NSStackView()
    private let summaryKind = NSPopUpButton()
    private let summaryTitle = NSTextField(wrappingLabelWithString: "")
    private let summaryBody = NSTextField(wrappingLabelWithString: "")
    private let summaryRange = NSTextField(wrappingLabelWithString: "")
    private let nextSummaryButton = NSButton(title: "后一份", target: nil, action: nil)
    private let routineEnabled = NSButton(checkboxWithTitle: "在工作日提醒我下班", target: nil, action: nil)
    private let offWorkTime = NSTextField(string: "17:00")
    private var dayButtons: [NSButton] = []
    private var interactionButtons: [NSButton] = []
    private var routineSnapshot: DailyRoutine?
    private var month = Calendar.current.date(from: Calendar.current.dateComponents([.year, .month], from: Date()))!
    private var summaryOffset = 0
    private var timer: Timer?

    init(pet: PetWindowController, config: @escaping () -> RemoteConfig, refreshConfig: @escaping () async -> Bool) {
        self.pet = pet; self.config = config; self.refreshConfig = refreshConfig
        super.init(nibName: nil, bundle: nil)
        title = "我们的日常"
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    deinit { timer?.invalidate() }

    override func loadView() {
        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true; scroll.autohidesScrollers = true
        let content = DailyDocumentView(); content.translatesAutoresizingMaskIntoConstraints = false
        let stack = NSStackView(); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 16
        stack.translatesAutoresizingMaskIntoConstraints = false
        content.addSubview(stack); scroll.documentView = content
        NSLayoutConstraint.activate([
            content.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
            stack.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 24),
            stack.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -24),
            stack.topAnchor.constraint(equalTo: content.topAnchor, constant: 24),
            stack.bottomAnchor.constraint(equalTo: content.bottomAnchor, constant: -24)
        ])
        view = scroll
        stack.addArrangedSubview(heading("我和桌搭子的日常"))
        stack.addArrangedSubview(text("点选心情、回答题目和聊聊工作，都会留在本机，汇成我们的日常。"))
        interactionButtons = [
            button("记下心情", #selector(mood)), button("一起答题", #selector(quiz)),
            button("聊聊下班", #selector(offWork))
        ]
        stack.addArrangedSubview(row(interactionButtons))
        period.addItems(withTitles: ["本周实时统计", "本月实时统计", "全部统计"])
        period.target = self; period.action = #selector(periodChanged)
        stack.addArrangedSubview(period)
        stack.addArrangedSubview(statsLabel); stack.addArrangedSubview(moodsLabel)
        statusLabel.textColor = .secondaryLabelColor; stack.addArrangedSubview(statusLabel)
        stack.addArrangedSubview(heading("心情月历"))
        stack.addArrangedSubview(row([button("上个月", #selector(previousMonth)), calendarTitle, button("下个月", #selector(nextMonth))]))
        calendarStack.orientation = .vertical; calendarStack.spacing = 8; calendarStack.alignment = .leading
        stack.addArrangedSubview(calendarStack)
        stack.addArrangedSubview(text("每天显示当天最后一次记录的心情；没有记录的日期保留空白。"))
        stack.addArrangedSubview(heading("陪伴总结"))
        summaryKind.addItems(withTitles: ["周总结", "月总结"])
        summaryKind.target = self; summaryKind.action = #selector(summaryChanged)
        nextSummaryButton.target = self; nextSummaryButton.action = #selector(nextSummary)
        stack.addArrangedSubview(row([summaryKind, button("前一份", #selector(previousSummary)), nextSummaryButton, button("最近一期", #selector(latestSummary))]))
        summaryTitle.font = .systemFont(ofSize: 16, weight: .semibold)
        stack.addArrangedSubview(summaryRange); stack.addArrangedSubview(summaryTitle); stack.addArrangedSubview(summaryBody)
        refreshConfigButton.target = self; refreshConfigButton.action = #selector(syncConfig)
        stack.addArrangedSubview(row([refreshConfigButton, configStatus]))
        stack.addArrangedSubview(text("只整理已结束的周期。总结时间与文案跟随后台设置，定期联网更新。"))
        stack.addArrangedSubview(heading("工作与下班"))
        stack.addArrangedSubview(routineEnabled)
        offWorkTime.placeholderString = "HH:mm"; offWorkTime.widthAnchor.constraint(equalToConstant: 80).isActive = true
        stack.addArrangedSubview(row([text("平时下班时间"), offWorkTime]))
        dayButtons = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"].map { NSButton(checkboxWithTitle: $0, target: nil, action: nil) }
        stack.addArrangedSubview(row(dayButtons))
        stack.addArrangedSubview(button("保存作息", #selector(saveRoutine)))
        stack.addArrangedSubview(text("到点可以选择下班、加班、今天六点下班或休息。只有你选择延后，才会再提醒一次；关闭弹窗不会记成回答。"))
        [statsLabel, moodsLabel, statusLabel, summaryTitle, summaryRange, summaryBody].forEach {
            $0.maximumNumberOfLines = 0
            $0.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
        }
        refresh()
    }
    override func viewWillAppear() {
        super.viewWillAppear(); refresh()
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in self?.refresh() }
        }
    }
    override func viewDidDisappear() { super.viewDidDisappear(); timer?.invalidate(); timer = nil }

    func refresh() {
        guard isViewLoaded else { return }
        let now = Date(), calendar = Calendar.current
        let entries = pet.dailyEntries
        interactionButtons.forEach { $0.isEnabled = pet.hasPremiumAccess }
        let start: Date
        if period.indexOfSelectedItem == 0 {
            let days = (calendar.component(.weekday, from: now) + 5) % 7
            start = calendar.startOfDay(for: calendar.date(byAdding: .day, value: -days, to: now)!)
        } else if period.indexOfSelectedItem == 1 {
            start = calendar.date(from: calendar.dateComponents([.year, .month], from: now))!
        } else { start = .distantPast }
        let from = period.indexOfSelectedItem == 2 ? "" : DailyClock.stamp(start)
        let until = DailyClock.stamp(now)
        let stats = DailyStats(entries.filter { (period.indexOfSelectedItem == 2 || $0.localStamp >= from) && $0.localStamp <= until })
        statsLabel.stringValue = "互动 \(stats.total) 次 · 一起答题 \(stats.quizzes) 次 · 答对 \(stats.correct) 次 · 心情 \(stats.moodCount) 次"
        moodsLabel.stringValue = DailyMood.allCases.map { "\($0.label) \(stats.moods[$0.rawValue] ?? 0) 次" }.joined(separator: "　")
        statusLabel.stringValue = pet.dailyStorageError ?? (!pet.hasPremiumAccess
            ? "日常统计可以继续查看；有效体验或激活后可继续互动。"
            : entries.isEmpty ? "还没有日常数据，先一起聊聊吧。" : "累计相伴 \(DailyStats(entries).interactionDays) 天 · 答题 \(DailyStats(entries).quizzes) 次")
        refreshCalendar(entries)
        refreshSummary(entries)
        let routine = pet.currentSettings.dailyRoutine
        if routineSnapshot == nil || routineSnapshot?.enabled != routine.enabled || routineSnapshot?.time != routine.time || routineSnapshot?.weekdays != routine.weekdays {
            routineEnabled.state = routine.enabled ? .on : .off
            offWorkTime.stringValue = routine.time
            for (index, button) in dayButtons.enumerated() { button.state = routine.weekdays.contains(index) ? .on : .off }
            routineSnapshot = routine
        }
    }
    private func refreshCalendar(_ entries: [DailyEntry]) {
        calendarStack.arrangedSubviews.forEach { calendarStack.removeArrangedSubview($0); $0.removeFromSuperview() }
        let calendar = Calendar.current
        calendarTitle.stringValue = String(DailyClock.stamp(month).prefix(7))
        let moods = Dictionary(grouping: entries.filter { $0.kind == "mood" }, by: \.localDay)
        func cells(_ strings: [String]) -> NSStackView {
            row(strings.map { value in
                let field = text(value); field.alignment = .center; field.font = .systemFont(ofSize: 11)
                field.widthAnchor.constraint(equalToConstant: 85).isActive = true
                field.heightAnchor.constraint(equalToConstant: 40).isActive = true
                return field
            })
        }
        calendarStack.addArrangedSubview(cells(["一", "二", "三", "四", "五", "六", "日"]))
        let padding = (calendar.component(.weekday, from: month) + 5) % 7
        var values = Array(repeating: "", count: padding)
        for day in calendar.range(of: .day, in: .month, for: month)! {
            let date = calendar.date(byAdding: .day, value: day - 1, to: month)!
            let key = String(DailyClock.stamp(date).prefix(10))
            let latest = moods[key]?.max { $0.occurredAt < $1.occurredAt }
            let label = latest.flatMap { DailyMood(rawValue: $0.mood ?? "")?.label } ?? ""
            values.append("\(day)\n\(label)")
        }
        while values.count % 7 != 0 { values.append("") }
        for offset in stride(from: 0, to: values.count, by: 7) { calendarStack.addArrangedSubview(cells(Array(values[offset..<offset+7]))) }
    }
    private func refreshSummary(_ entries: [DailyEntry]) {
        let kind = summaryKind.indexOfSelectedItem == 1 ? "monthly" : "weekly"
        let result = DailySummary.build(config: config().dailySummaries, entries: entries, kind: kind, offset: summaryOffset)
        nextSummaryButton.isEnabled = summaryOffset > 0
        summaryTitle.stringValue = result.available ? result.title : result.status
        summaryBody.stringValue = result.available ? result.body : ""
        var range = ""
        if let start = result.start, let end = result.end {
            let finish = kind == "monthly" ? end.addingTimeInterval(-1) : end
            range = "\(DailyClock.stamp(start).prefix(kind == "monthly" ? 10 : 16)) — \(DailyClock.stamp(finish).prefix(kind == "monthly" ? 10 : 16))"
        }
        if let next = result.nextAvailable { range += "\n下一期：\(DailyClock.stamp(next).prefix(16))" }
        summaryRange.stringValue = range
    }
    private func heading(_ value: String) -> NSTextField { let field = text(value); field.font = .systemFont(ofSize: 17, weight: .semibold); return field }
    private func text(_ value: String) -> NSTextField { let field = NSTextField(wrappingLabelWithString: value); field.maximumNumberOfLines = 0; return field }
    private func row(_ views: [NSView]) -> NSStackView { let row = NSStackView(views: views); row.orientation = .horizontal; row.spacing = 8; row.alignment = .centerY; return row }
    private func button(_ title: String, _ action: Selector) -> NSButton { let button = NSButton(title: title, target: self, action: action); button.bezelStyle = .rounded; return button }
    @objc private func mood() { pet.startDailyMood() }
    @objc private func quiz() { pet.startDailyQuiz() }
    @objc private func offWork() { pet.startDailyOffWork() }
    @objc private func periodChanged() { refresh() }
    @objc private func previousMonth() { month = Calendar.current.date(byAdding: .month, value: -1, to: month)!; refresh() }
    @objc private func nextMonth() {
        let next = Calendar.current.date(byAdding: .month, value: 1, to: month)!
        if next <= Date() { month = next; refresh() }
    }
    @objc private func summaryChanged() { summaryOffset = 0; refresh() }
    @objc private func previousSummary() { summaryOffset = min(1200, summaryOffset + 1); refresh() }
    @objc private func nextSummary() { summaryOffset = max(0, summaryOffset - 1); refresh() }
    @objc private func latestSummary() { summaryOffset = 0; refresh() }
    @objc private func syncConfig() {
        refreshConfigButton.isEnabled = false; configStatus.stringValue = "正在同步…"
        Task { @MainActor [weak self] in
            guard let self else { return }
            let success = await refreshConfig()
            refreshConfigButton.isEnabled = true
            configStatus.stringValue = success ? "已同步后台设置" : "暂时未能同步，保留上次可用设置。"
            refresh()
        }
    }
    @objc private func saveRoutine() {
        let time = offWorkTime.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        let days = dayButtons.enumerated().compactMap { $0.element.state == .on ? $0.offset : nil }
        guard DailyClock.time(time) != nil, routineEnabled.state != .on || !days.isEmpty else {
            let alert = NSAlert(); alert.messageText = "请填写 00:00–23:59 的时间，并选择工作日。"; alert.runModal(); return
        }
        pet.update { settings in
            settings.dailyRoutine.enabled = routineEnabled.state == .on
            settings.dailyRoutine.time = time; settings.dailyRoutine.weekdays = days
            settings.dailyRoutine.snoozedUntil = nil
        }
        refresh()
    }
}
