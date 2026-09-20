import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:zhuodazi_ui/app_controller.dart';
import 'package:zhuodazi_ui/host_api.dart';
import 'package:zhuodazi_ui/main.dart';

class ProductHost extends HostApi {
  final calls = <String>[];
  final settings = <String, Object>{};
  bool joined = false;
  bool trial = true;
  bool failHall = false;
  bool populated = true;
  String? sentPet;
  String? sentTarget;
  int sends = 0;

  HostSnapshot get state => HostSnapshot({
    'activated': false,
    'premium': trial,
    'trialSeconds': trial ? 3600 : 0,
    'overlayAllowed': true,
    'running': true,
    'notificationAllowed': true,
    'autoCheckUpdates': false,
    'activePet': 'preview-pet',
    'pets': [
      {'id': 'preview-pet', 'name': '月薪喵'},
    ],
    'dailySpeechEnabled': settings[settingDailySpeech] ?? true,
    'quietUntilUtc': settings[settingQuietUntil] ?? 0,
    'randomPet': false,
    'companionHallEnabled': true,
    'interactionSyncError': '网络暂时不可用',
    'interactionOnlineCount': 12,
    'interactionCachedCount': 18,
    'interactionLastSyncAt': 1700000000000,
  });

  Map<String, dynamic> get profile => {
    'displayName': '周五的小猫',
    'pairingCode': '',
    'partner': null,
    'hallEnabled': joined,
  };
  @override
  void listen(void Function(String, Object?) onEvent) {}
  @override
  Future<HostSnapshot> snapshot() async {
    calls.add('snapshot');
    return state;
  }

  @override
  Future<HostSnapshot> siteLinks() async {
    calls.add('siteLinks');
    return state;
  }

  @override
  Future<HostSnapshot> checkTrial() async {
    calls.add('checkTrial');
    return state;
  }

  @override
  Future<Uint8List?> petGif(String petId) async => null;
  @override
  Future<HostSnapshot> setSetting(String key, Object value) async {
    settings[key] = value;
    return state;
  }

  @override
  Future<Map<String, dynamic>> companionRefresh() async {
    calls.add('profile');
    return profile;
  }

  @override
  Future<Map<String, dynamic>> companionHallRefresh() async {
    calls.add('hall');
    if (failHall) throw const HostFailure('暂时离线，请稍后重试');
    return {
      'enabled': joined,
      'people': !populated || !joined
          ? <Object>[]
          : [
              {
                'id': 'trial:installation-1',
                'displayName': '小橘今天不加班',
                'online': true,
              },
              {
                'id': 'trial:installation-2',
                'displayName': '正在摸鱼的团子',
                'online': true,
              },
              {'id': 'user-3', 'displayName': '认真生活的小布', 'online': true},
            ],
    };
  }

  @override
  Future<Map<String, dynamic>> companionHallSet(bool enabled) async {
    calls.add('join:$enabled');
    joined = enabled;
    return profile;
  }

  @override
  Future<Map<String, dynamic>> companionUpdateName(String name) async => {
    ...profile,
    'displayName': name,
  };
  @override
  Future<String> companionHallSend(
    String recipientId,
    String message, {
    required String petId,
  }) async {
    sends++;
    sentTarget = recipientId;
    sentPet = petId;
    return '小橘今天不加班';
  }
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  Future<void> pump(
    WidgetTester tester,
    ProductHost host, {
    Size size = const Size(412, 915),
  }) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final reviewDirectory = Platform.environment['DESKPET_REVIEW_DIR'];
    if (reviewDirectory != null) {
      final font = File(
        '${Platform.environment['WINDIR'] ?? 'C:/Windows'}/Fonts/msyh.ttc',
      );
      if (font.existsSync()) {
        final loader = FontLoader('sans-serif')
          ..addFont(Future.value(ByteData.sublistView(font.readAsBytesSync())));
        await loader.load();
      }
      final icons = File(
        'build/unit_test_assets/fonts/MaterialIcons-Regular.otf',
      );
      if (icons.existsSync()) {
        final loader = FontLoader(
          'MaterialIcons',
        )..addFont(Future.value(ByteData.sublistView(icons.readAsBytesSync())));
        await loader.load();
      }
    }
    await tester.pumpWidget(
      RepaintBoundary(
        key: const Key('review'),
        child: ZhuoDaziApp(controller: AppController(api: host)),
      ),
    );
    await tester.pumpAndSettle();
  }

  Future<void> screenshot(WidgetTester tester, String name) async {
    final directory = Platform.environment['DESKPET_REVIEW_DIR'];
    if (directory == null) return;
    await tester.runAsync(() async {
      final boundary = tester.renderObject<RenderRepaintBoundary>(
        find.byKey(const Key('review')),
      );
      final picture = await boundary.toImage(pixelRatio: 2);
      final bytes = await picture.toByteData(format: ui.ImageByteFormat.png);
      await Directory(directory).create(recursive: true);
      await File('$directory/$name.png')
          .writeAsBytes(bytes!.buffer.asUint8List());
      picture.dispose();
    });
  }

  Future<void> showHall(WidgetTester tester) async {
    await tester.tap(
      find.byWidgetPredicate(
        (widget) => widget is NavigationDestination && widget.label == '大厅',
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('remote defaults load before trial metadata is created', (
    tester,
  ) async {
    final host = ProductHost();
    await pump(tester, host);
    expect(
      host.calls.indexOf('siteLinks'),
      lessThan(host.calls.indexOf('checkTrial')),
    );
    expect(host.calls.where((call) => call.startsWith('join:')), isEmpty);
  });

  testWidgets(
    'trial user explicitly joins hall and confirms exact preview before sending',
    (tester) async {
      final host = ProductHost();
      await pump(tester, host);
      await showHall(tester);
      expect(find.text('我愿意加入大厅'), findsOneWidget);
      expect(host.joined, isFalse);
      await tester.ensureVisible(find.text('我愿意加入大厅'));
      await tester.tap(find.text('我愿意加入大厅'));
      await tester.pumpAndSettle();
      await tester.pump(const Duration(seconds: 5));
      await tester.pumpAndSettle();
      expect(host.joined, isTrue);
      expect(find.text('现在在线 · 3'), findsOneWidget);
      await screenshot(tester, 'android-hall-online');
      final send = find.byTooltip('预览并发送给 小橘今天不加班');
      await tester.ensureVisible(send);
      await tester.tap(send);
      await tester.pumpAndSettle();
      expect(host.sends, 0);
      expect(find.text('确认发送'), findsOneWidget);
      await tester.tap(find.text('确认发送'));
      await tester.pumpAndSettle();
      expect(host.sends, 1);
      expect(host.sentTarget, 'trial:installation-1');
      expect(host.sentPet, 'preview-pet');
      expect(find.textContaining('秒后可以再发'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'hall shows empty state and retains a retry after network failure',
    (tester) async {
      final host = ProductHost()
        ..joined = true
        ..populated = false;
      await pump(tester, host);
      await showHall(tester);
      expect(find.text('还没有其他人在线'), findsOneWidget);
      await screenshot(tester, 'android-hall-empty');
      host.failHall = true;
      await tester.ensureVisible(find.byTooltip('刷新大厅'));
      await tester.tap(find.byTooltip('刷新大厅'));
      await tester.pumpAndSettle();
      expect(find.text('暂时没连上大厅'), findsOneWidget);
      expect(find.text('重新连接'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('expired trial cannot join or invoke hall profile APIs', (
    tester,
  ) async {
    final host = ProductHost()..trial = false;
    await pump(tester, host);
    await showHall(tester);
    expect(find.text('完整体验已结束'), findsOneWidget);
    expect(find.text('我愿意加入大厅'), findsNothing);
    expect(
      host.calls.where((call) => call == 'profile' || call == 'hall'),
      isEmpty,
    );
  });

  testWidgets(
    'pause and resume save explicit timer without changing automatic pet switch',
    (tester) async {
      final host = ProductHost();
      await pump(tester, host);
      await tester.tap(
        find.byWidgetPredicate(
          (widget) => widget is NavigationDestination && widget.label == '互动',
        ),
      );
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('暂停打扰 1 小时'),
        300,
        scrollable: find
            .descendant(
              of: find.byType(InteractionPage),
              matching: find.byType(Scrollable),
            )
          .first,
      );
      await Scrollable.ensureVisible(
        tester.element(find.text('暂停打扰 1 小时')),
        alignment: 0.5,
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('暂停打扰 1 小时'));
      await tester.pumpAndSettle();
      expect(
        host.settings[settingQuietUntil],
        greaterThan(DateTime.now().millisecondsSinceEpoch),
      );
      expect(find.text('恢复陪伴'), findsOneWidget);
      await tester.tap(find.text('恢复陪伴'));
      await tester.pumpAndSettle();
      expect(host.settings[settingQuietUntil], 0);
      expect(host.settings.containsKey(settingRandomPet), isFalse);
    },
  );

  testWidgets('online cached content still shows last sync error', (
    tester,
  ) async {
    final host = ProductHost();
    await pump(tester, host);
    await tester.tap(
      find.byWidgetPredicate(
        (widget) => widget is NavigationDestination && widget.label == '互动',
      ),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('暂时无法更新，已有内容仍可使用。'),
      250,
      scrollable: find.descendant(
        of: find.byType(InteractionPage),
        matching: find.byType(Scrollable),
      ),
    );
    expect(find.text('暂时无法更新，已有内容仍可使用。'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
