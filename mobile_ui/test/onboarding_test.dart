import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:zhuodazi_ui/app_controller.dart';
import 'package:zhuodazi_ui/host_api.dart';
import 'package:zhuodazi_ui/main.dart';

class GuideHost extends HostApi {
  final data = <String, dynamic>{
    'guideVersion': 1,
    'guideStep': 0,
    'guideDismissed': false,
    'guideUpgradeNotice': false,
    'guideDemo': '',
    'overlayAllowed': true,
    'running': true,
    'petVisible': true,
    'premium': false,
    'activated': true,
    'autoCheckUpdates': false,
  };
  final calls = <String>[];
  bool failAction = false;
  void Function(String, Object?)? listener;
  HostSnapshot get state => HostSnapshot(Map.of(data));
  @override
  void listen(void Function(String, Object?) onEvent) => listener = onEvent;
  @override
  Future<HostSnapshot> snapshot() async => state;
  @override
  Future<HostSnapshot> siteLinks() async => state;
  @override
  Future<HostSnapshot> checkTrial() async => state;
  @override
  Future<Uint8List?> petGif(String petId) async => null;
  @override
  Future<void> requestOverlayPermission() async {
    calls.add('permission');
  }

  @override
  Future<HostSnapshot> serviceAction(String action) async {
    calls.add('service:$action');
    if (failAction) throw const HostFailure('当前场景还没结束');
    data['running'] = true;
    return state;
  }

  @override
  Future<HostSnapshot> guideAction(String action) async {
    calls.add('guide:$action');
    if (action == 'interaction') data['guideDemo'] = 'interaction';
    if (action == 'dismissNotice') data['guideUpgradeNotice'] = false;
    return state;
  }

  void show(Map<String, dynamic> changes) {
    data.addAll(changes);
    listener?.call('snapshotChanged', Map.of(data));
  }
}

class GuideTestOwner extends StatefulWidget {
  const GuideTestOwner({
    required this.controller,
    required this.child,
    super.key,
  });
  final AppController controller;
  final Widget child;
  @override
  State<GuideTestOwner> createState() => _GuideTestOwnerState();
}

class _GuideTestOwnerState extends State<GuideTestOwner> {
  @override
  void dispose() {
    widget.controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => widget.child;
}

void main() {
  Future<AppController> showCard(
    WidgetTester tester,
    GuideHost host, {
    double scale = 1,
  }) async {
    final controller = AppController(api: host);
    await controller.initialize();
    await tester.pumpWidget(
      GuideTestOwner(
        controller: controller,
        child: MaterialApp(
          home: MediaQuery(
            data: MediaQueryData(textScaler: TextScaler.linear(scale)),
            child: Scaffold(
              body: SingleChildScrollView(
                child: AnimatedBuilder(
                  animation: controller,
                  builder: (context, _) =>
                      OnboardingCard(controller: controller),
                ),
              ),
            ),
          ),
        ),
      ),
    );
    return controller;
  }

  testWidgets('welcome waits until pet is actually visible and is optional', (
    tester,
  ) async {
    final host = GuideHost()..data['petVisible'] = false;
    await showCard(tester, host);
    expect(find.text('带我体验一下'), findsNothing);
    host.show({'petVisible': true});
    await tester.pump();
    expect(find.text('带我体验一下'), findsOneWidget);
    await tester.tap(find.text('先自己玩'));
    await tester.pump();
    expect(host.calls, contains('guide:dismiss'));
    expect(host.calls.where((call) => call.startsWith('service:')), isEmpty);
  });

  testWidgets(
    'local interaction never advances on dispatch and works without premium',
    (tester) async {
      final host = GuideHost()..data['guideStep'] = 2;
      await showCard(tester, host);
      await tester.tap(find.text('让它回应我'));
      await tester.pump();
      expect(host.calls, contains('guide:interaction'));
      expect(find.text('2 · 让它回应你'), findsOneWidget);
      expect(find.text('点一个回应，继续下一步。'), findsOneWidget);
      host.show({'guideStep': 3, 'guideDemo': ''});
      await tester.pump();
      expect(find.text('看一小段'), findsOneWidget);
    },
  );

  testWidgets(
    'theater has an end action and does not change auto-play settings',
    (tester) async {
      final host = GuideHost()
        ..data.addAll({
          'guideStep': 3,
          'guideDemo': 'theater',
          'theaterEnabled': false,
        });
      await showCard(tester, host);
      expect(find.textContaining('屏幕上方正在演短剧'), findsOneWidget);
      await tester.tap(find.text('结束示例'));
      await tester.pump();
      expect(host.calls, ['guide:endDemo']);
      expect(host.data['theaterEnabled'], false);
    },
  );

  testWidgets(
    'upgrade notice can close without starting or completing the tutorial',
    (tester) async {
      final host = GuideHost()
        ..data.addAll({'guideUpgradeNotice': true, 'guideDismissed': true});
      await showCard(tester, host);
      expect(find.text('带我体验一下'), findsNothing);
      await tester.tap(find.text('知道了，不再提示'));
      await tester.pump();
      expect(host.calls, ['guide:dismissNotice']);
      expect(host.data['guideStep'], 0);
      expect(find.text('开始一分钟体验'), findsOneWidget);
    },
  );

  testWidgets(
    'small screen and large text retain skip and later controls without overflow',
    (tester) async {
      tester.view.physicalSize = const Size(320, 720);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final host = GuideHost()..data['guideStep'] = 4;
      await showCard(tester, host, scale: 1.7);
      await tester.ensureVisible(find.text('稍后继续'));
      await tester.tap(find.text('稍后继续'));
      await tester.pump();
      expect(host.calls, ['guide:dismiss']);
      expect(tester.takeException(), isNull);
    },
  );

  test('a denied overlay grant does not dispatch or report success and resume continues the original action', () async {
    final host = GuideHost()
      ..data.addAll({
        'overlayAllowed': false,
        'running': false,
        'petVisible': false,
      });
    final controller = AppController(api: host);
    await controller.initialize();
    await expectLater(
      controller.service('theater'),
      throwsA(isA<HostFailure>()),
    );
    expect(host.calls, ['permission']);
    await controller.onAppResumed();
    expect(host.calls, ['permission']);
    host.data['overlayAllowed'] = true;
    await controller.onAppResumed();
    expect(host.calls, ['permission', 'service:theater']);
    controller.dispose();
  });

  test('native rejection stays an error and releases the busy state', () async {
    final host = GuideHost()..failAction = true;
    final controller = AppController(api: host);
    await controller.initialize();
    await expectLater(
      controller.service('interact'),
      throwsA(isA<HostFailure>()),
    );
    expect(controller.busy, false);
    expect(controller.operationStatus, '');
    controller.dispose();
  });
  test(
    'closing a permission-blocked tutorial cancels only its deferred start',
    () async {
      final host = GuideHost()
        ..data.addAll({
          'overlayAllowed': false,
          'running': false,
          'guideDismissed': true,
        });
      final controller = AppController(api: host);
      await controller.initialize();
      await expectLater(controller.guide('begin'), throwsA(isA<HostFailure>()));
      await controller.guide('dismissNotice');
      host.data['overlayAllowed'] = true;
      await controller.onAppResumed();
      expect(host.calls, ['permission', 'guide:dismissNotice']);
      controller.dispose();
    },
  );

  test('closing an unrelated guide notice keeps an explicitly pending normal action', () async {
    final host = GuideHost()
      ..data.addAll({'overlayAllowed': false, 'running': false});
    final controller = AppController(api: host);
    await controller.initialize();
    await expectLater(controller.service('next'), throwsA(isA<HostFailure>()));
    await controller.guide('dismissNotice');
    host.data['overlayAllowed'] = true;
    await controller.onAppResumed();
    expect(host.calls, ['permission', 'guide:dismissNotice', 'service:next']);
    controller.dispose();
  });

  test(
    'full exit withdraws actions waiting on a later permission grant',
    () async {
      final host = GuideHost()
        ..data.addAll({'overlayAllowed': false, 'running': false});
      final controller = AppController(api: host);
      await controller.initialize();
      await expectLater(
        controller.service('interact'),
        throwsA(isA<HostFailure>()),
      );
      await controller.service('stop');
      host.data['overlayAllowed'] = true;
      await controller.onAppResumed();
      expect(host.calls, ['permission', 'service:stop']);
      controller.dispose();
    },
  );
  testWidgets(
    'first visible pet brings fresh welcome home but an upgrade does not redirect',
    (tester) async {
      for (final upgraded in [false, true]) {
        final host = GuideHost()
          ..data.addAll({
            'petVisible': false,
            'guideDismissed': upgraded,
            'guideUpgradeNotice': upgraded,
          });
        await tester.pumpWidget(
          ZhuoDaziApp(controller: AppController(api: host)),
        );
        await tester.pump(const Duration(milliseconds: 50));
        await tester.tap(
          find.byWidgetPredicate(
            (widget) => widget is NavigationDestination && widget.label == '互动',
          ),
        );
        await tester.pump();
        expect(
          tester
              .widget<NavigationBar>(find.byType(NavigationBar))
              .selectedIndex,
          1,
        );
        host.show({'petVisible': true});
        await tester.pump();
        await tester.pump();
        expect(
          tester
              .widget<NavigationBar>(find.byType(NavigationBar))
              .selectedIndex,
          upgraded ? 1 : 0,
        );
        await tester.pumpWidget(const SizedBox.shrink());
      }
    },
  );
}
