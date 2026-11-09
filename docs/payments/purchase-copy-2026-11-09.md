# Purchase copy revision

Client activation, shop and order history now use concise product copy. The activation card shows the product name and server price; the payment button is disabled while the price/provider status loads. Device codes, manual-contact instructions, payer-account guidance and preview labels do not appear in this purchase flow. Necessary errors and order states remain visible. Installed product display names follow current catalog titles while signed package metadata remains intact.

The website checkout presents product details, amount, order number, expiry, Alipay payment and payment/fulfillment states. Removed repeated footnotes, teaching copy and decorative slogans. Code-only activation delivery remains distinct from activation of the current device. Payment and fulfillment failure remain distinct, with a retry action.

Validation: 24 checkout/API tests passed; AlipayPreview, market view/client checks passed. Live preview/download/install/use-grant checks passed for both existing fixtures. WPF shop/activation visual checks and normal activation regressions passed. Final Release build completed with no warnings/errors; self-contained executable started with the expected title. Extracted ZIP file hashes and launcher availability check passed.

Artifact: `D:\code_file\pro_python\pet\.alipay-local\packages\桌搭子-购买页优化-20261109-win-x64.zip`.

SHA-256: `4c01f4619f5f9926a21b0c5bdb9c052d28690508d8b47e17f40abf99ec6d951f`.

The package reuses profile `fulltest-20261109-v1` to retain the user's tested activation/purchases. The runtime still connects to the configured isolated local backend and existing payment-domain tunnel. This revision changes customer copy and presentation; production deployment and production pricing are separate from this local artifact.
