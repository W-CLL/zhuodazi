import 'package:flutter_test/flutter_test.dart';
import 'package:zhuodazi_ui/host_api.dart';

void main() {
  test('legacy host snapshots keep built-in activation copy and QR', () {
    final snapshot = HostSnapshot.empty();
    expect(snapshot.activationTitle, '还没有激活码');
    expect(snapshot.activationDescription, contains('加作者微信'));
    expect(snapshot.activationQrBytes, isNull);
  });

  test(
    'host activation configuration includes full device code and QR bytes',
    () {
      final snapshot = HostSnapshot({
        'installationId': 'device-code-12345678901234567890',
        'activationTitle': '新的激活标题',
        'activationDescription': '最新说明\n第二行',
        'activationQrCodeDataUrl': 'data:image/png;base64,AQID',
      });
      expect(snapshot.installationId, 'device-code-12345678901234567890');
      expect(snapshot.activationTitle, '新的激活标题');
      expect(snapshot.activationDescription, '最新说明\n第二行');
      expect(snapshot.activationQrBytes, [1, 2, 3]);
    },
  );

  test('invalid or cleared remote QR falls back to the bundled image', () {
    for (final image in [
      '',
      'data:image/png;base64,?',
      'data:image/svg+xml;base64,AQID',
      'data:image/png;base64,${'A' * 350000}',
    ]) {
      expect(
        HostSnapshot({'activationQrCodeDataUrl': image}).activationQrBytes,
        isNull,
      );
    }
  });
}
