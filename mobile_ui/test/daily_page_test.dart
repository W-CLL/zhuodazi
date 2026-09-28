import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:zhuodazi_ui/app_controller.dart';
import 'package:zhuodazi_ui/host_api.dart';
import 'package:zhuodazi_ui/main.dart';

class DailyHost extends HostApi {
  final moods = <String>[];
  final queries = <Map<String, Object>>[];
  final workdayChoices = <Map<String, Object>>[];
  Map<String, Object>? savedRoutine;
  List<int> workdays = [1, 2, 3, 4, 5];
  bool routineEnabled = false;
  int configRefreshes = 0;
  int quizzesStarted = 0;
  bool emptyPreviousWeek = false;
  String storageWarning = '';

  HostSnapshot get state => HostSnapshot({
    'activated': true,
    'premium': true,
    'running': false,
    'overlayAllowed': true,
    'autoCheckUpdates': false,
  });

  @override
  void listen(void Function(String, Object?) onEvent) {}

  @override
  Future<Map<String, dynamic>> dailySnapshot({
    required String period,
    required String month,
    int weeklyOffset = 0,
    int monthlyOffset = 0,
  }) async {
    queries.add({
      'period': period,
      'month': month,
      'weeklyOffset': weeklyOffset,
      'monthlyOffset': monthlyOffset,
    });
    return {
      'storageWarning': storageWarning,
      'stats': {
        'totalInteractions': 4 + moods.length,
        'quizzesAnswered': 4,
        'quizzesCorrect': 3,
        'moodRecords': moods.length,
        'moodDays': moods.isEmpty ? 0 : 1,
        'interactionDays': 2,
        'moodCounts': {
          for (final mood in dailyMoods.keys)
            mood: moods.where((value) => value == mood).length,
        },
      },
      'calendar': <Object>[],
      'weekly': emptyPreviousWeek && weeklyOffset > 0
          ? {
              'state': 'waiting',
              'message': '这一期没有互动，等下次一起留下回忆。',
              'periodStart': '2026-09-04T18:00:00',
              'periodEnd': '2026-09-11T18:00:00',
              'nextDue': '2026-09-25T18:00:00',
            }
          : {
              'state': 'ready',
              'title': '后台周模板 $weeklyOffset',
              'body': '这一周的回忆来自后台配置。',
              'periodStart': '2026-09-11T18:00:00',
              'periodEnd': '2026-09-18T18:00:00',
              'nextDue': '2026-09-25T18:00:00',
            },
      'monthly': {
        'state': 'ready',
        'title': '后台月模板 $monthlyOffset',
        'body': '完整月份的回忆来自后台配置。',
        'periodStart': '2026-08-01T00:00:00',
        'periodEnd': '2026-09-01T00:00:00',
        'nextDue': '2026-10-01T09:00:00',
      },
      'routine': {
        'enabled': routineEnabled,
        'workdays': workdays,
        'time': '17:00',
      },
    };
  }

  @override
  Future<HostSnapshot> dailyMood(String mood) async {
    moods.add(mood);
    return state;
  }

  @override
  Future<HostSnapshot> dailyWorkday(
    String choice, {
    int snoozeMinutes = 0,
  }) async {
    workdayChoices.add({'choice': choice, 'snoozeMinutes': snoozeMinutes});
    return state;
  }

  @override
  Future<HostSnapshot> dailyQuiz() async {
    quizzesStarted++;
    return state;
  }

  @override
  Future<HostSnapshot> dailyRefreshConfig() async {
    configRefreshes++;
    return state;
  }

  @override
  Future<HostSnapshot> dailySaveRoutine({
    required bool enabled,
    required List<int> workdays,
    required String time,
  }) async {
    savedRoutine = {'enabled': enabled, 'workdays': workdays, 'time': time};
    return state;
  }
}

void main() {
  Future<void> pumpDaily(
    WidgetTester tester,
    DailyHost host, {
    Size size = const Size(412, 915),
  }) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    if (Platform.environment['DESKPET_REVIEW_DIR'] != null) {
      for (final font in {
        'sans-serif':
            '${Platform.environment['WINDIR'] ?? 'C:/Windows'}/Fonts/msyh.ttc',
        'MaterialIcons':
            'build/unit_test_assets/fonts/MaterialIcons-Regular.otf',
      }.entries) {
        final file = File(font.value);
        if (file.existsSync()) {
          final loader = FontLoader(font.key)
            ..addFont(
              Future.value(ByteData.sublistView(file.readAsBytesSync())),
            );
          await loader.load();
        }
      }
    }
    final controller = AppController(api: host)..snapshot = host.state;
    addTearDown(controller.dispose);
    await tester.pumpWidget(
      RepaintBoundary(
        key: const Key('daily-review'),
        child: Builder(
          builder: (context) {
            final application =
                const ZhuoDaziApp().build(context) as MaterialApp;
            return MaterialApp(
              debugShowCheckedModeBanner: false,
              theme: application.theme,
              home: DailyPage(controller: controller),
            );
          },
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  Future<void> reveal(WidgetTester tester, Finder target) async {
    final scrollable = find
        .descendant(
          of: find.byType(PageScroll),
          matching: find.byType(Scrollable),
        )
        .first;
    if (target.evaluate().isEmpty) {
      tester.state<ScrollableState>(scrollable).position.jumpTo(0);
      await tester.pumpAndSettle();
    }
    await tester.scrollUntilVisible(target, 260, scrollable: scrollable);
    await tester.ensureVisible(target);
    await tester.pumpAndSettle();
  }

  Future<void> screenshot(WidgetTester tester, String name) async {
    final directory = Platform.environment['DESKPET_REVIEW_DIR'];
    if (directory == null) return;
    await tester.runAsync(() async {
      final boundary = tester.renderObject<RenderRepaintBoundary>(
        find.byKey(const Key('daily-review')),
      );
      final image = await boundary.toImage(pixelRatio: 2);
      final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
      await Directory(directory).create(recursive: true);
      await File('$directory/$name.png')
          .writeAsBytes(bytes!.buffer.asUint8List());
      image.dispose();
    });
  }

  Finder summaryPanel(String title) =>
      find.ancestor(of: find.text(title), matching: find.byType(Panel)).first;

  Finder summaryButton(String title, String label) => find.descendant(
    of: summaryPanel(title),
    matching: find.widgetWithText(TextButton, label),
  );

  testWidgets('all eight moods are available and dismissing does not record', (
    tester,
  ) async {
    final host = DailyHost();
    await pumpDaily(tester, host);
    await tester.tap(find.text('记一下心情'));
    await tester.pumpAndSettle();
    for (final label in ['开心', '一般', '糟糕', '想哭', '疲惫', '烦躁', '平静', '有盼头']) {
      expect(find.widgetWithText(OutlinedButton, label), findsOneWidget);
    }
    await screenshot(tester, 'android-daily-moods');
    await tester.binding.handlePopRoute();
    await tester.pumpAndSettle();
    expect(host.moods, isEmpty);
    expect(find.text('开心 0 次'), findsOneWidget);

    await tester.tap(find.text('记一下心情'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(OutlinedButton, '开心'));
    await tester.pumpAndSettle();
    expect(host.moods, ['happy']);
    expect(host.queries.length, greaterThanOrEqualTo(2));
    expect(find.text('开心 1 次'), findsOneWidget);
    expect(find.text('5 次'), findsOneWidget);
    expect(find.text('日常记录'), findsNothing);
    expect(find.text('删除记录'), findsNothing);
    expect(find.text('修改心情'), findsNothing);
    expect(tester.takeException(), isNull);
    await screenshot(tester, 'android-daily-overview');
  });

  testWidgets(
    'overtime carries the chosen snooze and quiz start adds no mood',
    (tester) async {
      final host = DailyHost();
      await pumpDaily(tester, host);
      await tester.tap(find.text('聊聊下班'));
      await tester.pumpAndSettle();
      for (final label in ['下班啦', '还得加会班', '今天六点下班', '今天休息']) {
        expect(find.widgetWithText(OutlinedButton, label), findsOneWidget);
      }
      await tester.tap(find.text('还得加会班'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('30 分钟后再提醒'));
      await tester.pumpAndSettle();
      expect(host.workdayChoices, [
        {'choice': 'overtime', 'snoozeMinutes': 30},
      ]);
      await tester.tap(find.text('来一道互动题'));
      await tester.pumpAndSettle();
      expect(host.quizzesStarted, 1);
      expect(host.moods, isEmpty);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('routine saves Sunday zero, Saturday six and a 24 hour time', (
    tester,
  ) async {
    final host = DailyHost();
    await pumpDaily(tester, host);
    await reveal(tester, find.text('保存工作节奏'));
    await tester.tap(find.text('下班时来关心我'));
    await tester.pumpAndSettle();
    for (final day in ['周一', '周二', '周三', '周四', '周五', '周六', '周日']) {
      await tester.tap(find.widgetWithText(FilterChip, day));
      await tester.pumpAndSettle();
    }
    await tester.tap(find.text('通常 17:00 下班'));
    await tester.pumpAndSettle();
    final localization = MaterialLocalizations.of(
      tester.element(find.byType(TimePickerDialog)),
    );
    await tester.tap(find.byTooltip(localization.inputTimeModeButtonLabel));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField).first, '18');
    await tester.enterText(find.byType(TextField).last, '30');
    await tester.tap(find.text(localization.okButtonLabel));
    await tester.pumpAndSettle();
    expect(find.text('通常 18:30 下班'), findsOneWidget);
    await tester.tap(find.text('保存工作节奏'));
    await tester.pumpAndSettle();
    expect(host.savedRoutine, {
      'enabled': true,
      'workdays': [0, 6],
      'time': '18:30',
    });
    expect(find.text('工作节奏已保存'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await screenshot(tester, 'android-daily-routine');
  });

  testWidgets('enabled routine requires at least one working day', (
    tester,
  ) async {
    final host = DailyHost()
      ..workdays = [1]
      ..routineEnabled = true;
    await pumpDaily(tester, host);
    await reveal(tester, find.text('保存工作节奏'));
    await tester.tap(find.widgetWithText(FilterChip, '周一'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('保存工作节奏'));
    await tester.pumpAndSettle();
    expect(host.savedRoutine, isNull);
    expect(find.text('请至少选择一个工作日'), findsOneWidget);
  });

  testWidgets(
    'weekly and monthly navigation use independent completed offsets',
    (tester) async {
      final host = DailyHost();
      await pumpDaily(tester, host);
      await reveal(tester, find.text('周总结'));
      expect(
        tester.widget<TextButton>(summaryButton('周总结', '下一期')).onPressed,
        isNull,
      );
      await tester.tap(summaryButton('周总结', '上一期'));
      await tester.pumpAndSettle();
      expect(host.queries.last['weeklyOffset'], 1);
      expect(host.queries.last['monthlyOffset'], 0);
      expect(find.text('后台周模板 1'), findsOneWidget);
      await reveal(tester, find.text('月总结'));
      expect(
        tester.widget<TextButton>(summaryButton('月总结', '下一期')).onPressed,
        isNull,
      );
      await tester.tap(summaryButton('月总结', '上一期'));
      await tester.pumpAndSettle();
      expect(host.queries.last['weeklyOffset'], 1);
      expect(host.queries.last['monthlyOffset'], 1);
      expect(find.text('后台月模板 1'), findsOneWidget);
      expect(find.text('2026-08-01 — 2026-08-31 · 已结束'), findsOneWidget);
      await screenshot(tester, 'android-daily-monthly');
      await tester.tap(summaryButton('月总结', '最近一期'));
      await tester.pumpAndSettle();
      expect(host.queries.last['weeklyOffset'], 1);
      expect(host.queries.last['monthlyOffset'], 0);
      await reveal(tester, find.text('周总结'));
      await tester.tap(summaryButton('周总结', '下一期'));
      await tester.pumpAndSettle();
      expect(host.queries.last['weeklyOffset'], 0);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'empty completed period replaces summary prose with waiting state',
    (tester) async {
      final host = DailyHost()
        ..emptyPreviousWeek = true
        ..storageWarning = '部分日常数据暂时无法读取，请保留本机数据后重试。';
      await pumpDaily(tester, host);
      expect(find.text(host.storageWarning), findsOneWidget);
      await reveal(tester, find.text('周总结'));
      expect(find.text('后台周模板 0'), findsOneWidget);
      await tester.tap(summaryButton('周总结', '上一期'));
      await tester.pumpAndSettle();
      expect(find.text('这一期没有互动，等下次一起留下回忆。'), findsOneWidget);
      expect(find.text('后台周模板 0'), findsNothing);
      expect(find.text('这一周的回忆来自后台配置。'), findsNothing);
      expect(find.text('下一期：2026-09-25 18:00 后可查看'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await screenshot(tester, 'android-daily-summary-empty');
    },
  );

  testWidgets(
    '320 pixel screen can reach every daily section without overflow',
    (tester) async {
      final host = DailyHost();
      await pumpDaily(tester, host, size: const Size(320, 568));
      expect(find.text('我们的日常'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await screenshot(tester, 'android-daily-narrow');
      for (final section in ['这些时刻的心情', '心情月历', '周总结', '月总结', '保存工作节奏']) {
        await reveal(tester, find.text(section));
        expect(find.text(section), findsOneWidget);
        expect(tester.takeException(), isNull);
      }
      expect(find.text('日常记录'), findsNothing);
      expect(find.text('删除记录'), findsNothing);
      expect(find.text('下载离线包'), findsNothing);
      await tester.tap(find.byTooltip('刷新日常'));
      await tester.pumpAndSettle();
      expect(host.configRefreshes, 1);
      expect(tester.takeException(), isNull);
    },
  );
}
