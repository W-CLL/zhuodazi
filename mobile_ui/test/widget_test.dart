import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:zhuodazi_ui/main.dart';

void main() {
  final channel = const MethodChannel('com.zhuodazi.android/host');

  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
          if (call.method == 'snapshot') {
            return <String, dynamic>{
              'overlayAllowed': false,
              'running': false,
              'activated': false,
              'premium': false,
              'trialSeconds': 300,
              'interactionCachedCount': 18,
              'interactionOnlineCount': 0,
              'interactionCatalogVersion': 0,
              'interactionSyncError': '',
              'interactionLastSyncAt': 0,
              'pets': <Map<String, String>>[],
              'wordPacks': <String>[],
            };
          }
          return null;
        });
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, null);
  });

  testWidgets('renders the Android companion shell', (tester) async {
    await tester.binding.setSurfaceSize(const Size(360, 800));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    await tester.pumpWidget(const ZhuoDaziApp());
    await tester.pumpAndSettle();
    expect(find.text('桌搭子'), findsOneWidget);
    expect(find.text('今天也一起'), findsOneWidget);
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
    expect(tester.takeException(), isNull);

    await tester.tap(find.text('桌宠').last);
    await tester.pumpAndSettle();
    expect(find.text('激活后可导入'), findsOneWidget);
    expect(tester.takeException(), isNull);

    for (final destination in ['搭子', '我的']) {
      await tester.tap(find.text(destination).last);
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
    }
  });
}
