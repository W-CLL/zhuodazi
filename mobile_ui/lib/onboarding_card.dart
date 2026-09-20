part of 'main.dart';

class OnboardingCard extends StatelessWidget {
  const OnboardingCard({required this.controller, super.key});
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final state = controller.snapshot;
    if (state.guideVersion == 0) return const SizedBox.shrink();
    void act(String action) =>
        runAction(context, controller.guide(action), (_) => '');
    final shell = context.findAncestorStateOfType<_AppShellState>();
    if (state.guideUpgradeNotice) {
      return Panel(
        color: _mint,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              '新手体验上新了',
              style: TextStyle(fontWeight: FontWeight.w800),
            ),
            const SizedBox(height: 8),
            const Text('试试互动和小剧场，原有设置不变。'),
            Wrap(
              spacing: 10,
              children: [
                TextButton(
                  onPressed: controller.busy ? null : () => act('begin'),
                  child: const Text('体验一下'),
                ),
                TextButton(
                  onPressed: () => act('dismissNotice'),
                  child: const Text('知道了，不再提示'),
                ),
              ],
            ),
          ],
        ),
      );
    }
    if (state.guideDismissed) {
      if (state.guideStep == 5) return const SizedBox.shrink();
      return Align(
        alignment: Alignment.centerLeft,
        child: TextButton.icon(
          icon: const Icon(Icons.explore_outlined),
          onPressed: controller.busy ? null : () => act('begin'),
          label: Text(
            state.guideStep == 0
                ? '开始一分钟体验'
                : '继续新手体验 · 第 ${state.guideStep}/4 步',
          ),
        ),
      );
    }
    if (!state.petVisible && state.guideStep < 5) {
      return const SizedBox.shrink();
    }
    final step = state.guideStep;
    final playing = state.guideDemo.isNotEmpty;
    final title = switch (step) {
      0 => '一起玩一会吧',
      1 => '1 · 移动与菜单',
      2 => '2 · 让它回应你',
      3 => '3 · 看一场小剧场',
      4 => '4 · 暂停与找回',
      _ => '体验完成',
    };
    final text = switch (step) {
      0 => '试试互动和小剧场，随时可跳过。',
      1 => '拖动换位置，轻点打开菜单。',
      2 => playing ? '点一个回应，继续下一步。' : '和它打个招呼。',
      3 => playing ? '屏幕上方正在演短剧，可随时结束。' : '看两只小搭子轮流接话。',
      4 => '想安静就暂停，找不到它就点首页“恢复桌宠”。',
      _ => '去玩互动，或到大厅打个招呼，体验期也能加入。',
    };
    return Panel(
      color: _mint,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Icon(Icons.auto_awesome_outlined, color: _brand),
              const SizedBox(width: 9),
              Expanded(
                child: Text(
                  title,
                  style: Theme.of(context).textTheme.titleLarge,
                ),
              ),
            ],
          ),
          if (step > 0 && step < 5) ...[
            const SizedBox(height: 12),
            Semantics(
              label: '新手体验第 $step 步，共 4 步',
              child: LinearProgressIndicator(value: (step - 1) / 4),
            ),
          ],
          const SizedBox(height: 12),
          Text(text),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            runSpacing: 6,
            children: [
              if (step == 0)
                FilledButton(
                  onPressed: controller.busy ? null : () => act('begin'),
                  child: const Text('带我体验一下'),
                ),
              if (step == 1 || step == 4)
                FilledButton(
                  onPressed: controller.busy ? null : () => act('confirm'),
                  child: Text(step == 1 ? '下一步' : '完成体验'),
                ),
              if ((step == 2 || step == 3) && !playing)
                FilledButton.icon(
                  onPressed: controller.busy
                      ? null
                      : () => act(step == 2 ? 'interaction' : 'theater'),
                  icon: Icon(
                    step == 2
                        ? Icons.chat_bubble_outline
                        : Icons.theater_comedy_outlined,
                  ),
                  label: Text(step == 2 ? '让它回应我' : '看一小段'),
                ),
              if (playing)
                OutlinedButton(
                  onPressed: controller.busy ? null : () => act('endDemo'),
                  child: const Text('结束示例'),
                ),
              if (step == 4)
                TextButton(
                  onPressed: () => shell?.selectTab(1),
                  child: const Text('打开陪伴设置'),
                ),
              if (step == 5) ...[
                FilledButton(
                  onPressed: () => shell?.selectTab(1),
                  child: const Text('去玩互动'),
                ),
                OutlinedButton(
                  onPressed: () => shell?.selectTab(3),
                  child: const Text('看看桌宠大厅'),
                ),
              ],
              if (step > 0 && step < 5)
                TextButton(
                  onPressed: controller.busy ? null : () => act('skip'),
                  child: const Text('跳过这一步'),
                ),
              TextButton(
                onPressed: controller.busy ? null : () => act('dismiss'),
                child: Text(
                  step == 0
                      ? '先自己玩'
                      : step == 5
                      ? '收起'
                      : '稍后继续',
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

Future<bool> explainNotificationUse(
  BuildContext context,
  AppController controller, {
  bool reminder = false,
}) async {
  if (controller.snapshot.notificationAllowed) return true;
  final allow = await showDialog<bool>(
    context: context,
    builder: (dialog) => AlertDialog(
      title: Text(reminder ? '允许提醒通知' : '用通知找回桌宠'),
      content: Text(
        reminder
            ? '允许后，隐藏桌宠也能收到提醒；暂不允许时，请保持桌宠可见、应用后台运行。'
            : '通知可显示桌宠、恢复触摸；也可从首页点“恢复桌宠”。',
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(dialog, false),
          child: const Text('暂不允许，继续'),
        ),
        FilledButton(
          onPressed: () => Navigator.pop(dialog, true),
          child: const Text('允许通知'),
        ),
      ],
    ),
  );
  if (allow == null) return false;
  if (allow) await controller.requestNotificationPermission();
  return true;
}

Future<void> runRecoveryAction(
  BuildContext context,
  AppController controller,
  String action,
) async {
  if (action == 'hide' || action == 'clickThrough') {
    if (!await explainNotificationUse(context, controller)) return;
  }
  if (!context.mounted) return;
  await runAction(
    context,
    controller.service(action),
    (_) => action == 'show' ? '桌宠已恢复' : '可从首页“恢复桌宠”找回',
  );
}
