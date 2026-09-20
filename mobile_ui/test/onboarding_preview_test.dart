import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:zhuodazi_ui/app_controller.dart';
import 'package:zhuodazi_ui/main.dart';

import 'onboarding_test.dart' show GuideHost;

void main() {
  final directory = Platform.environment['DESKPET_REVIEW_DIR'];
  testWidgets('render real themed onboarding cards for local visual review', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(412, 915);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    for (final entry in {
      'sans-serif':
          '${Platform.environment['WINDIR'] ?? 'C:/Windows'}/Fonts/msyh.ttc',
      'MaterialIcons': 'build/unit_test_assets/fonts/MaterialIcons-Regular.otf',
    }.entries) {
      final file = File(entry.value);
      if (file.existsSync()) {
        final font = FontLoader(entry.key)
          ..addFont(Future.value(ByteData.sublistView(file.readAsBytesSync())));
        await font.load();
      }
    }
    final host = GuideHost();
    final controller = AppController(api: host);
    await tester.pumpWidget(
      RepaintBoundary(
        key: const Key('onboarding-review'),
        child: ZhuoDaziApp(controller: controller),
      ),
    );
    await tester.pump(const Duration(milliseconds: 300));
    Future<void> capture(String name) async {
      await tester.runAsync(() async {
        final boundary = tester.renderObject<RenderRepaintBoundary>(
          find.byKey(const Key('onboarding-review')),
        );
        final image = await boundary.toImage(pixelRatio: 2);
        final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
        await Directory(directory!).create(recursive: true);
        await File('$directory/$name.png')
            .writeAsBytes(bytes!.buffer.asUint8List());
        image.dispose();
      });
    }

    await capture('android-guide-welcome');
    host.show({'guideStep': 2});
    await tester.pump();
    await capture('android-guide-interaction');
    host.show({'guideStep': 3, 'guideDemo': 'theater'});
    await tester.pump();
    await capture('android-guide-theater');
    host.show({'guideStep': 4, 'guideDemo': ''});
    await tester.pump();
    await capture('android-guide-quiet');
    host.show({
      'guideStep': 5,
      'guideDismissed': true,
      'premium': true,
      'interactionBusy': false,
      'theaterActive': false,
    });
    await tester.pump();
    await tester.tap(find.text('互动').last);
    await tester.pumpAndSettle();
    await capture('android-interaction-clean');
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox.shrink());
  }, skip: directory == null);
}
