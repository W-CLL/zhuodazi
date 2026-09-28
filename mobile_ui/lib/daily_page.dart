part of 'main.dart';

const dailyMoods = <String, String>{
  'happy': '开心',
  'okay': '一般',
  'bad': '糟糕',
  'cry': '想哭',
  'tired': '疲惫',
  'annoyed': '烦躁',
  'calm': '平静',
  'hopeful': '有盼头',
};

class DailyPage extends StatefulWidget {
  const DailyPage({required this.controller, super.key});
  final AppController controller;

  @override
  State<DailyPage> createState() => _DailyPageState();
}

class _DailyPageState extends State<DailyPage> with WidgetsBindingObserver {
  Map<String, dynamic> _data = {};
  String _period = 'month';
  DateTime _month = DateTime(DateTime.now().year, DateTime.now().month);
  int _weeklyOffset = 0;
  int _monthlyOffset = 0;
  int _request = 0;
  bool _loading = true;
  String? _error;
  Timer? _clock;
  bool _routineLoaded = false;
  bool _routineEnabled = false;
  Set<int> _workdays = {1, 2, 3, 4, 5};
  TimeOfDay _offWork = const TimeOfDay(hour: 17, minute: 0);

  Map<String, dynamic> _map(Object? value) =>
      value is Map ? Map<String, dynamic>.from(value) : <String, dynamic>{};
  int _number(Object? value) => (value as num?)?.toInt() ?? 0;
  String _date(DateTime day) =>
      '${day.year}-${day.month.toString().padLeft(2, '0')}-${day.day.toString().padLeft(2, '0')}';
  String get _monthKey =>
      '${_month.year}-${_month.month.toString().padLeft(2, '0')}';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(_load());
    _clock = Timer.periodic(const Duration(seconds: 20), (_) {
      if (mounted &&
          !widget.controller.busy &&
          !_loading &&
          ModalRoute.of(context)?.isCurrent == true) {
        unawaited(_load(silent: true));
      }
    });
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(_load(silent: true));
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _clock?.cancel();
    super.dispose();
  }

  Future<void> _load({bool silent = false}) async {
    final request = ++_request;
    if (!silent && mounted) {
      setState(() {
        _loading = true;
        _error = null;
      });
    }
    try {
      final next = await widget.controller.dailySnapshot(
        period: _period,
        month: _monthKey,
        weeklyOffset: _weeklyOffset,
        monthlyOffset: _monthlyOffset,
      );
      if (!mounted || request != _request) return;
      setState(() {
        _data = next;
        _error = null;
        if (!_routineLoaded) {
          final routine = _map(next['routine']);
          _routineEnabled = routine['enabled'] == true;
          final days = routine['workdays'];
          if (days is List) {
            _workdays = days.whereType<num>().map((day) => day.toInt()).toSet();
          }
          final time = '${routine['time'] ?? '17:00'}'.split(':');
          _offWork = TimeOfDay(
            hour: int.tryParse(time.first) ?? 17,
            minute: time.length == 2 ? int.tryParse(time.last) ?? 0 : 0,
          );
          _routineLoaded = true;
        }
      });
    } catch (error) {
      if (mounted && request == _request) {
        setState(() => _error = readableHostError(error));
      }
    } finally {
      if (mounted && request == _request) setState(() => _loading = false);
    }
  }

  Future<void> _refresh() async {
    await runAction(
      context,
      widget.controller.refreshDailyConfig(),
      (_) => '日常已刷新',
    );
    if (mounted) await _load();
  }

  Future<String?> _choose(String title, Map<String, String> options) =>
      showModalBottomSheet<String>(
        context: context,
        isScrollControlled: true,
        showDragHandle: true,
        builder: (context) => SafeArea(
          child: SingleChildScrollView(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(20, 4, 20, 24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(title, style: Theme.of(context).textTheme.titleLarge),
                  const SizedBox(height: 12),
                  for (final option in options.entries)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 6),
                      child: OutlinedButton(
                        onPressed: () => Navigator.pop(context, option.key),
                        child: Text(option.value),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
      );

  Future<void> _mood() async {
    final mood = await _choose('此刻，心情怎么样？', dailyMoods);
    if (!mounted || mood == null) return;
    await runAction(
      context,
      widget.controller.recordDailyMood(mood),
      (_) => switch (mood) {
        'happy' => '收好这份开心，我也跟着高兴。',
        'cry' => '想哭也没关系，我陪你缓一缓。',
        'bad' => '今天不太容易，先对自己温柔一点。',
        'tired' => '辛苦啦，可以给自己留一点休息时间。',
        'annoyed' => '先喘口气，烦心事可以慢慢理。',
        'hopeful' => '有个期待真好，我陪你一起等。',
        'calm' => '平静的这一刻，也值得收好。',
        _ => '普通的一天，也有人陪你。',
      },
    );
    if (mounted) await _load(silent: true);
  }

  Future<void> _workday() async {
    final choice = await _choose('今天准备什么时候收工？', const {
      'done': '下班啦',
      'overtime': '还得加会班',
      'six': '今天六点下班',
      'rest': '今天休息',
    });
    if (!mounted || choice == null) return;
    int minutes = 0;
    if (choice == 'overtime') {
      final snooze = await _choose('要过一会儿再提醒你吗？', const {
        '30': '30 分钟后再提醒',
        '60': '1 小时后再提醒',
        '0': '今天不用再提醒',
      });
      if (!mounted) return;
      minutes = int.tryParse(snooze ?? '0') ?? 0;
    }
    await runAction(
      context,
      widget.controller.recordDailyWorkday(choice, snoozeMinutes: minutes),
      (_) => choice == 'done'
          ? '收工啦，今天辛苦了。'
          : choice == 'rest'
          ? '好好休息，今天不催你。'
          : '记住啦，也别忘了照顾自己。',
    );
    if (mounted) await _load(silent: true);
  }

  void _moveMonth(int direction) {
    final next = DateTime(_month.year, _month.month + direction);
    final today = DateTime.now();
    if (next.isAfter(DateTime(today.year, today.month))) return;
    setState(() => _month = next);
    unawaited(_load());
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: widget.controller,
    builder: (context, _) {
      final controller = widget.controller;
      final stats = _map(_data['stats']);
      final moods = _map(stats['moodCounts']);
      final canAnswer = controller.snapshot.premium && !controller.busy;
      return Scaffold(
        appBar: AppBar(
          title: const Text('我们的日常'),
          actions: [
            IconButton(
              tooltip: '刷新日常',
              onPressed: _loading || controller.busy ? null : _refresh,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        body: SafeArea(
          child: PageScroll(
            onRefresh: _refresh,
            children: [
              const Text('把心情、答过的题和收工时刻，收进我们的小日常。'),
              const SizedBox(height: 14),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  FilledButton.icon(
                    onPressed: canAnswer ? _mood : null,
                    icon: const Icon(Icons.mood),
                    label: const Text('记一下心情'),
                  ),
                  OutlinedButton(
                    onPressed: canAnswer ? _workday : null,
                    child: const Text('聊聊下班'),
                  ),
                  OutlinedButton(
                    onPressed:
                        canAnswer &&
                            !controller.snapshot.interactionBusy &&
                            !controller.snapshot.theaterActive
                        ? () => runAction(
                            context,
                            controller.startDailyQuiz(),
                            (_) => '',
                          )
                        : null,
                    child: const Text('来一道互动题'),
                  ),
                ],
              ),
              if (!controller.snapshot.premium) ...[
                const SizedBox(height: 8),
                const Text('体验或激活后可以继续互动；已有统计仍可查看。'),
              ],
              const SizedBox(height: 12),
              const Text(
                '回答后才计数。日常保存在本机，从使用此版本开始积累。',
                style: TextStyle(color: _muted, fontSize: 12),
              ),
              if (_loading)
                const Padding(
                  padding: EdgeInsets.symmetric(vertical: 12),
                  child: LinearProgressIndicator(),
                ),
              if (_error != null) ...[
                const SizedBox(height: 12),
                Panel(
                  color: _coralSoft,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(_error!),
                      TextButton(
                        onPressed: () => _load(),
                        child: const Text('重试'),
                      ),
                    ],
                  ),
                ),
              ],
              if ('${_data['storageWarning'] ?? ''}'.isNotEmpty) ...[
                const SizedBox(height: 12),
                Panel(
                  color: _coralSoft,
                  child: Text('${_data['storageWarning']}'),
                ),
              ],
              const SizedBox(height: 18),
              Panel(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Wrap(
                      spacing: 8,
                      children: [
                        for (final option in const {
                          'week': '本周',
                          'month': '按月',
                          'all': '全部',
                        }.entries)
                          ChoiceChip(
                            label: Text(option.value),
                            selected: _period == option.key,
                            onSelected: (_) {
                              setState(() => _period = option.key);
                              unawaited(_load());
                            },
                          ),
                      ],
                    ),
                    const SizedBox(height: 10),
                    if (stats['rangeStart'] != null)
                      Text(
                        '${stats['rangeStart']} — ${stats['rangeEnd']}',
                        style: const TextStyle(color: _muted, fontSize: 12),
                      ),
                    const SizedBox(height: 12),
                    LayoutBuilder(
                      builder: (context, constraints) => Wrap(
                        spacing: 10,
                        runSpacing: 10,
                        children: [
                          for (final metric in const {
                            'totalInteractions': '总互动',
                            'quizzesAnswered': '答题总数',
                            'quizzesCorrect': '答对',
                            'moodRecords': '心情次数',
                            'moodDays': '记录心情的天数',
                            'interactionDays': '一起互动的天数',
                          }.entries)
                            SizedBox(
                              width: (constraints.maxWidth - 10) / 2,
                              child: Container(
                                padding: const EdgeInsets.all(12),
                                decoration: BoxDecoration(
                                  color: _page,
                                  borderRadius: BorderRadius.circular(8),
                                ),
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      metric.value,
                                      style: const TextStyle(
                                        color: _muted,
                                        fontSize: 12,
                                      ),
                                    ),
                                    const SizedBox(height: 5),
                                    Text(
                                      '${_number(stats[metric.key])} ${metric.key.endsWith('Days') ? '天' : '次'}',
                                      style: const TextStyle(
                                        fontSize: 22,
                                        fontWeight: FontWeight.w700,
                                        color: _brandDark,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 16),
                    const Text(
                      '这些时刻的心情',
                      style: TextStyle(fontWeight: FontWeight.w700),
                    ),
                    const SizedBox(height: 8),
                    Wrap(
                      spacing: 7,
                      runSpacing: 7,
                      children: [
                        for (final mood in dailyMoods.entries)
                          Chip(
                            label: Text(
                              '${mood.value} ${_number(moods[mood.key])} 次',
                            ),
                            backgroundColor: _moodColor(mood.key),
                            side: BorderSide.none,
                            visualDensity: VisualDensity.compact,
                          ),
                        if (_number(moods['low']) > 0)
                          Chip(label: Text('低落 ${_number(moods['low'])} 次')),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 16),
              _calendar(),
              const SizedBox(height: 16),
              _summary('weekly', '周总结', _weeklyOffset),
              const SizedBox(height: 16),
              _summary('monthly', '月总结', _monthlyOffset),
              const SizedBox(height: 16),
              _routine(),
            ],
          ),
        ),
      );
    },
  );

  Color _moodColor(String? code) => switch (code) {
    'happy' => const Color(0xfffff0c9),
    'hopeful' => const Color(0xfff6e4d3),
    'calm' => const Color(0xffdcefe9),
    'cry' => const Color(0xffdde8f4),
    'tired' => const Color(0xffe9e4f3),
    'annoyed' => const Color(0xfff7e2dd),
    'bad' || 'low' => const Color(0xffe7e6ef),
    _ => _page,
  };

  Widget _calendar() {
    final days = {
      for (final item in (_data['calendar'] as List? ?? []).whereType<Map>())
        '${item['date']}': item,
    };
    final first = DateTime(_month.year, _month.month, 2 - _month.weekday);
    final today = DateTime.now();
    return Panel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SectionTitle(label: '心情月历'),
          Row(
            children: [
              IconButton(
                tooltip: '上个月',
                onPressed: _loading ? null : () => _moveMonth(-1),
                icon: const Icon(Icons.chevron_left),
              ),
              Expanded(
                child: Text(
                  '${_month.year} 年 ${_month.month} 月',
                  textAlign: TextAlign.center,
                ),
              ),
              IconButton(
                tooltip: '下个月',
                onPressed:
                    !_loading &&
                        _month.isBefore(DateTime(today.year, today.month))
                    ? () => _moveMonth(1)
                    : null,
                icon: const Icon(Icons.chevron_right),
              ),
            ],
          ),
          Table(
            children: [
              TableRow(
                children: [
                  for (final day in ['一', '二', '三', '四', '五', '六', '日'])
                    Padding(
                      padding: const EdgeInsets.only(bottom: 8),
                      child: Text(
                        day,
                        textAlign: TextAlign.center,
                        style: const TextStyle(color: _muted),
                      ),
                    ),
                ],
              ),
              for (int row = 0; row < 6; row++)
                TableRow(
                  children: [
                    for (int column = 0; column < 7; column++)
                      Builder(
                        builder: (context) {
                          final day = DateTime(
                            first.year,
                            first.month,
                            first.day + row * 7 + column,
                          );
                          final mood = days[_date(day)]?['mood'] as String?;
                          final active =
                              day.month == _month.month && !day.isAfter(today);
                          return Opacity(
                            opacity: active ? 1 : 0.35,
                            child: Container(
                              height: 57,
                              margin: const EdgeInsets.all(1),
                              decoration: BoxDecoration(
                                color: _moodColor(mood),
                                borderRadius: BorderRadius.circular(5),
                              ),
                              child: Column(
                                mainAxisAlignment: MainAxisAlignment.center,
                                children: [
                                  Text(
                                    '${day.day}',
                                    style: TextStyle(
                                      fontWeight: _date(day) == _date(today)
                                          ? FontWeight.bold
                                          : FontWeight.normal,
                                    ),
                                  ),
                                  const SizedBox(height: 3),
                                  Text(
                                    dailyMoods[mood] ??
                                        (mood == 'low' ? '低落' : ''),
                                    style: const TextStyle(fontSize: 10),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ],
                              ),
                            ),
                          );
                        },
                      ),
                  ],
                ),
            ],
          ),
          const SizedBox(height: 10),
          const Text(
            '显示当天最后一次心情，空白代表没有记录。',
            style: TextStyle(color: _muted, fontSize: 12),
          ),
        ],
      ),
    );
  }

  String _summaryDate(
    Object? value, {
    bool time = false,
    bool endOfMonth = false,
  }) {
    var date = DateTime.tryParse('$value')?.toLocal();
    if (date == null) return '';
    if (endOfMonth) date = DateTime(date.year, date.month, date.day - 1);
    return '${_date(date)}${time ? ' ${date.hour.toString().padLeft(2, '0')}:${date.minute.toString().padLeft(2, '0')}' : ''}';
  }

  Widget _summary(String kind, String title, int offset) {
    final value = _map(_data[kind]);
    final ready = value['state'] == 'ready';
    final weekly = kind == 'weekly';
    final from = _summaryDate(value['periodStart'], time: weekly);
    final to = _summaryDate(
      value['periodEnd'],
      time: weekly,
      endOfMonth: !weekly,
    );
    final next = _summaryDate(value['nextDue'], time: true);
    void move(int target) {
      setState(() {
        if (weekly) {
          _weeklyOffset = target;
        } else {
          _monthlyOffset = target;
        }
      });
      unawaited(_load());
    }

    return Panel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SectionTitle(label: title),
          Wrap(
            spacing: 6,
            children: [
              TextButton(
                onPressed: _loading ? null : () => move(offset + 1),
                child: const Text('上一期'),
              ),
              TextButton(
                onPressed: _loading || offset == 0
                    ? null
                    : () => move(offset - 1),
                child: const Text('下一期'),
              ),
              TextButton(
                onPressed: _loading || offset == 0 ? null : () => move(0),
                child: const Text('最近一期'),
              ),
            ],
          ),
          if (from.isNotEmpty)
            Text(
              '$from — $to · 已结束',
              style: const TextStyle(color: _muted, fontSize: 12),
            ),
          const SizedBox(height: 10),
          if (ready) ...[
            Text(
              '${value['title'] ?? ''}',
              style: const TextStyle(
                fontSize: 16,
                fontWeight: FontWeight.w700,
                color: _brandDark,
              ),
            ),
            const SizedBox(height: 8),
            SelectableText(
              '${value['body'] ?? ''}',
              style: const TextStyle(height: 1.6),
            ),
          ] else
            Text(
              '${value['message'] ?? switch (value['state']) {
                    'disabled' => '暂未开启$title。',
                    'waiting' => '这个已结束的周期还没有日常记录。',
                    _ => '总结尚未配置，联网刷新后再来看看。',
                  }}',
            ),
          if (next.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 12),
              child: Text(
                '下一期：$next 后可查看',
                style: const TextStyle(color: _muted, fontSize: 12),
              ),
            ),
          const SizedBox(height: 8),
          Text(
            weekly ? '按每周收尾时间结算完整 7 天。' : '完整月份结束后，再收好这一月的回忆。',
            style: const TextStyle(color: _muted, fontSize: 12),
          ),
        ],
      ),
    );
  }

  Widget _routine() => Panel(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SectionTitle(label: '你的工作节奏'),
        SwitchListTile.adaptive(
          contentPadding: EdgeInsets.zero,
          title: const Text('下班时来关心我'),
          value: _routineEnabled,
          onChanged: (value) => setState(() => _routineEnabled = value),
        ),
        Wrap(
          spacing: 5,
          runSpacing: 5,
          children: [
            for (final day in const {
              1: '周一',
              2: '周二',
              3: '周三',
              4: '周四',
              5: '周五',
              6: '周六',
              0: '周日',
            }.entries)
              FilterChip(
                label: Text(day.value),
                selected: _workdays.contains(day.key),
                onSelected: (selected) => setState(() {
                  if (selected) {
                    _workdays.add(day.key);
                  } else {
                    _workdays.remove(day.key);
                  }
                }),
              ),
          ],
        ),
        const SizedBox(height: 10),
        OutlinedButton.icon(
          icon: const Icon(Icons.schedule),
          label: Text(
            '通常 ${_offWork.hour.toString().padLeft(2, '0')}:${_offWork.minute.toString().padLeft(2, '0')} 下班',
          ),
          onPressed: () async {
            final time = await showTimePicker(
              context: context,
              initialTime: _offWork,
              builder: (context, child) => MediaQuery(
                data: MediaQuery.of(context)
                    .copyWith(alwaysUse24HourFormat: true),
                child: child!,
              ),
            );
            if (mounted && time != null) setState(() => _offWork = time);
          },
        ),
        const SizedBox(height: 10),
        FilledButton(
          onPressed:
              !_routineLoaded ||
                  widget.controller.busy ||
                  !widget.controller.snapshot.premium
              ? null
              : () async {
                  if (_routineEnabled && _workdays.isEmpty) {
                    ScaffoldMessenger.of(
                      context,
                    ).showSnackBar(const SnackBar(content: Text('请至少选择一个工作日')));
                    return;
                  }
                  await runAction(
                    context,
                    widget.controller.saveDailyRoutine(
                      enabled: _routineEnabled,
                      workdays: _workdays.toList()..sort(),
                      time:
                          '${_offWork.hour.toString().padLeft(2, '0')}:${_offWork.minute.toString().padLeft(2, '0')}',
                    ),
                    (_) => '工作节奏已保存',
                  );
                },
          child: const Text('保存工作节奏'),
        ),
        const SizedBox(height: 10),
        const Text(
          '桌宠运行时提醒。可选择下班、加班或今天休息；明确延后最多再提醒一次。',
          style: TextStyle(color: _muted, fontSize: 12),
        ),
      ],
    ),
  );
}
