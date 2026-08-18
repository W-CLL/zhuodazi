import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:zhuodazi_ui/main.dart';

void main() {
  final channel = const MethodChannel('com.zhuodazi.android/host');
  final calls = <MethodCall>[];
  var overlayAllowed = false;
  var running = false;
  var activated = false;

  Map<String, dynamic> snapshot() => <String, dynamic>{
    'overlayAllowed': overlayAllowed,
    'running': running,
    'activated': activated,
    'premium': activated,
    'trialSeconds': activated ? 0 : 300,
    'hidden': false,
    'clickThrough': false,
    'notificationAllowed': true,
    'interactionCachedCount': 18,
    'interactionOnlineCount': 0,
    'interactionCatalogVersion': 0,
    'interactionSyncError': '',
    'interactionLastSyncAt': 0,
    'pets': <Map<String, String>>[
      <String, String>{'id': '001', 'name': '月薪喵'},
    ],
    'activePet': '001',
    'wordPacks': <String>['元气夸夸.json'],
  };

  setUp(() {
    calls.clear();
    overlayAllowed = false;
    running = false;
    activated = false;
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
          calls.add(call);
          switch (call.method) {
            case 'snapshot':
            case 'checkTrial':
              return snapshot();
            case 'requestOverlayPermission':
              overlayAllowed = true;
              return true;
            case 'serviceAction':
              if (call.arguments is Map &&
                  call.arguments['action'] == 'start') {
                running = overlayAllowed;
              }
              return snapshot();
            case 'companionSend':
              return '小布';
            default:
              return snapshot();
          }
        });
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, null);
  });

  Future<void> pumpApp(WidgetTester tester, Size size) async {
    await tester.binding.setSurfaceSize(size);
    addTearDown(() => tester.binding.setSurfaceSize(null));
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(const ZhuoDaziApp());
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));
  }

  testWidgets('renders the Android companion shell', (tester) async {
    await pumpApp(tester, const Size(360, 800));
    expect(find.text('桌搭子'), findsOneWidget);
    expect(find.text('今天也一起'), findsOneWidget);
    expect(find.text('发给搭子'), findsWidgets);
    expect(find.text('首页'), findsWidgets);

    final destinations = tester
        .widgetList<NavigationDestination>(find.byType(NavigationDestination))
        .map((item) => item.label)
        .toList();
    expect(destinations, ['首页', '互动', '桌宠', '搭子', '我的']);

    await tester.tap(find.text('互动').last);
    await tester.pump();
    expect(find.text('随机来一个'), findsOneWidget);
    expect(find.text('18 条可用内容'), findsOneWidget);
    expect(find.text('线上趣味内容'), findsOneWidget);
    expect(tester.takeException(), isNull);

    await tester.tap(find.text('桌宠').last);
    await tester.pump();
    expect(find.text('激活后可导入'), findsOneWidget);
    expect(tester.takeException(), isNull);

    await tester.tap(find.text('搭子').last);
    await tester.pump();
    expect(find.text('体验期先看看怎么发'), findsOneWidget);
    expect(tester.takeException(), isNull);

    await tester.tap(find.text('我的').last);
    await tester.pump();
    expect(tester.takeException(), isNull);
  });

  testWidgets('home send path asks for overlay first', (tester) async {
    await pumpApp(tester, const Size(360, 800));
    await tester.ensureVisible(find.text('发给搭子').first);
    await tester.tap(find.text('发给搭子').first);
    await tester.pump();
    expect(
      calls.any((call) => call.method == 'requestOverlayPermission'),
      isTrue,
    );
  });

  testWidgets('home send path starts the pet then explains activation', (
    tester,
  ) async {
    overlayAllowed = true;
    await pumpApp(tester, const Size(360, 800));
    await tester.ensureVisible(find.text('发给搭子').first);
    await tester.tap(find.text('发给搭子').first);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 320));
    expect(
      calls.any(
        (call) =>
            call.method == 'serviceAction' &&
            call.arguments is Map &&
            call.arguments['action'] == 'start',
      ),
      isTrue,
    );
    expect(find.textContaining('正式激活'), findsWidgets);
  });

  for (final size in const [
    Size(320, 568),
    Size(360, 800),
    Size(412, 915),
    Size(768, 1024),
  ]) {
    testWidgets('tabs stay tappable at ${size.width.toInt()}x${size.height.toInt()}', (
      tester,
    ) async {
      await pumpApp(tester, size);
      expect(tester.takeException(), isNull);
      expect(find.text('发给搭子'), findsWidgets);

      await tester.ensureVisible(find.text('发给搭子').first);
      await tester.tap(find.text('发给搭子').first);
      await tester.pump();
      expect(tester.takeException(), isNull);

      for (final tab in ['互动', '桌宠', '搭子', '我的', '首页']) {
        await tester.tap(
          find.byWidgetPredicate(
            (widget) => widget is NavigationDestination && widget.label == tab,
          ),
        );
        await tester.pump();
        expect(tester.takeException(), isNull);
      }
    });
  }
}
