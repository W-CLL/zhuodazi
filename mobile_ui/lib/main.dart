import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'app_controller.dart';
import 'host_api.dart';

const _ink = Color(0xff17201e);
const _muted = Color(0xff5d6b67);
const _page = Color(0xfff3f7f5);
const _surface = Color(0xffffffff);
const _line = Color(0xffd8e2de);
const _brand = Color(0xff147d6b);
const _brandDark = Color(0xff0c5f52);
const _mint = Color(0xffdff1eb);
const _coral = Color(0xffd97752);
const _coralSoft = Color(0xffffeadf);
const _sun = Color(0xfff2c861);

void main() => runApp(const ZhuoDaziApp());

class ZhuoDaziApp extends StatelessWidget {
  const ZhuoDaziApp({super.key});

  @override
  Widget build(BuildContext context) {
    final scheme =
        ColorScheme.fromSeed(
          seedColor: _brand,
          brightness: Brightness.light,
          surface: _surface,
        ).copyWith(
          primary: _brand,
          onPrimary: Colors.white,
          secondary: _coral,
          onSecondary: Colors.white,
          surface: _surface,
          onSurface: _ink,
          outline: _line,
        );
    return MaterialApp(
      title: '桌搭子',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        useMaterial3: true,
        colorScheme: scheme,
        scaffoldBackgroundColor: _page,
        fontFamily: 'sans-serif',
        splashFactory: InkSparkle.splashFactory,
        textTheme: const TextTheme(
          headlineSmall: TextStyle(
            color: _ink,
            fontSize: 26,
            fontWeight: FontWeight.w800,
          ),
          titleLarge: TextStyle(
            color: _ink,
            fontSize: 19,
            fontWeight: FontWeight.w800,
          ),
          titleMedium: TextStyle(
            color: _ink,
            fontSize: 16,
            fontWeight: FontWeight.w700,
          ),
          bodyLarge: TextStyle(color: _ink, fontSize: 15, height: 1.35),
          bodyMedium: TextStyle(color: _muted, fontSize: 13, height: 1.35),
          labelLarge: TextStyle(fontSize: 14, fontWeight: FontWeight.w700),
        ),
        appBarTheme: const AppBarTheme(
          backgroundColor: _page,
          foregroundColor: _ink,
          elevation: 0,
          surfaceTintColor: Colors.transparent,
        ),
        navigationBarTheme: NavigationBarThemeData(
          backgroundColor: _surface,
          elevation: 0,
          indicatorColor: _mint,
          labelTextStyle: WidgetStateProperty.resolveWith((states) {
            final selected = states.contains(WidgetState.selected);
            return TextStyle(
              color: selected ? _brandDark : _muted,
              fontSize: 11,
              fontWeight: selected ? FontWeight.w800 : FontWeight.w600,
            );
          }),
          iconTheme: WidgetStateProperty.resolveWith((states) {
            final selected = states.contains(WidgetState.selected);
            return IconThemeData(
              color: selected ? _brandDark : _muted,
              size: 22,
            );
          }),
        ),
        inputDecorationTheme: InputDecorationTheme(
          filled: true,
          fillColor: _surface,
          contentPadding: const EdgeInsets.symmetric(
            horizontal: 15,
            vertical: 13,
          ),
          border: OutlineInputBorder(
            borderRadius: BorderRadius.circular(8),
            borderSide: const BorderSide(color: _line),
          ),
          enabledBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(8),
            borderSide: const BorderSide(color: _line),
          ),
          focusedBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(8),
            borderSide: const BorderSide(color: _brand, width: 1.5),
          ),
        ),
        cardTheme: CardThemeData(
          color: _surface,
          elevation: 0,
          margin: EdgeInsets.zero,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(8),
            side: const BorderSide(color: _line),
          ),
        ),
        filledButtonTheme: FilledButtonThemeData(
          style: FilledButton.styleFrom(
            backgroundColor: _brand,
            foregroundColor: Colors.white,
            minimumSize: const Size(0, 46),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(8),
            ),
          ),
        ),
        outlinedButtonTheme: OutlinedButtonThemeData(
          style: OutlinedButton.styleFrom(
            foregroundColor: _brandDark,
            minimumSize: const Size(0, 44),
            side: const BorderSide(color: _line),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(8),
            ),
          ),
        ),
      ),
      home: AppShell(controller: AppController()),
    );
  }
}

class AppShell extends StatefulWidget {
  const AppShell({required this.controller, super.key});
  final AppController controller;
  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> with WidgetsBindingObserver {
  int _index = 0;
  late final AppController _controller = widget.controller;
  static const _titles = ['首页', '互动', '桌宠', '搭子', '我的'];
  static const _icons = [
    Icons.home_outlined,
    Icons.chat_bubble_outline,
    Icons.pets_outlined,
    Icons.people_outline,
    Icons.person_outline,
  ];

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _controller.initialize();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _controller.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed && mounted) _controller.refresh();
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: _controller,
    builder: (context, _) => Scaffold(
      appBar: PreferredSize(
        preferredSize: const Size.fromHeight(74),
        child: _TopBar(title: _titles[_index], snapshot: _controller.snapshot),
      ),
      body: IndexedStack(
        index: _index,
        children: [
          HomePage(controller: _controller),
          InteractionPage(controller: _controller),
          PetPage(controller: _controller),
          CompanionPage(controller: _controller),
          AccountPage(controller: _controller),
        ],
      ),
      bottomNavigationBar: NavigationBar(
        height: 72,
        selectedIndex: _index,
        onDestinationSelected: (value) => setState(() => _index = value),
        destinations: [
          for (var i = 0; i < _titles.length; i++)
            NavigationDestination(
              icon: _navigationIcon(i, false),
              selectedIcon: _navigationIcon(i, true),
              label: _titles[i],
            ),
        ],
      ),
    ),
  );

  IconData _selectedIcon(int index) => switch (index) {
    0 => Icons.home_rounded,
    1 => Icons.chat_bubble_rounded,
    2 => Icons.pets,
    3 => Icons.people_rounded,
    _ => Icons.person_rounded,
  };

  Widget _navigationIcon(int index, bool selected) {
    final icon = Icon(selected ? _selectedIcon(index) : _icons[index]);
    if (index != 2) return icon;
    return Container(
      width: 42,
      height: 42,
      decoration: BoxDecoration(
        color: selected ? _brand : _ink,
        shape: BoxShape.circle,
      ),
      child: Icon(
        selected ? _selectedIcon(index) : _icons[index],
        color: selected ? Colors.white : _sun,
        size: 23,
      ),
    );
  }

  void selectTab(int index) => setState(() => _index = index);
}

class _TopBar extends StatelessWidget {
  const _TopBar({required this.title, required this.snapshot});
  final String title;
  final HostSnapshot snapshot;
  @override
  Widget build(BuildContext context) => AppBar(
    automaticallyImplyLeading: false,
    toolbarHeight: 74,
    titleSpacing: 20,
    title: Row(
      children: [
        Container(
          width: 38,
          height: 38,
          decoration: BoxDecoration(
            color: _ink,
            borderRadius: BorderRadius.circular(11),
          ),
          child: const Icon(Icons.pets_rounded, color: _sun, size: 23),
        ),
        const SizedBox(width: 11),
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Text(
              '桌搭子',
              style: TextStyle(fontSize: 20, fontWeight: FontWeight.w900),
            ),
            Text(
              title,
              style: const TextStyle(
                color: _muted,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
        ),
        const Spacer(),
        StatusPill(label: snapshot.licenseLabel, active: snapshot.activated),
      ],
    ),
  );
}

class HomePage extends StatelessWidget {
  const HomePage({required this.controller, super.key});
  final AppController controller;
  @override
  Widget build(BuildContext context) {
    final snapshot = controller.snapshot;
    return PageScroll(
      onRefresh: controller.refresh,
      children: [
        const PageIntro(title: '今天也一起', subtitle: '查看桌宠状态，快速完成常用操作。'),
        PetStage(snapshot: snapshot, gif: controller.petGif, height: 206),
        const SizedBox(height: 14),
        if (!snapshot.activated && snapshot.trialSeconds > 0) ...[
          Panel(
            color: _mint,
            child: StatusLine(
              icon: Icons.timer_outlined,
              title: '完整体验还剩 ${trialClock(snapshot.trialSeconds)}',
              detail: '先拖一拖、换一只，或打开「搭子」看看怎么发 GIF。',
              color: _brand,
            ),
          ),
          const SizedBox(height: 14),
        ],
        _HomeStatus(controller: controller),
        const SizedBox(height: 22),
        const SectionTitle(label: '常用操作'),
        const SizedBox(height: 9),
        Row(
          children: [
            Expanded(
              child: QuickAction(
                icon: Icons.touch_app_outlined,
                label: '说句话',
                onTap: () => runAction(
                  context,
                  controller.service('interact'),
                  (_) => '桌宠回应你了',
                ),
              ),
            ),
            const SizedBox(width: 9),
            Expanded(
              child: QuickAction(
                icon: Icons.shuffle_rounded,
                label: '切换形象',
                onTap: () => runAction(
                  context,
                  controller.service('next'),
                  (_) => '已换好桌宠',
                ),
              ),
            ),
            const SizedBox(width: 9),
            Expanded(
              child: QuickAction(
                icon: snapshot.hidden || snapshot.clickThrough
                    ? Icons.visibility_outlined
                    : Icons.visibility_off_outlined,
                label: snapshot.hidden || snapshot.clickThrough
                    ? '恢复显示'
                    : '暂时隐藏',
                onTap: () => runAction(
                  context,
                  controller.service(
                    snapshot.hidden || snapshot.clickThrough ? 'show' : 'hide',
                  ),
                  (_) => snapshot.hidden || snapshot.clickThrough
                      ? '桌宠已恢复'
                      : '桌宠已隐藏',
                ),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

class _HomeStatus extends StatelessWidget {
  const _HomeStatus({required this.controller});
  final AppController controller;
  @override
  Widget build(BuildContext context) {
    final snapshot = controller.snapshot;
    if (!snapshot.overlayAllowed) {
      return Panel(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const StatusLine(
              icon: Icons.layers_outlined,
              title: '先打开悬浮窗，桌宠才会出现',
              detail: '桌搭子要待在其他应用上面。授权后回到这里，它就会待在屏幕边角。',
              color: _coral,
            ),
            const SizedBox(height: 14),
            FilledButton.icon(
              onPressed: () => runAction(
                context,
                controller.requestOverlayPermission(),
                (_) => '',
              ),
              icon: const Icon(Icons.open_in_new),
              label: const Text('去打开悬浮窗'),
            ),
          ],
        ),
      );
    }
    if (!snapshot.running) {
      return Panel(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const StatusLine(
              icon: Icons.bedtime_outlined,
              title: '桌宠还在等你',
              detail: '启动后会在屏幕边上散步、互动和陪伴。',
              color: _brand,
            ),
            const SizedBox(height: 14),
            FilledButton.icon(
              onPressed: () => runAction(
                context,
                controller.service('start'),
                (_) => '桌宠已启动',
              ),
              icon: const Icon(Icons.play_arrow_rounded),
              label: const Text('启动桌宠'),
            ),
          ],
        ),
      );
    }
    final inactiveTouch = snapshot.hidden || snapshot.clickThrough;
    final detail = snapshot.hidden
        ? '桌宠仍在后台运行，随时可以恢复显示。'
        : snapshot.clickThrough
        ? '当前触摸会直接交给桌宠下方的应用。'
        : '桌宠可拖动，点一下会打开中央快捷菜单。';
    return Panel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          StatusLine(
            icon: inactiveTouch
                ? Icons.visibility_off_outlined
                : Icons.check_circle_outline,
            title: snapshot.petStatus,
            detail: detail,
            color: inactiveTouch ? _coral : _brand,
          ),
          const SizedBox(height: 13),
          Row(
            children: [
              Expanded(
                child: OutlinedButton.icon(
                  onPressed: () => runAction(
                    context,
                    controller.service(inactiveTouch ? 'show' : 'clickThrough'),
                    (_) => '',
                  ),
                  icon: Icon(
                    inactiveTouch
                        ? Icons.visibility_outlined
                        : Icons.touch_app_outlined,
                  ),
                  label: Text(inactiveTouch ? '恢复桌宠' : '开启触摸穿透'),
                ),
              ),
              const SizedBox(width: 9),
              OutlinedButton.icon(
                onPressed: () => runAction(
                  context,
                  controller.service('stop'),
                  (_) => '桌宠已暂停',
                ),
                icon: const Icon(Icons.stop_circle_outlined),
                label: const Text('暂停'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class PetPage extends StatelessWidget {
  const PetPage({required this.controller, super.key});
  final AppController controller;
  @override
  Widget build(BuildContext context) {
    final snapshot = controller.snapshot;
    final selected = snapshot.selectedPet;
    return PageScroll(
      onRefresh: controller.refresh,
      children: [
        const PageIntro(title: '桌宠', subtitle: '选择形象，调整悬浮显示与自动行为。'),
        PetStage(snapshot: snapshot, gif: controller.petGif, height: 180),
        const SizedBox(height: 12),
        Panel(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const SectionTitle(label: '当前形象'),
              const SizedBox(height: 9),
              Row(
                children: [
                  Expanded(
                    child: Text(
                      selected?.name ?? '尚未选择',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                  ),
                  OutlinedButton.icon(
                    onPressed: () => showPetPicker(context, controller),
                    icon: const Icon(Icons.grid_view_rounded),
                    label: const Text('选择'),
                  ),
                ],
              ),
              const SizedBox(height: 9),
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton.icon(
                      onPressed:
                          !snapshot.activated ||
                              snapshot.pets
                                      .where((pet) => pet.isCustom)
                                      .length >=
                                  3
                          ? null
                          : () => runAction(
                              context,
                              controller.importGif(),
                              (_) => 'GIF 已导入',
                            ),
                      icon: Icon(
                        snapshot.activated
                            ? Icons.file_upload_outlined
                            : Icons.lock_outline,
                      ),
                      label: Text(snapshot.activated ? '导入自己的 GIF' : '激活后可导入'),
                    ),
                  ),
                  if (selected?.isCustom == true) ...[
                    const SizedBox(width: 9),
                    IconButton(
                      onPressed: () =>
                          confirmDelete(context, controller, selected!.id),
                      icon: const Icon(Icons.delete_outline, color: _coral),
                      tooltip: '删除自定义 GIF',
                    ),
                  ],
                ],
              ),
              const SizedBox(height: 8),
              Text(
                snapshot.activated
                    ? '单个 GIF 不超过 8 MB，最多保存 3 个。'
                    : '自定义桌宠只对正式激活设备开放。',
                style: Theme.of(context).textTheme.bodyMedium,
              ),
            ],
          ),
        ),
        const SizedBox(height: 22),
        const SectionTitle(label: '外观'),
        const SizedBox(height: 8),
        Panel(
          child: Column(
            children: [
              SettingSlider(
                label: '桌宠大小',
                value: snapshot.sizeDp.toDouble(),
                min: 96,
                max: 280,
                suffix: 'dp',
                onChanged: (value) =>
                    controller.setSetting(settingSize, value.round()),
              ),
              const Divider(height: 25),
              SettingSlider(
                label: '透明度',
                value: snapshot.opacity.toDouble(),
                min: 20,
                max: 100,
                suffix: '%',
                onChanged: (value) =>
                    controller.setSetting(settingOpacity, value.round()),
              ),
              const Divider(height: 10),
              SwitchListTile.adaptive(
                contentPadding: EdgeInsets.zero,
                title: const Text('镜像显示'),
                subtitle: const Text('反转桌宠朝向'),
                value: snapshot.mirrored,
                onChanged: (value) =>
                    controller.setSetting(settingMirrored, value),
              ),
            ],
          ),
        ),
        const SizedBox(height: 22),
        const SectionTitle(label: '行为'),
        const SizedBox(height: 8),
        Panel(
          child: Column(
            children: [
              SwitchListTile.adaptive(
                contentPadding: EdgeInsets.zero,
                title: const Text('随机走动'),
                subtitle: const Text('在屏幕范围内自动散步'),
                value: snapshot.movement,
                onChanged: (value) =>
                    controller.setSetting(settingMovement, value),
              ),
              const Divider(height: 10),
              SwitchListTile.adaptive(
                contentPadding: EdgeInsets.zero,
                title: const Text('随机换宠'),
                subtitle: const Text('按设置间隔轮换桌宠'),
                value: snapshot.randomPet,
                onChanged: (value) =>
                    controller.setSetting(settingRandomPet, value),
              ),
              const Divider(height: 10),
              ChoiceRow(
                label: '性格',
                value: snapshot.personality,
                options: const {
                  'lively': '活泼',
                  'shy': '害羞',
                  'clingy': '黏人',
                  'chaotic': '混乱',
                },
                onChanged: (value) =>
                    controller.setSetting(settingPersonality, value),
              ),
              const Divider(height: 10),
              ChoiceRow(
                label: '换宠间隔',
                value: '${snapshot.randomPetInterval}',
                options: const {
                  '30': '30 秒',
                  '60': '1 分钟',
                  '300': '5 分钟',
                  '600': '10 分钟',
                  '1800': '30 分钟',
                },
                onChanged: (value) => controller.setSetting(
                  settingRandomPetInterval,
                  int.parse(value),
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class InteractionPage extends StatelessWidget {
  const InteractionPage({required this.controller, super.key});
  final AppController controller;
  @override
  Widget build(BuildContext context) {
    final snapshot = controller.snapshot;
    final canInteract = snapshot.running && snapshot.premium;
    final interactionStatus = !snapshot.running
        ? '先启动桌宠，互动会显示在桌宠旁边。'
        : !snapshot.premium
        ? '体验或正式激活后可使用随机互动。'
        : '桌宠已准备好，也会按设定的频率主动出现。';
    return PageScroll(
      onRefresh: controller.refresh,
      children: [
        const PageIntro(title: '互动', subtitle: '随机带来心情问候、笑话、趣味内容和关怀。'),
        Panel(
          color: canInteract ? _mint : _coralSoft,
          child: Row(
            children: [
              Icon(
                canInteract ? Icons.forum_outlined : Icons.info_outline,
                color: canInteract ? _brand : _coral,
                size: 27,
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  interactionStatus,
                  style: Theme.of(context).textTheme.bodyLarge,
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 22),
        const SectionTitle(label: '立即互动'),
        const SizedBox(height: 8),
        Panel(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Icon(Icons.casino_outlined, color: _coral, size: 27),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          '随机来一个',
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                        const SizedBox(height: 4),
                        const Text('桌宠会随机选择问候、笑话、趣味题、小贴士或关怀内容。'),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 15),
              SizedBox(
                width: double.infinity,
                child: FilledButton.icon(
                  onPressed: canInteract
                      ? () => runAction(
                          context,
                          controller.service('interact'),
                          (_) => '互动已经出现在桌宠旁边',
                        )
                      : null,
                  icon: const Icon(Icons.auto_awesome_outlined),
                  label: Text(
                    !snapshot.running
                        ? '先启动桌宠'
                        : snapshot.premium
                        ? '开始随机互动'
                        : '体验或激活后可用',
                  ),
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 22),
        const SectionTitle(label: '线上趣味内容'),
        const SizedBox(height: 8),
        Panel(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(
                    snapshot.interactionOnlineCount > 0
                        ? Icons.cloud_done_outlined
                        : Icons.storage_outlined,
                    color: _brand,
                    size: 26,
                  ),
                  const SizedBox(width: 11),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          '${snapshot.interactionCachedCount} 条可用内容',
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          snapshot.interactionOnlineCount > 0
                              ? '${snapshot.interactionOnlineCount} 条来自线上 · 目录 v${snapshot.interactionCatalogVersion}'
                              : snapshot.interactionSyncError.isNotEmpty
                              ? '同步失败：${snapshot.interactionSyncError}'
                              : '当前使用随包内容，联网刷新后会补充新内容。',
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 15),
              const Wrap(
                spacing: 14,
                runSpacing: 10,
                children: [
                  InteractionTypeLabel(
                    icon: Icons.mood_outlined,
                    label: '心情问候',
                  ),
                  InteractionTypeLabel(
                    icon: Icons.sentiment_satisfied_alt,
                    label: '冷笑话',
                  ),
                  InteractionTypeLabel(
                    icon: Icons.calculate_outlined,
                    label: '数学题',
                  ),
                  InteractionTypeLabel(
                    icon: Icons.lightbulb_outline,
                    label: '趣味知识',
                  ),
                  InteractionTypeLabel(
                    icon: Icons.psychology_alt_outlined,
                    label: '脑筋急转弯',
                  ),
                  InteractionTypeLabel(
                    icon: Icons.favorite_outline,
                    label: '小贴士与关怀',
                  ),
                ],
              ),
              const SizedBox(height: 15),
              OutlinedButton.icon(
                onPressed: snapshot.premium
                    ? () => runAction(
                        context,
                        controller.syncInteractions(),
                        (_) =>
                            '${controller.snapshot.interactionCachedCount} 条内容可用',
                      )
                    : null,
                icon: const Icon(Icons.sync),
                label: const Text('刷新线上内容'),
              ),
            ],
          ),
        ),
        const SizedBox(height: 22),
        const SectionTitle(label: '自动出现'),
        const SizedBox(height: 8),
        Panel(
          child: Column(
            children: [
              SwitchListTile.adaptive(
                contentPadding: EdgeInsets.zero,
                title: const Text('允许桌宠主动互动'),
                subtitle: Text(
                  snapshot.premium ? '按下面的频率随机发起一次互动' : '体验或正式激活后可开启',
                ),
                value: snapshot.interactions,
                onChanged: snapshot.premium
                    ? (value) =>
                          controller.setSetting(settingInteractions, value)
                    : null,
              ),
              const Divider(height: 10),
              ChoiceRow(
                label: '互动频率',
                value: snapshot.interactionMode,
                options: const {
                  'quiet': '安静 · 60-120 分钟',
                  'standard': '标准 · 30-60 分钟',
                  'lively': '活跃 · 10-30 分钟',
                },
                enabled: snapshot.premium,
                onChanged: (value) =>
                    controller.setSetting(settingInteractionMode, value),
              ),
            ],
          ),
        ),
        const SizedBox(height: 22),
        const SectionTitle(label: '日常台词风格'),
        const SizedBox(height: 8),
        Panel(
          child: DropdownButtonFormField<String>(
            initialValue: snapshot.wordPacks.contains(snapshot.wordPack)
                ? snapshot.wordPack
                : null,
            isExpanded: true,
            decoration: const InputDecoration(
              labelText: '互动词包',
              helperText: '用于拖动、换宠和普通气泡台词。',
            ),
            items: snapshot.wordPacks
                .map(
                  (pack) => DropdownMenuItem(
                    value: pack,
                    child: Text(pack.replaceAll('.json', '')),
                  ),
                )
                .toList(),
            onChanged: snapshot.premium && snapshot.wordPacks.isNotEmpty
                ? (value) {
                    if (value != null) {
                      controller.setSetting(settingWordPack, value);
                    }
                  }
                : null,
          ),
        ),
      ],
    );
  }
}

class CompanionPage extends StatefulWidget {
  const CompanionPage({required this.controller, super.key});
  final AppController controller;
  @override
  State<CompanionPage> createState() => _CompanionPageState();
}

class _CompanionPageState extends State<CompanionPage> {
  final _nameController = TextEditingController();
  final _codeController = TextEditingController();
  bool _requested = false;
  @override
  void dispose() {
    _nameController.dispose();
    _codeController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final snapshot = widget.controller.snapshot;
    if (!snapshot.activated) {
      return PageScroll(
        children: [
          const PageIntro(title: '搭子联机', subtitle: '和另一台设备互相发送当前桌宠。'),
          Panel(
            color: _coralSoft,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Icon(Icons.lock_outline, color: _coral, size: 28),
                const SizedBox(height: 12),
                Text('正式激活后可用', style: Theme.of(context).textTheme.titleLarge),
                const SizedBox(height: 5),
                const Text('Android 设备独立计为一个激活设备。'),
                const SizedBox(height: 14),
                FilledButton.icon(
                  onPressed: () => tabToAccount(context),
                  icon: const Icon(Icons.key_outlined),
                  label: const Text('前往激活'),
                ),
              ],
            ),
          ),
        ],
      );
    }
    if (!_requested && widget.controller.companion == null) {
      _requested = true;
      WidgetsBinding.instance.addPostFrameCallback(
        (_) => runAction(
          context,
          widget.controller.refreshCompanion(),
          (_) => '资料已刷新',
        ),
      );
    }
    final profile = widget.controller.companion;
    if (profile == null) {
      final connecting =
          widget.controller.companionLoading ||
          (_requested && widget.controller.companionError == null);
      final connectionError = widget.controller.companionError;
      return PageScroll(
        children: [
          const PageIntro(title: '搭子联机', subtitle: '配对后互相发送当前桌宠。'),
          Panel(
            child: Column(
              children: [
                const SizedBox(height: 6),
                if (connecting)
                  const CircularProgressIndicator(strokeWidth: 2)
                else
                  Icon(
                    Icons.cloud_off_outlined,
                    size: 34,
                    color: Theme.of(context).colorScheme.error,
                  ),
                const SizedBox(height: 14),
                Text(
                  connecting ? '正在连接搭子服务…' : '搭子服务连接失败',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                if (!connecting && connectionError != null) ...[
                  const SizedBox(height: 8),
                  Text(
                    connectionError,
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                    ),
                  ),
                ],
                const SizedBox(height: 14),
                OutlinedButton.icon(
                  onPressed: connecting
                      ? null
                      : () => runAction(
                          context,
                          widget.controller.refreshCompanion(),
                          (_) => '资料已刷新',
                        ),
                  icon: const Icon(Icons.refresh),
                  label: const Text('重试'),
                ),
              ],
            ),
          ),
        ],
      );
    }
    final partner = profile['partner'] as Map?;
    if (_nameController.text.isEmpty) {
      _nameController.text = '${profile['displayName'] ?? ''}';
    }
    return PageScroll(
      children: [
        const PageIntro(title: '搭子联机', subtitle: '配对后互相发送当前桌宠。'),
        const SectionTitle(label: '我的资料'),
        const SizedBox(height: 8),
        Panel(
          child: Column(
            children: [
              TextField(
                controller: _nameController,
                maxLength: 20,
                decoration: const InputDecoration(
                  labelText: '搭子昵称',
                  counterText: '',
                ),
              ),
              const SizedBox(height: 9),
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton.icon(
                      onPressed: () => runAction(
                        context,
                        widget.controller.updateCompanionName(
                          _nameController.text.trim(),
                        ),
                        (_) => '昵称已更新',
                      ),
                      icon: const Icon(Icons.check),
                      label: const Text('保存昵称'),
                    ),
                  ),
                  const SizedBox(width: 9),
                  IconButton(
                    onPressed: () => runAction(
                      context,
                      widget.controller.refreshCompanion(),
                      (_) => '资料已刷新',
                    ),
                    icon: const Icon(Icons.refresh),
                    tooltip: '刷新资料',
                  ),
                ],
              ),
            ],
          ),
        ),
        const SizedBox(height: 14),
        Panel(
          color: _mint,
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Text(
                      '我的配对码',
                      style: TextStyle(
                        color: _muted,
                        fontSize: 12,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 5),
                    Text(
                      '${profile['pairingCode'] ?? ''}',
                      style: const TextStyle(
                        color: _brandDark,
                        fontSize: 27,
                        fontWeight: FontWeight.w900,
                        letterSpacing: 1.5,
                      ),
                    ),
                  ],
                ),
              ),
              IconButton(
                onPressed: () {
                  Clipboard.setData(
                    ClipboardData(text: '${profile['pairingCode'] ?? ''}'),
                  );
                  ScaffoldMessenger.of(context)
                      .showSnackBar(const SnackBar(content: Text('配对码已复制')));
                },
                icon: const Icon(Icons.copy_outlined),
                tooltip: '复制配对码',
              ),
            ],
          ),
        ),
        const SizedBox(height: 22),
        const SectionTitle(label: '当前搭子'),
        const SizedBox(height: 8),
        if (partner == null)
          Panel(
            child: Column(
              children: [
                TextField(
                  controller: _codeController,
                  maxLength: 8,
                  textCapitalization: TextCapitalization.characters,
                  decoration: const InputDecoration(
                    labelText: '输入搭子的 8 位配对码',
                    counterText: '',
                  ),
                ),
                const SizedBox(height: 9),
                FilledButton.icon(
                  onPressed: () => runAction(
                    context,
                    widget.controller.pairCompanion(_codeController.text),
                    (_) => '配对成功',
                  ),
                  icon: const Icon(Icons.link),
                  label: const Text('立即配对'),
                ),
              ],
            ),
          )
        else
          Panel(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Container(
                      width: 42,
                      height: 42,
                      decoration: BoxDecoration(
                        color: _mint,
                        borderRadius: BorderRadius.circular(12),
                      ),
                      child: const Icon(
                        Icons.person_rounded,
                        color: _brandDark,
                      ),
                    ),
                    const SizedBox(width: 11),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            '${partner['displayName'] ?? '搭子'}',
                            style: Theme.of(context).textTheme.titleMedium,
                          ),
                          const Text(
                            '已连接',
                            style: TextStyle(
                              color: _brand,
                              fontSize: 12,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 14),
                FilledButton.icon(
                  onPressed: () => runAction<String>(
                    context,
                    widget.controller.sendCompanion(),
                    (recipient) => '已发送给 $recipient',
                  ),
                  icon: const Icon(Icons.send_outlined),
                  label: const Text('发送当前桌宠'),
                ),
                const SizedBox(height: 8),
                OutlinedButton.icon(
                  onPressed: () => confirmUnpair(context, widget.controller),
                  icon: const Icon(Icons.link_off, color: _coral),
                  label: const Text('解除配对'),
                ),
              ],
            ),
          ),
      ],
    );
  }
}

class AccountPage extends StatelessWidget {
  const AccountPage({required this.controller, super.key});
  final AppController controller;
  @override
  Widget build(BuildContext context) {
    final snapshot = controller.snapshot;
    return PageScroll(
      onRefresh: controller.refresh,
      children: [
        const PageIntro(title: '我的', subtitle: '激活、权限与应用设置。'),
        Panel(
          color: snapshot.activated ? _mint : _coralSoft,
          child: Row(
            children: [
              Icon(
                snapshot.activated
                    ? Icons.verified_rounded
                    : Icons.key_outlined,
                color: snapshot.activated ? _brand : _coral,
                size: 30,
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      snapshot.activated ? '此设备已激活' : '体验与激活',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      snapshot.activated
                          ? '授权尾号 · ${snapshot.installationSuffix}'
                          : trialText(snapshot.trialSeconds),
                      style: Theme.of(context).textTheme.bodyMedium,
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
        if (!snapshot.activated) ...[
          const SizedBox(height: 14),
          Panel(
            child: Column(
              children: [
                FilledButton.icon(
                  onPressed: () => _activateWithPrompt(context),
                  icon: const Icon(Icons.lock_open_outlined),
                  label: const Text('输入激活码'),
                ),
                const SizedBox(height: 8),
                OutlinedButton.icon(
                  onPressed: () => runAction(
                    context,
                    controller.checkTrial(),
                    (_) => '体验时间已更新',
                  ),
                  icon: const Icon(Icons.timer_outlined),
                  label: const Text('检查体验时间'),
                ),
              ],
            ),
          ),
        ],
        const SizedBox(height: 22),
        const SectionTitle(label: '系统权限'),
        const SizedBox(height: 8),
        Panel(
          child: Column(
            children: [
              PermissionRow(
                icon: Icons.layers_outlined,
                label: '悬浮窗权限',
                value: snapshot.overlayAllowed ? '已允许' : '需要授权',
                ok: snapshot.overlayAllowed,
                onTap: () => runAction(
                  context,
                  controller.requestOverlayPermission(),
                  (_) => '',
                ),
              ),
              const Divider(height: 15),
              PermissionRow(
                icon: Icons.notifications_none,
                label: '通知权限',
                value: snapshot.notificationAllowed
                    ? '已允许桌宠状态与搭子提醒'
                    : '需要授权才能显示提醒',
                ok: snapshot.notificationAllowed,
                onTap: () => runAction(
                  context,
                  controller.requestNotificationPermission(),
                  (_) => '',
                ),
              ),
              const Divider(height: 8),
              SwitchListTile.adaptive(
                contentPadding: EdgeInsets.zero,
                title: const Text('开机后自动恢复'),
                subtitle: const Text('启动后台服务并恢复桌宠'),
                value: snapshot.startOnBoot,
                onChanged: (value) =>
                    controller.setSetting(settingStartOnBoot, value),
              ),
            ],
          ),
        ),
        const SizedBox(height: 22),
        const SectionTitle(label: '应用'),
        const SizedBox(height: 8),
        Panel(
          child: Column(
            children: [
              InfoRow(label: '当前版本', value: 'Android v${snapshot.version}'),
              const Divider(height: 15),
              PermissionRow(
                icon: Icons.settings_outlined,
                label: '应用系统设置',
                value: '通知、电池与权限',
                ok: true,
                onTap: () =>
                    runAction(context, controller.openAppSettings(), (_) => ''),
              ),
            ],
          ),
        ),
        const SizedBox(height: 25),
        const Center(
          child: Text(
            '桌搭子 Android',
            style: TextStyle(
              color: _muted,
              fontSize: 12,
              fontWeight: FontWeight.w600,
            ),
          ),
        ),
        const SizedBox(height: 10),
      ],
    );
  }

  Future<void> _activateWithPrompt(BuildContext context) async {
    final code = await showDialog<String>(
      context: context,
      builder: (context) {
        final input = TextEditingController();
        return AlertDialog(
          title: const Text('输入激活码'),
          content: TextField(
            controller: input,
            autofocus: true,
            maxLength: 8,
            textCapitalization: TextCapitalization.characters,
            decoration: const InputDecoration(hintText: '6 位激活码'),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, input.text),
              child: const Text('继续'),
            ),
          ],
        );
      },
    );
    if (code != null && context.mounted) {
      await runAction(
        context,
        controller.activate(code),
        (_) => '此 Android 设备已激活',
      );
    }
  }
}

class PageScroll extends StatelessWidget {
  const PageScroll({required this.children, this.onRefresh, super.key});
  final List<Widget> children;
  final Future<void> Function()? onRefresh;
  @override
  Widget build(BuildContext context) {
    final list = ListView(
      padding: const EdgeInsets.fromLTRB(20, 10, 20, 26),
      children: children,
    );
    return onRefresh == null
        ? list
        : RefreshIndicator(color: _brand, onRefresh: onRefresh!, child: list);
  }
}

class PageIntro extends StatelessWidget {
  const PageIntro({required this.title, required this.subtitle, super.key});
  final String title;
  final String subtitle;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 17),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(title, style: Theme.of(context).textTheme.headlineSmall),
        const SizedBox(height: 4),
        Text(subtitle, style: Theme.of(context).textTheme.bodyMedium),
      ],
    ),
  );
}

class SectionTitle extends StatelessWidget {
  const SectionTitle({required this.label, super.key});
  final String label;
  @override
  Widget build(BuildContext context) =>
      Text(label, style: Theme.of(context).textTheme.titleMedium);
}

class Panel extends StatelessWidget {
  const Panel({required this.child, this.color = _surface, super.key});
  final Widget child;
  final Color color;
  @override
  Widget build(BuildContext context) => Container(
    width: double.infinity,
    padding: const EdgeInsets.all(16),
    decoration: BoxDecoration(
      color: color,
      border: Border.all(color: color == _surface ? _line : color),
      borderRadius: BorderRadius.circular(8),
    ),
    child: Material(
      color: color,
      type: MaterialType.transparency,
      child: child,
    ),
  );
}

class StatusPill extends StatelessWidget {
  const StatusPill({required this.label, required this.active, super.key});
  final String label;
  final bool active;
  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 11, vertical: 8),
    decoration: BoxDecoration(
      color: active ? _mint : _coralSoft,
      borderRadius: BorderRadius.circular(20),
    ),
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(
          active ? Icons.verified_rounded : Icons.schedule_rounded,
          size: 15,
          color: active ? _brandDark : _coral,
        ),
        const SizedBox(width: 5),
        Text(
          label,
          style: TextStyle(
            color: active ? _brandDark : _coral,
            fontSize: 12,
            fontWeight: FontWeight.w800,
          ),
        ),
      ],
    ),
  );
}

class StatusLine extends StatelessWidget {
  const StatusLine({
    required this.icon,
    required this.title,
    required this.detail,
    required this.color,
    super.key,
  });
  final IconData icon;
  final String title;
  final String detail;
  final Color color;
  @override
  Widget build(BuildContext context) => Row(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Icon(icon, color: color, size: 27),
      const SizedBox(width: 11),
      Expanded(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 3),
            Text(detail, style: Theme.of(context).textTheme.bodyMedium),
          ],
        ),
      ),
    ],
  );
}

class StatusDot extends StatelessWidget {
  const StatusDot({super.key});
  @override
  Widget build(BuildContext context) => Container(
    width: 10,
    height: 10,
    decoration: const BoxDecoration(color: _brand, shape: BoxShape.circle),
  );
}

class PetStage extends StatelessWidget {
  const PetStage({
    required this.snapshot,
    required this.gif,
    this.height = 220,
    super.key,
  });
  final HostSnapshot snapshot;
  final Uint8List? gif;
  final double height;
  @override
  Widget build(BuildContext context) {
    final pet = snapshot.selectedPet;
    return Container(
      height: height,
      width: double.infinity,
      decoration: BoxDecoration(
        color: _ink,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Stack(
        children: [
          Positioned(
            top: 13,
            left: 15,
            right: 78,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  pet?.name ?? '月薪喵',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    color: Colors.white,
                    fontSize: 14,
                    fontWeight: FontWeight.w800,
                  ),
                ),
                Text(
                  snapshot.running ? '正在陪伴' : '桌宠预览',
                  style: const TextStyle(
                    color: _sun,
                    fontSize: 11,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ],
            ),
          ),
          Positioned(
            top: 13,
            right: 13,
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 5),
              decoration: BoxDecoration(
                color: Colors.white12,
                borderRadius: BorderRadius.circular(7),
              ),
              child: Text(
                '${snapshot.sizeDp} dp',
                style: const TextStyle(
                  color: Colors.white70,
                  fontSize: 11,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
          ),
          Positioned.fill(
            top: 35,
            bottom: 32,
            child: gif == null
                ? const Center(
                    child: Icon(Icons.pets_rounded, color: _sun, size: 64),
                  )
                : Center(
                    child: Image.memory(
                      gif!,
                      height: height - 54,
                      fit: BoxFit.contain,
                      gaplessPlayback: true,
                    ),
                  ),
          ),
          Positioned(
            bottom: 10,
            left: 14,
            right: 14,
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const StatusDot(),
                const SizedBox(width: 7),
                Expanded(
                  child: Text(
                    snapshot.petStatus,
                    maxLines: 2,
                    style: const TextStyle(color: Colors.white70, fontSize: 12),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class QuickAction extends StatelessWidget {
  const QuickAction({
    required this.icon,
    required this.label,
    required this.onTap,
    super.key,
  });
  final IconData icon;
  final String label;
  final VoidCallback onTap;
  @override
  Widget build(BuildContext context) => OutlinedButton(
    onPressed: onTap,
    style: OutlinedButton.styleFrom(
      padding: const EdgeInsets.symmetric(vertical: 13),
      minimumSize: const Size(0, 68),
    ),
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 22),
        const SizedBox(height: 4),
        Text(label, style: const TextStyle(fontSize: 12)),
      ],
    ),
  );
}

class InteractionTypeLabel extends StatelessWidget {
  const InteractionTypeLabel({
    required this.icon,
    required this.label,
    super.key,
  });
  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Icon(icon, color: _brandDark, size: 18),
      const SizedBox(width: 5),
      Text(
        label,
        style: const TextStyle(
          color: _ink,
          fontSize: 12,
          fontWeight: FontWeight.w700,
        ),
      ),
    ],
  );
}

class SettingSlider extends StatelessWidget {
  const SettingSlider({
    required this.label,
    required this.value,
    required this.min,
    required this.max,
    required this.suffix,
    required this.onChanged,
    super.key,
  });
  final String label;
  final double value;
  final double min;
  final double max;
  final String suffix;
  final ValueChanged<double> onChanged;
  @override
  Widget build(BuildContext context) => Column(
    children: [
      Row(
        children: [
          Expanded(
            child: Text(label, style: Theme.of(context).textTheme.bodyLarge),
          ),
          Text(
            '${value.round()} $suffix',
            style: const TextStyle(
              color: _brandDark,
              fontWeight: FontWeight.w800,
              fontSize: 13,
            ),
          ),
        ],
      ),
      Slider(
        value: value.clamp(min, max),
        min: min,
        max: max,
        label: '${value.round()} $suffix',
        onChanged: onChanged,
      ),
    ],
  );
}

class ChoiceRow extends StatelessWidget {
  const ChoiceRow({
    required this.label,
    required this.value,
    required this.options,
    required this.onChanged,
    this.enabled = true,
    super.key,
  });
  final String label;
  final String value;
  final Map<String, String> options;
  final ValueChanged<String> onChanged;
  final bool enabled;
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        label,
        style: TextStyle(color: enabled ? _ink : _muted, fontSize: 14),
      ),
      const SizedBox(height: 7),
      DropdownButtonFormField<String>(
        initialValue: options.containsKey(value) ? value : null,
        isExpanded: true,
        decoration: const InputDecoration(isDense: true),
        items: options.entries
            .map(
              (item) => DropdownMenuItem(
                value: item.key,
                child: Text(item.value, overflow: TextOverflow.ellipsis),
              ),
            )
            .toList(),
        onChanged: enabled
            ? (value) {
                if (value != null) onChanged(value);
              }
            : null,
      ),
    ],
  );
}

class PermissionRow extends StatelessWidget {
  const PermissionRow({
    required this.icon,
    required this.label,
    required this.value,
    required this.ok,
    required this.onTap,
    super.key,
  });
  final IconData icon;
  final String label;
  final String value;
  final bool ok;
  final VoidCallback onTap;
  @override
  Widget build(BuildContext context) => InkWell(
    onTap: onTap,
    borderRadius: BorderRadius.circular(7),
    child: Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(
        children: [
          Icon(icon, color: ok ? _brand : _coral, size: 22),
          const SizedBox(width: 11),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label, style: Theme.of(context).textTheme.bodyLarge),
                const SizedBox(height: 2),
                Text(value, style: Theme.of(context).textTheme.bodyMedium),
              ],
            ),
          ),
          Icon(
            ok ? Icons.check_circle_outline : Icons.chevron_right,
            color: ok ? _brand : _coral,
            size: 20,
          ),
        ],
      ),
    ),
  );
}

class InfoRow extends StatelessWidget {
  const InfoRow({required this.label, required this.value, super.key});
  final String label;
  final String value;
  @override
  Widget build(BuildContext context) => Row(
    children: [
      Expanded(
        child: Text(label, style: Theme.of(context).textTheme.bodyLarge),
      ),
      Text(
        value,
        style: const TextStyle(
          color: _muted,
          fontSize: 13,
          fontWeight: FontWeight.w700,
        ),
      ),
    ],
  );
}

Future<void> runAction<T>(
  BuildContext context,
  Future<T> action,
  String Function(T value)? success,
) async {
  try {
    final value = await action;
    if (context.mounted && success != null) {
      final message = success(value);
      if (message.isNotEmpty) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(message)));
      }
    }
  } catch (error) {
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          backgroundColor: _coral,
          content: Text(readableHostError(error)),
        ),
      );
    }
  }
}

Future<void> confirmDelete(
  BuildContext context,
  AppController controller,
  String petId,
) async {
  final confirmed = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: const Text('删除自定义桌宠'),
      content: const Text('将从这台 Android 设备删除该 GIF。'),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context, false),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: () => Navigator.pop(context, true),
          child: const Text('删除'),
        ),
      ],
    ),
  );
  if (confirmed == true && context.mounted) {
    await runAction(context, controller.deleteCustom(petId), (_) => 'GIF 已删除');
  }
}

Future<void> confirmUnpair(
  BuildContext context,
  AppController controller,
) async {
  final confirmed = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: const Text('解除搭子配对'),
      content: const Text('解除后双方需要重新输入配对码才能互发桌宠。'),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context, false),
          child: const Text('取消'),
        ),
        FilledButton(
          onPressed: () => Navigator.pop(context, true),
          child: const Text('解除'),
        ),
      ],
    ),
  );
  if (confirmed == true && context.mounted) {
    await runAction(context, controller.unpairCompanion(), (_) => '已解除配对');
  }
}

Future<void> showPetPicker(
  BuildContext context,
  AppController controller,
) async {
  await showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    backgroundColor: _page,
    builder: (context) => SafeArea(
      child: SizedBox(
        height: MediaQuery.sizeOf(context).height * .76,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 17, 15, 12),
              child: Row(
                children: [
                  Expanded(
                    child: Text(
                      '选择桌宠',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                  ),
                  IconButton(
                    onPressed: () => Navigator.pop(context),
                    icon: const Icon(Icons.close),
                    tooltip: '关闭',
                  ),
                ],
              ),
            ),
            Expanded(
              child: GridView.builder(
                padding: const EdgeInsets.fromLTRB(20, 4, 20, 24),
                gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                  crossAxisCount: 2,
                  crossAxisSpacing: 10,
                  mainAxisSpacing: 10,
                  childAspectRatio: 1.55,
                ),
                itemCount: controller.snapshot.pets.length,
                itemBuilder: (context, index) {
                  final pet = controller.snapshot.pets[index];
                  final selected = pet.id == controller.snapshot.activePet;
                  return InkWell(
                    onTap: () async {
                      await runAction(
                        context,
                        controller.setSetting(settingActivePet, pet.id),
                        (_) => '',
                      );
                      if (context.mounted) Navigator.pop(context);
                    },
                    borderRadius: BorderRadius.circular(8),
                    child: Container(
                      padding: const EdgeInsets.all(10),
                      decoration: BoxDecoration(
                        color: selected ? _mint : _surface,
                        border: Border.all(
                          color: selected ? _brand : _line,
                          width: selected ? 1.5 : 1,
                        ),
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Row(
                        children: [
                          FutureBuilder<Uint8List?>(
                            future: controller.gifFor(pet.id),
                            builder: (context, shot) => SizedBox(
                              width: 57,
                              height: 57,
                              child: shot.data == null
                                  ? const Icon(Icons.pets, color: _brand)
                                  : Image.memory(
                                      shot.data!,
                                      fit: BoxFit.contain,
                                    ),
                            ),
                          ),
                          const SizedBox(width: 8),
                          Expanded(
                            child: Text(
                              pet.name,
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                              style: TextStyle(
                                color: selected ? _brandDark : _ink,
                                fontWeight: FontWeight.w700,
                                fontSize: 13,
                              ),
                            ),
                          ),
                          if (selected)
                            const Icon(
                              Icons.check_circle,
                              color: _brand,
                              size: 18,
                            ),
                        ],
                      ),
                    ),
                  );
                },
              ),
            ),
          ],
        ),
      ),
    ),
  );
}

String trialClock(int seconds) {
  final safe = seconds < 0 ? 0 : seconds;
  final minutes = safe ~/ 60;
  final remaining = safe % 60;
  return '${minutes.toString().padLeft(2, '0')}:${remaining.toString().padLeft(2, '0')}';
}

String trialText(int seconds) {
  if (seconds <= 0) return '体验已结束，基础桌宠仍可使用';
  return '完整体验还剩 ${trialClock(seconds)}';
}

void tabToAccount(BuildContext context) {
  final shell = context.findAncestorStateOfType<_AppShellState>();
  shell?.selectTab(4);
}
