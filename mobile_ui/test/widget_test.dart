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
    'trialSeconds': activated ? 0 : 86400,
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
    'libraries': <Map<String, Object>>[],
    'activeLibrary': '',
    'libraryGifCount': 0,
    'wordPacks': <String>['元气夸夸.json'],
    'theaterEnabled': false,
    'theaterInterval': 300,
    'theaterScripts': <Map<String, Object>>[],
    'reminders': <Map<String, Object>>[],
    'xianyuUrl': 'https://www.goofish.com/item?id=1',
    'wechatId': 'wcl_lcw627',
    'websiteUrl': 'https://desktoppet.online/',
    'announcement': '',
    'trialVisitsEnabled': true,
    'companionHallEnabled': true,
    'autoUpdatesEnabled': true,
    'autoCheckUpdates': true,
    'ignoredUpdateVersion': '',
    'canInstallPackages': false,
    'update': <String, Object>{
      'phase': 'idle',
      'message': '可以检查更新',
      'progress': 0,
    },
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
            case 'siteLinks':
            case 'checkUpdate':
              return snapshot();
            case 'petGif':
              return null;
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
    await tester.pumpAndSettle();
  }

  Future<void> tapHomeSend(WidgetTester tester) async {
    final sendHome = find.widgetWithText(QuickAction, '女友来访');
    await tester.scrollUntilVisible(
      sendHome,
      180,
      scrollable: find.descendant(
        of: find.byType(HomePage),
        matching: find.byType(Scrollable),
      ),
    );
    await tester.ensureVisible(sendHome);
    await tester.pump();
    await tester.tap(sendHome, warnIfMissed: false);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 320));
  }

  testWidgets('renders the Android companion shell', (tester) async {
    await pumpApp(tester, const Size(360, 800));
    expect(find.text('桌搭子'), findsOneWidget);
    expect(find.text('今天也一起'), findsOneWidget);
    expect(find.text('先打开悬浮窗，才会看到来访'), findsOneWidget);
    expect(find.text('去打开悬浮窗'), findsOneWidget);
    expect(find.text('首页'), findsWidgets);

    final destinations = tester
        .widgetList<NavigationDestination>(find.byType(NavigationDestination))
        .map((item) => item.label)
        .toList();
    expect(destinations, ['首页', '互动', '桌宠', '搭子', '我的']);

    await tester.tap(find.text('互动').last);
    await tester.pumpAndSettle();
    expect(find.text('随机来一个'), findsOneWidget);
    expect(find.text('18 条可用内容'), findsOneWidget);
    expect(find.text('线上趣味内容'), findsOneWidget);
    await tester.drag(
      find.descendant(
        of: find.byType(InteractionPage),
        matching: find.byType(ListView),
      ),
      const Offset(0, -1400),
    );
    await tester.pumpAndSettle();
    expect(find.text('小剧场'), findsWidgets);
    expect(find.text('自动随机上演'), findsOneWidget);
    expect(find.text('提醒'), findsWidgets);
    expect(find.text('新建提醒'), findsOneWidget);
    expect(tester.takeException(), isNull);

    await tester.tap(find.text('桌宠').last);
    await tester.pump();
    expect(find.text('激活后可导入'), findsOneWidget);
    await tester.scrollUntilVisible(
      find.text('图鉴目录'),
      180,
      scrollable: find.descendant(
        of: find.byType(PetPage),
        matching: find.byType(Scrollable),
      ),
    );
    await tester.pump();
    expect(find.text('图鉴目录'), findsOneWidget);
    expect(find.text('体验后可绑定'), findsOneWidget);
    expect(tester.takeException(), isNull);

    await tester.tap(find.text('搭子').last);
    await tester.pump();
    expect(find.text('体验期先自己叫人来串门'), findsOneWidget);
    expect(tester.takeException(), isNull);

    await tester.tap(find.text('我的').last);
    await tester.pumpAndSettle();
    expect(find.text('还没有激活码'), findsOneWidget);
    await tester.drag(
      find.descendant(
        of: find.byType(AccountPage),
        matching: find.byType(ListView),
      ),
      const Offset(0, -600),
    );
    await tester.pumpAndSettle();
    expect(find.text('去闲鱼看看'), findsOneWidget);
    expect(find.text('复制微信号'), findsOneWidget);
    await tester.drag(
      find.descendant(
        of: find.byType(AccountPage),
        matching: find.byType(ListView),
      ),
      const Offset(0, -800),
    );
    await tester.pumpAndSettle();
    expect(find.text('检查更新'), findsOneWidget);
    expect(find.text('自动检查更新'), findsOneWidget);
    expect(find.text('可以检查更新'), findsOneWidget);
    expect(
      calls.any((call) => call.method == 'checkUpdate'),
      isTrue,
    );
    await tester.tap(find.text('检查更新'));
    await tester.pumpAndSettle();
    expect(
      calls.where((call) => call.method == 'checkUpdate').length,
      greaterThanOrEqualTo(2),
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('premium pet page can bind a library folder', (tester) async {
    activated = true;
    await pumpApp(tester, const Size(360, 800));
    await tester.tap(find.text('桌宠').last);
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('绑定目录'),
      180,
      scrollable: find.descendant(
        of: find.byType(PetPage),
        matching: find.byType(Scrollable),
      ),
    );
    await tester.pump();
    expect(find.text('绑定目录'), findsOneWidget);
    await tester.tap(find.text('绑定目录'));
    await tester.pumpAndSettle();
    expect(calls.any((call) => call.method == 'importLibrary'), isTrue);
    expect(tester.takeException(), isNull);
  });

  testWidgets('trial home leads with visit, not send', (tester) async {
    overlayAllowed = true;
    running = true;
    await pumpApp(tester, const Size(360, 800));
    expect(find.textContaining('叫人来串门'), findsWidgets);
    expect(find.widgetWithText(FilledButton, '女友来访'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, '发给搭子'), findsNothing);
    await tester.scrollUntilVisible(
      find.widgetWithText(QuickAction, '女友来访'),
      180,
      scrollable: find.descendant(
        of: find.byType(HomePage),
        matching: find.byType(Scrollable),
      ),
    );
    expect(find.widgetWithText(QuickAction, '女友来访'), findsOneWidget);
    expect(find.widgetWithText(QuickAction, '好友来访'), findsOneWidget);
    expect(find.widgetWithText(QuickAction, '搭子来访'), findsOneWidget);
    expect(find.widgetWithText(QuickAction, '发给搭子'), findsNothing);
  });

  testWidgets('home send path asks for overlay first', (tester) async {
    await pumpApp(tester, const Size(360, 800));
    expect(find.text('先打开悬浮窗，才会看到来访'), findsOneWidget);
    await tapHomeSend(tester);
    expect(
      calls.any((call) => call.method == 'requestOverlayPermission'),
      isTrue,
    );
  });

  testWidgets('granting overlay starts the pet', (tester) async {
    await pumpApp(tester, const Size(360, 800));
    await tester.tap(find.widgetWithText(FilledButton, '去打开悬浮窗'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 320));
    expect(
      calls.any((call) => call.method == 'requestOverlayPermission'),
      isTrue,
    );
    expect(
      calls.any(
        (call) =>
            call.method == 'serviceAction' &&
            call.arguments is Map &&
            call.arguments['action'] == 'start',
      ),
      isTrue,
    );
  });

  testWidgets('home send path starts the pet then plays a trial visit', (
    tester,
  ) async {
    overlayAllowed = true;
    await pumpApp(tester, const Size(360, 800));
    await tapHomeSend(tester);
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
    expect(
      calls.any(
        (call) =>
            call.method == 'serviceAction' &&
            call.arguments is Map &&
            call.arguments['action'] == 'trialVisitGirlfriend',
      ),
      isTrue,
    );
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
      if (size.height >= 700) {
        await tester.scrollUntilVisible(
          find.widgetWithText(QuickAction, '女友来访'),
          180,
          scrollable: find.descendant(
            of: find.byType(HomePage),
            matching: find.byType(Scrollable),
          ),
        );
        expect(find.widgetWithText(QuickAction, '女友来访'), findsOneWidget);
        await tapHomeSend(tester);
        expect(tester.takeException(), isNull);
      }

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
