# Windows full local payment test package

The self-contained Windows x64 package starts the complete settings/pet application with a fresh, isolated real license identity. Activation and pet purchase open the official desktop website checkout; fulfilled purchases enter the existing market order, installer and signed use-grant flow.

Build uses `LocalPaymentTestBuild=true` and `ALIPAY_LOCAL_TEST`. This mode starts before normal initialization, loads only the public `payment-test.json`, routes known application API requests to the fixed loopback backend, and rejects other destinations. Its per-profile data resides under `%LOCALAPPDATA%\ZhuoDazi\AlipayLocalTest`; automatic updates and startup registration are disabled. Production builds retain their normal startup.

The local backend has a free fixture and a one-fen paid fixture. Activation also costs one fen. These are real merchant charges. Merchant keys, service databases, old test identities and checkout tokens remain outside the package. The launcher is intentionally for the configured Windows computer and starts the existing external backend/tunnel.

## Validation

- Alipay backend regression: 42 tests passed.
- Windows AlipayPreview, AppPreview, PetMarketClient, PetMarketView and PetMarketInstaller checks passed.
- New LocalPaymentTest checks cover API routing/rejection, current license identity, protected pending checkout persistence, restart recovery and fulfilled-order handoff.
- Live loopback trial registered without activation. Real local catalog exposes the free and paid fixtures, with one-fen activation and configured official provider.
- The previously paid real pet and newly claimed free pet both passed preview download, Ed25519 descriptor verification, package hash/size verification, actual Windows installation and signed use authorization.
- WPF harness rendered the complete integrated shop and activation window against the live local service. Both product thumbnails, purchase button and enabled activation checkout entry passed. The normal activation configuration/refresh/replacement regression also passed.
- Fixed concurrent thumbnail/detail preview downloads sharing a partial filename; each download now uses a unique partial file. Fresh-profile UI check confirms both thumbnails load.
- Final self-contained executable started, responded and displayed the full-payment-test title using a separate smoke profile. The delivered profile remains untouched and unactivated.
- Package audit found no merchant secret, saved device identity, service database or debug symbols. ZIP extraction and per-file SHA-256 manifest verification passed; extracted launcher verified local/public availability.

## Artifact

- ZIP: `D:\code_file\pro_python\pet\.alipay-local\packages\桌搭子-完整支付测试版-20261109-win-x64.zip`
- Size: 78,582,472 bytes (74.94 MiB).
- SHA-256: `226bcdad3e9bbc285acb338b1fa18bd49aa87b53f48115c0bfafd82aaf2c13d6`.
- Delivery profile: `fulltest-20261109-v1`.
- Entry: `启动完整测试版.cmd`; instructions: `测试说明.txt`.

The human still performs any new actual Alipay payment. This packaging run used the two earlier verified real payments for existing paid-state validation and did not submit another payment.
