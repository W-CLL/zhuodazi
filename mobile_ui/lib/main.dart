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
    if (state == AppLifecycleState.resumed && mounted) {
      _controller.refresh(checkTrial: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final compact = MediaQuery.sizeOf(context).width < 360;
    return AnimatedBuilder(
      animation: _controller,
      builder: (context, _) => Scaffold(
        appBar: PreferredSize(
          preferredSize: Size.fromHeight(compact ? 68 : 74),
          child: _TopBar(
            title: _titles[_index],
            licenseLabel: _controller.licenseLabel,
            activated: _controller.snapshot.activated,
          ),
        ),
        body: SafeArea(
          top: false,
          child: IndexedStack(
            index: _index,
            children: [
              HomePage(controller: _controller),
              InteractionPage(controller: _controller),
              PetPage(controller: _controller),
              CompanionPage(controller: _controller),
              AccountPage(controller: _controller),
            ],
          ),
        ),
        bottomNavigationBar: NavigationBar(
          height: compact ? 64 : 72,
          labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
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
  }

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
  const _TopBar({
    required this.title,
    required this.licenseLabel,
    required this.activated,
  });
  final String title;
  final String licenseLabel;
  final bool activated;
  @override
  Widget build(BuildContext context) {
    final compact = MediaQuery.sizeOf(context).width < 360;
    return AppBar(
      automaticallyImplyLeading: false,
      toolbarHeight: compact ? 68 : 74,
      titleSpacing: compact ? 14 : 20,
      title: Row(
        children: [
          Container(
            width: compact ? 34 : 38,
            height: compact ? 34 : 38,
            decoration: BoxDecoration(
              color: _ink,
              borderRadius: BorderRadius.circular(11),
            ),
            child: const Icon(Icons.pets_rounded, color: _sun, size: 23),
          ),
          SizedBox(width: compact ? 8 : 11),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                const Text(
                  '桌搭子',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 20, fontWeight: FontWeight.w900),
                ),
                Text(
                  title,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    color: _muted,
                    fontSize: 12,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 8),
          Flexible(
            child: Align(
              alignment: Alignment.centerRight,
              child: FittedBox(
                fit: BoxFit.scaleDown,
                alignment: Alignment.centerRight,
                child: StatusPill(label: licenseLabel, active: activated),
              ),
            ),
          ),
        ],
      ),
    );
  }
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
        const PageIntro(title: '今天也一起', subtitle: '先让桌宠出来，再随手发一张给搭子。'),
        PetStage(snapshot: snapshot, gif: controller.petGif, height: 206),
        const SizedBox(height: 14),
        if (!snapshot.activated && controller.liveTrialSeconds > 0) ...[
          Panel(
            color: _mint,
            child: StatusLine(
              icon: Icons.timer_outlined,
              title: '完整体验还剩 ${trialClock(controller.liveTrialSeconds)}',
              detail: '先拖一拖、换一只。发 GIF 给搭子需要正式激活。',
              color: _brand,
            ),
          ),
          const SizedBox(height: 14),
        ],
        _HomeStatus(controller: controller),
        const SizedBox(height: 22),
        const SectionTitle(label: '常用操作'),
        const SizedBox(height: 9),
        AdaptiveActionRow(
          children: [
            QuickAction(
              icon: Icons.send_outlined,
              label: '发给搭子',
              onTap: () => sendHomeGif(context, controller),
            ),
            QuickAction(
              icon: Icons.theater_comedy_outlined,
              label: '小剧场',
              onTap: () => startTheater(context, controller),
            ),
            QuickAction(
              icon: Icons.touch_app_outlined,
              label: '说句话',
              onTap: () => runAction(
                context,
                controller.service('interact'),
                (_) => '桌宠回应你了',
              ),
            ),
            QuickAction(
              icon: Icons.shuffle_rounded,
              label: '切换形象',
              onTap: () => runAction(
                context,
                controller.service('next'),
                (_) => '已换好桌宠',
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
              detail: '启动后会待在屏幕边角。点它打开菜单，也可以从这里发给搭子。',
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
        : '点桌宠打开菜单，或直接把当前形象发给搭子。';
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
          if (!inactiveTouch) ...[
            SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: () => sendHomeGif(context, controller),
                icon: const Icon(Icons.send_outlined),
                label: const Text('发给搭子'),
              ),
            ),
            const SizedBox(height: 9),
          ],
          AdaptiveActionRow(
            children: [
              OutlinedButton.icon(
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
              AdaptiveActionRow(
                children: [
                  Align(
                    alignment: Alignment.centerLeft,
                    child: Text(
                      selected?.name ?? '尚未选择',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
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
        const SectionTitle(label: '图鉴目录'),
        const SizedBox(height: 8),
        LibraryPanel(controller: controller),
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
                  'lively': '热闹 · 10-30 分钟',
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
        const SizedBox(height: 22),
        const SectionTitle(label: '小剧场'),
        const SizedBox(height: 8),
        TheaterPanel(controller: controller),
        const SizedBox(height: 22),
        const SectionTitle(label: '提醒'),
        const SizedBox(height: 8),
        ReminderPanel(controller: controller),
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
      final trial = widget.controller.liveTrialSeconds > 0;
      return PageScroll(
        children: [
          const PageIntro(
            title: '搭子联机',
            subtitle: '配对后，两台设备可以互发当前桌宠。',
          ),
          Panel(
            color: trial ? _mint : _coralSoft,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(
                  trial ? Icons.mail_outline : Icons.lock_outline,
                  color: trial ? _brand : _coral,
                  size: 28,
                ),
                const SizedBox(height: 12),
                Text(
                  trial ? '体验期先看看怎么发' : '正式激活后可以互发',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                const SizedBox(height: 5),
                Text(
                  trial
                      ? '现在可以先启动桌宠、换一只、点它说话。把当前 GIF 发给另一台设备，需要正式激活。'
                      : '激活后即可配对，把当前桌宠发给搭子。Android 设备单独计为一台。',
                ),
                const SizedBox(height: 14),
                FilledButton.icon(
                  onPressed: () => tabToAccount(context),
                  icon: const Icon(Icons.key_outlined),
                  label: Text(trial ? '去看看激活' : '前往激活'),
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
    final trialEnded = !snapshot.activated && controller.liveTrialSeconds <= 0;
    return PageScroll(
      onRefresh: () async {
        await controller.refresh(checkTrial: true);
        await controller.refreshSiteLinks();
      },
      children: [
        PageIntro(
          title: '我的',
          subtitle: snapshot.activated
              ? '这台 Android 已经解锁完整功能。'
              : '想给搭子发一张 GIF，或继续小剧场和提醒时，再输入激活码。',
        ),
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
                      snapshot.activated
                          ? '此设备已激活'
                          : trialEnded
                          ? '五分钟体验结束啦'
                          : '体验与激活',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      snapshot.activated
                          ? '授权尾号 · ${snapshot.installationSuffix}'
                          : trialText(controller.liveTrialSeconds),
                      style: Theme.of(context).textTheme.bodyMedium,
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
        if (!snapshot.activated) ...[
          if (trialEnded) ...[
            const SizedBox(height: 14),
            const AdaptiveActionRow(
              children: [
                _KeepPauseCard(
                  title: '继续留下',
                  color: Color(0xffeef6f3),
                  border: Color(0xffc7ddd6),
                  titleColor: Color(0xff167d6c),
                  items: ['桌宠还在角落', '走动、拖动、换一只', '内置图鉴和 3 个自定义 GIF'],
                ),
                _KeepPauseCard(
                  title: '想接着玩再回来',
                  color: Color(0xfffbf6f1),
                  border: Color(0xffe6d6c6),
                  titleColor: Color(0xff9a6a3a),
                  items: ['给搭子发 GIF', '小剧场、提醒', '词包和外部图鉴'],
                ),
              ],
            ),
          ],
          const SizedBox(height: 14),
          Panel(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('已有 6 位激活码', style: Theme.of(context).textTheme.titleMedium),
                const SizedBox(height: 10),
                FilledButton.icon(
                  onPressed: () => _activateWithPrompt(context),
                  icon: const Icon(Icons.lock_open_outlined),
                  label: Text(trialEnded ? '解锁完整功能' : '输入激活码'),
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
          const SizedBox(height: 14),
          Panel(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('还没有激活码', style: Theme.of(context).textTheme.titleMedium),
                const SizedBox(height: 10),
                LayoutBuilder(
                  builder: (context, constraints) {
                    final stacked = constraints.maxWidth < 420;
                    final qr = ClipRRect(
                      borderRadius: BorderRadius.circular(8),
                      child: Image.asset(
                        'assets/contact-author-wechat.png',
                        width: stacked ? double.infinity : 148,
                        height: stacked ? 168 : 148,
                        fit: BoxFit.contain,
                        errorBuilder: (_, __, ___) => SizedBox(
                          width: stacked ? double.infinity : 148,
                          height: stacked ? 168 : 148,
                          child: const ColoredBox(
                            color: _mint,
                            child: Icon(Icons.qr_code_2, color: _brand, size: 48),
                          ),
                        ),
                      ),
                    );
                    final copy = Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const Text('加作者微信，备注「桌搭子」，按提示领取激活码。一般当天回。'),
                        const SizedBox(height: 10),
                        const Text('微信号', style: TextStyle(color: _muted, fontSize: 12)),
                        SelectableText(
                          snapshot.wechatId,
                          style: const TextStyle(
                            fontFamily: 'monospace',
                            fontSize: 16,
                            fontWeight: FontWeight.w800,
                            color: _ink,
                          ),
                        ),
                        const SizedBox(height: 12),
                        AdaptiveActionRow(
                          children: [
                            OutlinedButton.icon(
                              onPressed: () => runAction(
                                context,
                                controller.copyText(snapshot.wechatId),
                                (_) => '微信号已复制，备注「桌搭子」即可',
                              ),
                              icon: const Icon(Icons.copy_outlined),
                              label: const Text('复制微信号'),
                            ),
                            if (snapshot.xianyuUrl.isNotEmpty)
                              OutlinedButton.icon(
                                onPressed: () => runAction(
                                  context,
                                  controller.openUrl(snapshot.xianyuUrl),
                                  (_) => '',
                                ),
                                icon: const Icon(Icons.storefront_outlined),
                                label: const Text('去闲鱼看看'),
                              ),
                          ],
                        ),
                      ],
                    );
                    if (stacked) {
                      return Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [qr, const SizedBox(height: 12), copy],
                      );
                    }
                    return Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        qr,
                        const SizedBox(width: 14),
                        Expanded(child: copy),
                      ],
                    );
                  },
                ),
                const SizedBox(height: 12),
                TextButton.icon(
                  onPressed: () => runAction(
                    context,
                    controller.openUrl(snapshot.websiteUrl),
                    (_) => '',
                  ),
                  icon: const Icon(Icons.public_outlined),
                  label: const Text('去官网看看'),
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
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              InfoRow(label: '当前版本', value: 'Android v${snapshot.version}'),
              const SizedBox(height: 12),
              Text(
                controller.update.message,
                style: Theme.of(context).textTheme.titleMedium,
              ),
              if (controller.update.notes.isNotEmpty) ...[
                const SizedBox(height: 6),
                Text(controller.update.notes),
              ],
              if (controller.update.downloading ||
                  controller.update.downloaded) ...[
                const SizedBox(height: 12),
                ClipRRect(
                  borderRadius: BorderRadius.circular(99),
                  child: LinearProgressIndicator(
                    minHeight: 6,
                    value: controller.update.progress <= 0
                        ? null
                        : controller.update.progress / 100,
                  ),
                ),
              ],
              const SizedBox(height: 12),
              SwitchListTile.adaptive(
                contentPadding: EdgeInsets.zero,
                title: const Text('自动检查更新'),
                subtitle: const Text('打开应用后先看一下有没有新版本'),
                value: snapshot.autoCheckUpdates,
                onChanged: (value) =>
                    controller.setSetting(settingAutoCheckUpdates, value),
              ),
              const SizedBox(height: 4),
              AdaptiveActionRow(
                children: [
                  FilledButton.icon(
                    onPressed: controller.update.busy || controller.busy
                        ? null
                        : () => runAction(
                            context,
                            controller.checkUpdate(),
                            (_) => controller.update.message,
                          ),
                    icon: const Icon(Icons.system_update_alt_outlined),
                    label: const Text('检查更新'),
                  ),
                  if (controller.update.available &&
                      snapshot.ignoredUpdateVersion !=
                          controller.update.version)
                    OutlinedButton.icon(
                      onPressed: controller.busy
                          ? null
                          : () => runAction(
                              context,
                              controller.downloadUpdate(),
                              (_) => controller.update.message,
                            ),
                      icon: const Icon(Icons.download_outlined),
                      label: const Text('下载更新'),
                    ),
                  if (controller.update.downloaded)
                    FilledButton.icon(
                      onPressed: controller.busy
                          ? null
                          : () => runAction(
                              context,
                              controller.installUpdate(),
                              (_) => snapshot.canInstallPackages
                                  ? '请在系统安装页确认更新'
                                  : '',
                            ),
                      icon: const Icon(Icons.install_mobile_outlined),
                      label: const Text('安装更新'),
                    ),
                  if (controller.update.available &&
                      snapshot.ignoredUpdateVersion !=
                          controller.update.version)
                    TextButton(
                      onPressed: controller.busy
                          ? null
                          : () => runAction(
                              context,
                              controller.ignoreUpdate(),
                              (_) => '已忽略该版本',
                            ),
                      child: const Text('忽略该版本'),
                    ),
                ],
              ),
              if (!snapshot.canInstallPackages &&
                  controller.update.downloaded) ...[
                const SizedBox(height: 8),
                const Text('安装前需要允许桌搭子安装应用。点安装更新后会先打开系统设置。'),
              ],
              const Divider(height: 22),
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

class _KeepPauseCard extends StatelessWidget {
  const _KeepPauseCard({
    required this.title,
    required this.color,
    required this.border,
    required this.titleColor,
    required this.items,
  });
  final String title;
  final Color color;
  final Color border;
  final Color titleColor;
  final List<String> items;

  @override
  Widget build(BuildContext context) => Container(
    width: double.infinity,
    padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
    decoration: BoxDecoration(
      color: color,
      border: Border.all(color: border),
      borderRadius: BorderRadius.circular(8),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          title,
          style: TextStyle(
            color: titleColor,
            fontSize: 13,
            fontWeight: FontWeight.w800,
          ),
        ),
        const SizedBox(height: 8),
        for (final item in items)
          Padding(
            padding: const EdgeInsets.only(bottom: 4),
            child: Text(
              '· $item',
              style: TextStyle(color: titleColor.withValues(alpha: 0.92), fontSize: 12, height: 1.35),
            ),
          ),
      ],
    ),
  );
}

class LibraryPanel extends StatelessWidget {
  const LibraryPanel({required this.controller, super.key});
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final snapshot = controller.snapshot;
    final libraries = snapshot.libraries;
    final selected = snapshot.selectedLibrary;
    final canBind = snapshot.premium && libraries.length < 3;
    return Panel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          AdaptiveActionRow(
            children: [
              Align(
                alignment: Alignment.centerLeft,
                child: Text(
                  selected == null
                      ? '当前使用内置图鉴'
                      : selected.gifCount < 0
                      ? '正在使用 ${selected.name}'
                      : '正在使用 ${selected.name} · ${selected.gifCount} 个 GIF',
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              OutlinedButton.icon(
                onPressed: canBind
                    ? () => runAction(context, controller.importLibrary(), (_) {
                        final count = controller.snapshot.libraryGifCount;
                        if (count <= 0) return '目录已绑定，但没有找到 GIF';
                        return '已绑定目录，扫描到 $count 个 GIF';
                      })
                    : null,
                icon: Icon(
                  snapshot.premium
                      ? Icons.folder_open_outlined
                      : Icons.lock_outline,
                ),
                label: Text(
                  snapshot.premium
                      ? (libraries.length >= 3 ? '已满 3 个目录' : '绑定目录')
                      : '体验后可绑定',
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            snapshot.premium
                ? '选择手机里的 GIF 文件夹，目录中的动图都会进入轮换池，最多 3 个目录、每个最多 500 张。'
                : '体验或正式激活后，可以把相册或文件里的整个 GIF 文件夹当作图鉴。',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          if (libraries.isNotEmpty) ...[
            const SizedBox(height: 10),
            for (final library in libraries)
              ListTile(
                contentPadding: EdgeInsets.zero,
                selected: library.id == snapshot.activeLibrary,
                title: Text(library.name),
                subtitle: Text(library.detail),
                leading: Icon(
                  library.id == snapshot.activeLibrary
                      ? Icons.folder
                      : Icons.folder_outlined,
                  color: _brand,
                ),
                trailing: IconButton(
                  tooltip: '删除所选',
                  onPressed: () =>
                      confirmDeleteLibrary(context, controller, library),
                  icon: const Icon(Icons.delete_outline, color: _coral),
                ),
                onTap: snapshot.premium
                    ? () => runAction(
                        context,
                        controller.selectLibrary(library.id),
                        (_) => '已切换到 ${library.name}',
                      )
                    : null,
              ),
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                onPressed: selected == null
                    ? null
                    : () => runAction(
                        context,
                        controller.selectLibrary('builtin'),
                        (_) => '已切回内置图鉴',
                      ),
                icon: const Icon(Icons.pets_outlined),
                label: const Text('使用内置图鉴'),
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class TheaterPanel extends StatelessWidget {
  const TheaterPanel({required this.controller, super.key});
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final snapshot = controller.snapshot;
    final scripts = snapshot.theaterScripts;
    return Panel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SwitchListTile.adaptive(
            contentPadding: EdgeInsets.zero,
            title: const Text('自动随机上演'),
            subtitle: Text(
              snapshot.premium ? '按间隔从剧本池里抽一场' : '体验或正式激活后可开启',
            ),
            value: snapshot.theaterEnabled,
            onChanged: snapshot.premium
                ? (value) => controller.setSetting(settingTheaterEnabled, value)
                : null,
          ),
          const Divider(height: 10),
          ChoiceRow(
            label: '上演间隔',
            value: '${snapshot.theaterInterval}',
            options: const {
              '60': '1 分钟',
              '180': '3 分钟',
              '300': '5 分钟',
              '600': '10 分钟',
              '1800': '30 分钟',
            },
            enabled: snapshot.premium,
            onChanged: (value) =>
                controller.setSetting(settingTheaterInterval, int.parse(value)),
          ),
          const SizedBox(height: 12),
          SizedBox(
            width: double.infinity,
            child: FilledButton.icon(
              onPressed: snapshot.premium
                  ? () => startTheater(context, controller)
                  : null,
              icon: const Icon(Icons.theater_comedy_outlined),
              label: Text(
                !snapshot.running
                    ? '先启动桌宠再演'
                    : snapshot.premium
                    ? '立即上演'
                    : '体验或激活后可用',
              ),
            ),
          ),
          const SizedBox(height: 14),
          Text(
            scripts.isEmpty
                ? '还没有导入剧本，先用内置的三场小剧场。'
                : '已导入 ${scripts.length}/10 个剧本 · 每次演出随机抽取',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          if (scripts.isNotEmpty) ...[
            const SizedBox(height: 10),
            for (final script in scripts)
              ListTile(
                contentPadding: EdgeInsets.zero,
                title: Text(script.name),
                subtitle: Text('${script.sceneCount} 轮对白'),
                trailing: IconButton(
                  tooltip: '删除剧本',
                  onPressed: snapshot.premium
                      ? () => runAction(
                          context,
                          controller.deleteTheaterScript(script.id),
                          (_) => '剧本已删除',
                        )
                      : null,
                  icon: const Icon(Icons.delete_outline, color: _coral),
                ),
              ),
          ],
          const SizedBox(height: 8),
          AdaptiveActionRow(
            children: [
              OutlinedButton.icon(
                onPressed: snapshot.premium
                    ? () => runAction(
                        context,
                        controller.importTheaterScript(),
                        (_) => '剧本已导入',
                      )
                    : null,
                icon: const Icon(Icons.file_upload_outlined),
                label: const Text('导入剧本'),
              ),
              OutlinedButton.icon(
                onPressed: () => showTheaterGuide(context),
                icon: const Icon(Icons.help_outline),
                label: const Text('格式说明'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class ReminderPanel extends StatelessWidget {
  const ReminderPanel({required this.controller, super.key});
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final snapshot = controller.snapshot;
    final reminders = snapshot.reminders;
    return Panel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            !snapshot.premium
                ? '体验或正式激活后，桌宠会在设定时间提醒你。'
                : reminders.isEmpty
                ? '暂无提醒'
                : '共 ${reminders.length} 个提醒',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          const SizedBox(height: 12),
          SizedBox(
            width: double.infinity,
            child: FilledButton.icon(
              onPressed: snapshot.premium
                  ? () => editReminder(context, controller)
                  : null,
              icon: const Icon(Icons.add_alert_outlined),
              label: const Text('新建提醒'),
            ),
          ),
          if (reminders.isNotEmpty) ...[
            const SizedBox(height: 8),
            for (final reminder in reminders)
              ListTile(
                contentPadding: EdgeInsets.zero,
                leading: Icon(
                  reminder.enabled
                      ? Icons.notifications_active_outlined
                      : Icons.notifications_off_outlined,
                  color: reminder.enabled ? _brand : _muted,
                ),
                title: Text(reminder.message),
                subtitle: Text(
                  '${_formatReminderTime(reminder)} · ${_emotionLabel(reminder.emotion)}'
                  '${reminder.repeatDaily ? ' · 每天' : ''}'
                  '${reminder.enabled ? '' : ' · 已关闭'}',
                ),
                trailing: const Icon(Icons.chevron_right, color: _muted),
                onTap: snapshot.premium
                    ? () => editReminder(context, controller, reminder: reminder)
                    : null,
              ),
          ],
        ],
      ),
    );
  }
}

class PageScroll extends StatelessWidget {
  const PageScroll({required this.children, this.onRefresh, super.key});
  final List<Widget> children;
  final Future<void> Function()? onRefresh;
  @override
  Widget build(BuildContext context) {
    final compact = MediaQuery.sizeOf(context).width < 360;
    final list = ListView(
      padding: EdgeInsets.fromLTRB(compact ? 14 : 20, 10, compact ? 14 : 20, 26),
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
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
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

class AdaptiveActionRow extends StatelessWidget {
  const AdaptiveActionRow({required this.children, super.key});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final width = MediaQuery.sizeOf(context).width;
    final compact = width < 360;
    if (compact) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (var i = 0; i < children.length; i++) ...[
            if (i > 0) const SizedBox(height: 8),
            children[i],
          ],
        ],
      );
    }
    if (children.length > 3 && width < 560) {
      final rows = <Widget>[];
      for (var i = 0; i < children.length; i += 2) {
        final pair = children.sublist(i, i + 2 > children.length ? children.length : i + 2);
        rows.add(
          Row(
            children: [
              for (var j = 0; j < pair.length; j++) ...[
                if (j > 0) const SizedBox(width: 9),
                Expanded(child: pair[j]),
              ],
              if (pair.length == 1) const Expanded(child: SizedBox.shrink()),
            ],
          ),
        );
      }
      return Column(
        children: [
          for (var i = 0; i < rows.length; i++) ...[
            if (i > 0) const SizedBox(height: 8),
            rows[i],
          ],
        ],
      );
    }
    return Row(
      children: [
        for (var i = 0; i < children.length; i++) ...[
          if (i > 0) const SizedBox(width: 9),
          Expanded(child: children[i]),
        ],
      ],
    );
  }
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
      padding: const EdgeInsets.symmetric(vertical: 13, horizontal: 8),
      minimumSize: const Size(0, 68),
    ),
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 22),
        const SizedBox(height: 4),
        Text(
          label,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          textAlign: TextAlign.center,
          style: const TextStyle(fontSize: 12),
        ),
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

Future<void> confirmDeleteLibrary(
  BuildContext context,
  AppController controller,
  LibraryItem library,
) async {
  final confirmed = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: const Text('删除图鉴目录'),
      content: Text('解除绑定「${library.name}」？目录里的文件还在手机上，只是不再作为桌宠图鉴。'),
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
    await runAction(
      context,
      controller.deleteLibrary(library.id),
      (_) => '已解除绑定',
    );
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
              child: LayoutBuilder(
                builder: (context, constraints) {
                  final columns = constraints.maxWidth < 360
                      ? 1
                      : constraints.maxWidth >= 720
                      ? 3
                      : 2;
                  return GridView.builder(
                    padding: const EdgeInsets.fromLTRB(20, 4, 20, 24),
                    gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
                      crossAxisCount: columns,
                      crossAxisSpacing: 10,
                      mainAxisSpacing: 10,
                      childAspectRatio: columns == 1 ? 3.1 : 1.55,
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

String trialText(int seconds) {
  if (seconds <= 0) return '体验已结束，基础桌宠仍可使用';
  return '完整体验还剩 ${trialClock(seconds)}';
}

void tabToAccount(BuildContext context) {
  final shell = context.findAncestorStateOfType<_AppShellState>();
  shell?.selectTab(4);
}

void tabToCompanion(BuildContext context) {
  final shell = context.findAncestorStateOfType<_AppShellState>();
  shell?.selectTab(3);
}

Future<void> startTheater(
  BuildContext context,
  AppController controller,
) async {
  final snapshot = controller.snapshot;
  if (!snapshot.overlayAllowed) {
    await runAction(
      context,
      controller.requestOverlayPermission(),
      (_) => '打开悬浮窗后回来，就能上演小剧场',
    );
    return;
  }
  if (!snapshot.running) {
    await runAction(context, controller.service('start'), (_) => '');
    if (!controller.snapshot.running) return;
  }
  if (!controller.snapshot.premium) {
    if (context.mounted) tabToAccount(context);
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('小剧场需要体验或正式激活')),
      );
    }
    return;
  }
  await runAction(
    context,
    controller.service('theater'),
    (_) => '小剧场已经开演',
  );
}

Future<void> showTheaterGuide(BuildContext context) async {
  await showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    backgroundColor: _page,
    builder: (context) => SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 18, 20, 24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('小剧场剧本格式', style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 8),
            const Text(
              '每个 JSON 剧本包含 3–5 轮双人对白。导入后每次演出会从剧本池随机抽取。',
            ),
            const SizedBox(height: 12),
            const SelectableText(
              '{"name":"周一摸鱼大会","scenes":[\n'
              '  {"main":"我宣布，今天的任务是准时下班。","companion":"收到，我负责盯住时钟。"},\n'
              '  {"main":"要是临时又来需求呢？","companion":"先深呼吸，再把优先级问清楚。"},\n'
              '  {"main":"计划听起来很稳。","companion":"最后记得保存文件，我们撤！"}\n'
              ']}',
              style: TextStyle(fontFamily: 'monospace', fontSize: 12, height: 1.4),
            ),
            const SizedBox(height: 12),
            const Text('也兼容 actorA / actorB 字段。每句最多 60 个字符。'),
            const SizedBox(height: 16),
            Align(
              alignment: Alignment.centerRight,
              child: FilledButton(
                onPressed: () => Navigator.pop(context),
                child: const Text('知道了'),
              ),
            ),
          ],
        ),
      ),
    ),
  );
}

const _emotionLabels = <String, String>{
  'happy': '开心',
  'cheer': '加油',
  'shy': '害羞',
  'surprised': '惊讶',
  'angry': '生气',
  'confused': '疑惑',
  'sad': '难过',
  'sleepy': '困倦',
  'calm': '安静',
};

String _emotionLabel(String emotion) => _emotionLabels[emotion] ?? '开心';

String _two(int value) => value.toString().padLeft(2, '0');

String _formatReminderTime(ReminderItem reminder) {
  final time = reminder.localTime;
  return '${time.month}/${time.day} ${_two(time.hour)}:${_two(time.minute)}';
}

Future<void> editReminder(
  BuildContext context,
  AppController controller, {
  ReminderItem? reminder,
}) async {
  var enabled = reminder?.enabled ?? true;
  var repeatDaily = reminder?.repeatDaily ?? false;
  var emotion = reminder?.emotion ?? 'happy';
  var expressionPetId = reminder?.expressionPetId ?? '';
  var at = reminder?.localTime ?? DateTime.now().add(const Duration(minutes: 10));
  final message = TextEditingController(text: reminder?.message ?? '休息一下吧');
  final saved = await showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    backgroundColor: _page,
    builder: (context) {
      return StatefulBuilder(
        builder: (context, setModalState) {
          final inset = MediaQuery.viewInsetsOf(context).bottom;
          return Padding(
            padding: EdgeInsets.fromLTRB(20, 18, 20, 20 + inset),
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    reminder == null ? '新建提醒' : '编辑提醒',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                  const SizedBox(height: 14),
                  TextField(
                    controller: message,
                    maxLength: 40,
                    decoration: const InputDecoration(labelText: '提醒内容'),
                  ),
                  const SizedBox(height: 8),
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('提醒时间'),
                    subtitle: Text(
                      '${at.year}-${_two(at.month)}-${_two(at.day)} ${_two(at.hour)}:${_two(at.minute)}',
                    ),
                    trailing: const Icon(Icons.schedule_outlined),
                    onTap: () async {
                      final date = await showDatePicker(
                        context: context,
                        initialDate: at,
                        firstDate: DateTime.now().subtract(const Duration(days: 1)),
                        lastDate: DateTime.now().add(const Duration(days: 365)),
                      );
                      if (date == null || !context.mounted) return;
                      final time = await showTimePicker(
                        context: context,
                        initialTime: TimeOfDay.fromDateTime(at),
                      );
                      if (time == null) return;
                      setModalState(() {
                        at = DateTime(date.year, date.month, date.day, time.hour, time.minute);
                      });
                    },
                  ),
                  DropdownButtonFormField<String>(
                    initialValue: _emotionLabels.containsKey(emotion) ? emotion : 'happy',
                    decoration: const InputDecoration(labelText: '情绪'),
                    items: _emotionLabels.entries
                        .map(
                          (item) => DropdownMenuItem(
                            value: item.key,
                            child: Text(item.value),
                          ),
                        )
                        .toList(),
                    onChanged: (value) {
                      if (value != null) setModalState(() => emotion = value);
                    },
                  ),
                  const SizedBox(height: 10),
                  DropdownButtonFormField<String>(
                    initialValue: expressionPetId,
                    decoration: const InputDecoration(
                      labelText: '指定表情 GIF（可选）',
                      helperText: '到点时桌宠会短暂换成这只',
                    ),
                    items: [
                      const DropdownMenuItem(value: '', child: Text('不更换')),
                      for (final pet in controller.snapshot.pets)
                        DropdownMenuItem(value: pet.id, child: Text(pet.name)),
                    ],
                    onChanged: (value) {
                      setModalState(() => expressionPetId = value ?? '');
                    },
                  ),
                  SwitchListTile.adaptive(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('启用提醒'),
                    value: enabled,
                    onChanged: (value) => setModalState(() => enabled = value),
                  ),
                  SwitchListTile.adaptive(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('每天重复'),
                    value: repeatDaily,
                    onChanged: (value) => setModalState(() => repeatDaily = value),
                  ),
                  const SizedBox(height: 8),
                  AdaptiveActionRow(
                    children: [
                      if (reminder != null)
                        OutlinedButton(
                          onPressed: () => Navigator.pop(context, false),
                          child: const Text('删除提醒'),
                        ),
                      FilledButton(
                        onPressed: () => Navigator.pop(context, true),
                        child: const Text('保存提醒'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          );
        },
      );
    },
  );
  if (!context.mounted) {
    message.dispose();
    return;
  }
  if (saved == false && reminder != null) {
    await runAction(context, controller.deleteReminder(reminder.id), (_) => '提醒已删除');
  } else if (saved == true) {
    await runAction(
      context,
      controller.saveReminder(
        id: reminder?.id,
        enabled: enabled,
        at: at.millisecondsSinceEpoch,
        message: message.text,
        emotion: emotion,
        expressionPetId: expressionPetId,
        repeatDaily: repeatDaily,
      ),
      (_) => '提醒已保存',
    );
  }
  message.dispose();
}

Future<void> sendHomeGif(
  BuildContext context,
  AppController controller,
) async {
  final snapshot = controller.snapshot;
  if (!snapshot.overlayAllowed) {
    await runAction(
      context,
      controller.requestOverlayPermission(),
      (_) => '打开悬浮窗后回来，就能启动桌宠',
    );
    return;
  }
  if (!snapshot.running) {
    await runAction(context, controller.service('start'), (_) => '');
    if (!controller.snapshot.running) return;
  }
  if (!controller.snapshot.activated) {
    if (context.mounted) tabToCompanion(context);
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('发给搭子需要正式激活，先看看怎么配对')),
      );
    }
    return;
  }
  await runAction<String>(
    context,
    controller.sendCompanion(),
    (recipient) => '已发送给 $recipient',
  );
}
