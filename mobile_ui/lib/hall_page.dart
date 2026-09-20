part of 'main.dart';

class HallPage extends StatefulWidget {
  const HallPage({required this.controller, this.active = true, super.key});
  final AppController controller;
  final bool active;

  @override
  State<HallPage> createState() => _HallPageState();
}

class _HallPageState extends State<HallPage> {
  bool _requested = false;
  Timer? _clock;
  String? _result;

  @override
  void initState() {
    super.initState();
    _clock = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted && widget.active) setState(() {});
    });
  }

  @override
  void dispose() {
    _clock?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final snapshot = controller.snapshot;
    final access = snapshot.activated || controller.liveTrialSeconds > 0;
    if (widget.active &&
        access &&
        snapshot.companionHallEnabled &&
        (!_requested ||
            (controller.companion == null &&
                controller.hallError == null &&
                !controller.hallLoading)) &&
        !controller.loading) {
      _requested = true;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) controller.refreshCompanionHall();
      });
    }
    final enabled = controller.companion?['hallEnabled'] == true;
    final people = (controller.companionHall?['people'] as List? ?? [])
        .whereType<Map>()
        .where((person) => person['online'] != false)
        .toList();
    return PageScroll(
      onRefresh: access ? controller.refreshCompanionHall : null,
      children: [
        Container(
          padding: EdgeInsets.all(enabled ? 18 : 22),
          decoration: BoxDecoration(
            gradient: const LinearGradient(
              colors: [Color(0xff0b6659), Color(0xff1c947b)],
            ),
            borderRadius: BorderRadius.circular(20),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(Icons.public, size: enabled ? 26 : 34, color: _sun),
                  const SizedBox(width: 10),
                  const Expanded(
                    child: Text(
                      '碰个面，送只小可爱',
                      style: TextStyle(
                        color: Colors.white,
                        fontSize: 21,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              Text(
                enabled ? '选一位在线搭子，送只表情。' : '和在线用户收发表情与留言。',
                style: const TextStyle(color: Color(0xffd9eee7), height: 1.5),
              ),
              const SizedBox(height: 12),
              Wrap(
                spacing: 10,
                runSpacing: 8,
                children: [
                  _HallBadge(
                    label: enabled ? '${people.length} 位在线搭子' : '由你决定是否加入',
                    icon: enabled
                        ? Icons.circle
                        : Icons.visibility_off_outlined,
                  ),
                  _HallBadge(
                    label: snapshot.activated ? '已激活' : '体验期可用',
                    icon: Icons.favorite_outline,
                  ),
                ],
              ),
            ],
          ),
        ),
        const SizedBox(height: 16),
        if (!snapshot.companionHallEnabled)
          const Panel(
            child: StatusLine(
              icon: Icons.pause_circle_outline,
              title: '大厅暂时休息中',
              detail: '稍后再来看看，桌宠陪伴仍然可以继续。',
              color: _brand,
            ),
          )
        else if (!access)
          Panel(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  '完整体验已结束',
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: 8),
                const Text('激活后可继续互发，基础陪伴仍免费。'),
                const SizedBox(height: 12),
                FilledButton(
                  onPressed: () => tabToAccount(context),
                  child: const Text('查看激活方式'),
                ),
              ],
            ),
          )
        else ...[
          if (controller.hallLoading)
            const LinearProgressIndicator(minHeight: 3),
          if (controller.hallError != null) ...[
            Panel(
              color: _coralSoft,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text(
                    '暂时没连上大厅',
                    style: TextStyle(fontWeight: FontWeight.w700),
                  ),
                  const SizedBox(height: 6),
                  Text(controller.hallError!),
                  TextButton.icon(
                    onPressed: controller.hallLoading
                        ? null
                        : controller.refreshCompanionHall,
                    icon: const Icon(Icons.refresh),
                    label: const Text('重新连接'),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),
          ],
          if (controller.companion != null) ...[
            Panel(
              color: enabled ? _mint : _surface,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      CircleAvatar(
                        backgroundColor: _brand,
                        child: Icon(
                          enabled ? Icons.waving_hand_outlined : Icons.pets,
                          color: Colors.white,
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              '${controller.companion?['displayName'] ?? '桌搭子'}',
                              style: const TextStyle(
                                fontSize: 17,
                                fontWeight: FontWeight.w800,
                              ),
                            ),
                            Text(enabled ? '已加入 · 可接收来访' : '未加入 · 不公开'),
                          ],
                        ),
                      ),
                      IconButton(
                        tooltip: '修改大厅昵称',
                        icon: const Icon(Icons.edit_outlined),
                        onPressed: controller.busy ? null : _editName,
                      ),
                    ],
                  ),
                  if (!enabled) ...[
                    const SizedBox(height: 14),
                    const Text(
                      '加入后公开昵称和在线状态，允许其他用户发来 GIF 和留言；退出后不再公开，也不接收新来访。',
                    ),
                    const SizedBox(height: 12),
                    SizedBox(
                      width: double.infinity,
                      child: FilledButton.icon(
                        onPressed: controller.busy
                            ? null
                            : () => _setJoined(true),
                        icon: const Icon(Icons.waving_hand_outlined),
                        label: const Text('我愿意加入大厅'),
                      ),
                    ),
                  ] else
                    Row(
                      mainAxisAlignment: MainAxisAlignment.end,
                      children: [
                        TextButton(
                          onPressed: () => showDialog<void>(
                            context: context,
                            builder: (context) => AlertDialog(
                              title: const Text('大厅公开与接收范围'),
                              content: const Text(
                                '加入后公开昵称和在线状态，可接收 GIF 和留言；退出后不再公开或接收新来访，已接收的来访仍会保留。',
                              ),
                              actions: [
                                TextButton(
                                  onPressed: () => Navigator.pop(context),
                                  child: const Text('知道了'),
                                ),
                              ],
                            ),
                          ),
                          child: const Text('公开范围'),
                        ),
                        TextButton(
                          onPressed: controller.busy
                              ? null
                              : () => _setJoined(false),
                          child: const Text('退出大厅'),
                        ),
                      ],
                    ),
                ],
              ),
            ),
            const SizedBox(height: 18),
          ],
          if (enabled) ...[
            Row(
              children: [
                Expanded(
                  child: Text(
                    '现在在线 · ${people.length}',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                ),
                IconButton(
                  tooltip: '刷新大厅',
                  onPressed: controller.hallLoading
                      ? null
                      : controller.refreshCompanionHall,
                  icon: const Icon(Icons.refresh),
                ),
              ],
            ),
            const SizedBox(height: 8),
            if (people.isEmpty && !controller.hallLoading)
              const Panel(
                child: Column(
                  children: [
                    Icon(Icons.nightlight_outlined, size: 36, color: _brand),
                    SizedBox(height: 12),
                    Text(
                      '还没有其他人在线',
                      style: TextStyle(fontWeight: FontWeight.w700),
                    ),
                    SizedBox(height: 6),
                    Text('稍后刷新看看。', textAlign: TextAlign.center),
                  ],
                ),
              ),
            for (final person in people) ...[
              Panel(
                child: Row(
                  children: [
                    const CircleAvatar(
                      backgroundColor: _coralSoft,
                      child: Icon(Icons.pets, color: _coral),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            '${person['displayName'] ?? '桌搭子'}',
                            style: const TextStyle(
                              fontSize: 16,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          const SizedBox(height: 4),
                          const Row(
                            children: [
                              Icon(Icons.circle, color: _brand, size: 8),
                              SizedBox(width: 5),
                              Text('在线 · 可以收表情'),
                            ],
                          ),
                        ],
                      ),
                    ),
                    IconButton.filledTonal(
                      tooltip: '预览并发送给 ${person['displayName']}',
                      onPressed:
                          controller.busy || controller.hallCooldownSeconds > 0
                          ? null
                          : () => _send(person),
                      icon: const Icon(Icons.send_outlined),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 10),
            ],
            if (controller.hallCooldownSeconds > 0)
              Text(
                '${controller.hallCooldownSeconds} 秒后可以再发。',
                style: const TextStyle(
                  color: _brand,
                  fontWeight: FontWeight.w700,
                ),
              ),
            if (_result != null)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(
                  _result!,
                  style: const TextStyle(
                    color: _brand,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            const SizedBox(height: 12),
            const Text('每 30 秒可发一次，待接收最多 3 条、24 小时有效；设备收到不代表已读。'),
          ],
        ],
        const SizedBox(height: 18),
        Panel(
          child: ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.favorite_outline, color: _coral),
            title: const Text('我的搭子'),
            subtitle: Text(snapshot.activated ? '给熟悉的人发表情' : '正式激活后可绑定'),
            trailing: const Icon(Icons.chevron_right),
            onTap: () => tabToCompanion(context),
          ),
        ),
        const SizedBox(height: 14),
        if (snapshot.isQuiet) const Text('暂停中，已接收来访会在恢复后显示。'),
      ],
    );
  }

  Future<void> _setJoined(bool joined) async {
    await runAction(
      context,
      widget.controller.setCompanionHall(joined),
      (_) => joined ? '已加入大厅' : '已退出大厅',
    );
  }

  Future<void> _editName() async {
    final input = TextEditingController(
      text: '${widget.controller.companion?['displayName'] ?? ''}',
    );
    final name = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('让大家认识你'),
        content: TextField(
          controller: input,
          maxLength: 12,
          decoration: const InputDecoration(
            labelText: '昵称',
            helperText: '最多 12 个字符，与私密搭子共用',
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, input.text.trim()),
            child: const Text('保存'),
          ),
        ],
      ),
    );
    input.dispose();
    if (name != null && mounted) {
      await runAction(
        context,
        widget.controller.updateCompanionName(name),
        (_) => '昵称已更新',
      );
    }
  }

  Future<void> _send(Map person) async {
    final controller = widget.controller;
    await controller.refresh();
    if (!mounted) return;
    final petId = controller.snapshot.activePet;
    final gif = controller.petGif;
    final input = TextEditingController();
    final message = await showDialog<String>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text('送给 ${person['displayName'] ?? '桌搭子'}'),
        content: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (gif != null)
                Image.memory(
                  gif,
                  height: 120,
                  fit: BoxFit.contain,
                  errorBuilder: (_, _, _) => const Icon(Icons.pets, size: 64),
                ),
              const SizedBox(height: 8),
              Text(
                '这只表情会去对方屏幕坐一会儿。',
                style: Theme.of(context).textTheme.bodyMedium,
              ),
              const SizedBox(height: 14),
              TextField(
                controller: input,
                maxLength: 120,
                maxLines: 3,
                minLines: 1,
                decoration: const InputDecoration(labelText: '留一句话（可选）'),
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext),
            child: const Text('再想想'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, input.text.trim()),
            child: const Text('确认发送'),
          ),
        ],
      ),
    );
    input.dispose();
    if (message == null || !mounted) return;
    await runAction<String>(
      context,
      controller.sendCompanionHall('${person['id']}', message, petId: petId),
      (recipient) {
        if (mounted) setState(() => _result = '已发给 $recipient，等待接收。');
        return '已发给 $recipient';
      },
    );
  }
}

class _HallBadge extends StatelessWidget {
  const _HallBadge({required this.label, required this.icon});
  final String label;
  final IconData icon;
  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
    decoration: BoxDecoration(
      color: Colors.white.withValues(alpha: .14),
      borderRadius: BorderRadius.circular(30),
    ),
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 12, color: _sun),
        const SizedBox(width: 6),
        Text(label, style: const TextStyle(color: Colors.white, fontSize: 12)),
      ],
    ),
  );
}
