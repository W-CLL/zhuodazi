import 'dart:async';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:zhuodazi_ui/app_controller.dart';
import 'package:zhuodazi_ui/host_api.dart';
import 'package:zhuodazi_ui/main.dart';

class RuntimeHost extends HostApi {
  bool interacting = false;
  bool theater = false;
  bool activated = true;
  int trialSeconds = 3600;
  String petId = 'A';
  final imageRequests = <String>[];
  Completer<Uint8List?>? delayedImage;
  Completer<HostSnapshot>? delayedPoll;
  HostSnapshot get state => HostSnapshot({
    'activated': activated,
    'trialSeconds': trialSeconds,
    'activePet': petId,
    'premium': true,
    'running': true,
    'overlayAllowed': true,
    'autoCheckUpdates': false,
    'guideVersion': 1,
    'guideDismissed': true,
    'guideStep': 5,
    'interactionBusy': interacting,
    'theaterActive': theater,
    'interactionCatalogVersion': 19,
    'interactionLastSyncAt': 1700000000000,
    'interactionCachedCount': 99,
  });
  @override
  void listen(void Function(String, Object?) listener) {}
  @override
  Future<HostSnapshot> snapshot() => delayedPoll?.future ?? Future.value(state);
  @override
  Future<HostSnapshot> siteLinks() async => state;
  @override
  Future<Uint8List?> petGif(String id) async {
    imageRequests.add(id);
    if (id == 'B' && delayedImage != null) return delayedImage!.future;
    return Uint8List.fromList(id.codeUnits);
  }

  @override
  Future<HostSnapshot> checkTrial() async => state;
  @override
  Future<HostSnapshot> serviceAction(String action) async {
    if (action == 'interact') interacting = true;
    if (action == 'theater') theater = true;
    if (action == 'endScene') {
      interacting = false;
      theater = false;
    }
    return state;
  }
}

void main() {
  testWidgets(
    'closing a normal native interaction refreshes buttons without onboarding or app resume',
    (tester) async {
      final host = RuntimeHost();
      final controller = AppController(api: host);
      await controller.initialize();
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: AnimatedBuilder(
              animation: controller,
              builder: (_, _) => InteractionPage(controller: controller),
            ),
          ),
        ),
      );
      final start = find.widgetWithText(FilledButton, '开始随机互动');
      await tester.ensureVisible(start);
      await tester.tap(start);
      await tester.pump();
      expect(controller.snapshot.interactionBusy, isTrue);
      expect(tester.widget<FilledButton>(start).onPressed, isNull);
      // The native overlay choice/close callback has finished; no explicit Flutter refresh or event.
      host.interacting = false;
      await tester.pump(const Duration(milliseconds: 850));
      await tester.pump();
      expect(controller.snapshot.interactionBusy, isFalse);
      expect(tester.widget<FilledButton>(start).onPressed, isNotNull);
      expect(find.textContaining('当前场景'), findsNothing);
      expect(find.textContaining('目录 v'), findsNothing);
      expect(find.textContaining('上次同步'), findsNothing);
      await tester.ensureVisible(start);
      await tester.tap(start);
      await tester.pump();
      expect(host.interacting, isTrue);
      await tester.pumpWidget(const SizedBox.shrink());
      controller.dispose();
    },
  );

  testWidgets('late runtime poll cannot overwrite a newer stop result', (
    tester,
  ) async {
    final host = RuntimeHost()..interacting = true;
    final controller = AppController(api: host);
    await controller.initialize();
    final stale = host.state;
    host.delayedPoll = Completer<HostSnapshot>();
    await tester.pump(const Duration(milliseconds: 800));
    await controller.service('endScene');
    expect(controller.snapshot.interactionBusy, isFalse);
    host.delayedPoll!.complete(stale);
    await tester.pump();
    expect(controller.snapshot.interactionBusy, isFalse);
    controller.dispose();
  });

  testWidgets('completed normal theater is read without an active guide', (
    tester,
  ) async {
    final host = RuntimeHost()..theater = true;
    final controller = AppController(api: host);
    await controller.initialize();
    host.theater = false;
    await tester.pump(const Duration(milliseconds: 850));
    expect(controller.snapshot.theaterActive, isFalse);
    controller.dispose();
  });

  testWidgets(
    'runtime refresh updates automatic pet preview and expired trial without repeated image loads',
    (tester) async {
      final host = RuntimeHost()..activated = false;
      final controller = AppController(api: host);
      await controller.initialize();
      final initialRequests = host.imageRequests.length;
      await tester.pump(const Duration(milliseconds: 850));
      expect(host.imageRequests.length, initialRequests);
      host.petId = 'B';
      host.trialSeconds = 0;
      await tester.pump(const Duration(milliseconds: 850));
      expect(controller.snapshot.activePet, 'B');
      expect(controller.petGif, Uint8List.fromList('B'.codeUnits));
      expect(controller.liveTrialSeconds, 0);
      controller.dispose();
    },
  );

  testWidgets('late preview image cannot replace a newer selected pet', (
    tester,
  ) async {
    final host = RuntimeHost();
    final controller = AppController(api: host);
    await controller.initialize();
    host.delayedImage = Completer<Uint8List?>();
    host.petId = 'B';
    await tester.pump(const Duration(milliseconds: 850));
    host.petId = 'C';
    await controller.refresh();
    host.delayedImage!.complete(Uint8List.fromList('B'.codeUnits));
    await tester.pump();
    expect(controller.snapshot.activePet, 'C');
    expect(controller.petGif, Uint8List.fromList('C'.codeUnits));
    controller.dispose();
  });
}
