#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != Darwin ]]; then
  printf '%s\n' 'This AppKit smoke test requires a logged-in macOS desktop session.' >&2
  exit 1
fi

mac_root="$(cd "$(dirname "$0")/.." && pwd)"
display_test_target="$(uname -m)-apple-macosx13.0"
display_test_tmp="$(mktemp -d "${TMPDIR:-/tmp}/zhuodazi-display-test.XXXXXX")"
trap 'rm -rf -- "$display_test_tmp"' EXIT

xcrun swiftc -swift-version 5 -target "$display_test_target" \
  -emit-library -emit-module -module-name ZhuoDaziCore \
  "$mac_root/Sources/ZhuoDaziCore/PresentationLifetime.swift" \
  -emit-module-path "$display_test_tmp/ZhuoDaziCore.swiftmodule" \
  -Xlinker -install_name -Xlinker '@rpath/libZhuoDaziCore.dylib' \
  -o "$display_test_tmp/libZhuoDaziCore.dylib"

xcrun swiftc -swift-version 5 -target "$display_test_target" \
  -parse-as-library -I "$display_test_tmp" -L "$display_test_tmp" \
  -lZhuoDaziCore -Xlinker -rpath -Xlinker "$display_test_tmp" \
  "$mac_root/Sources/ZhuoDaziMac/PetCanvasView.swift" \
  "$mac_root/Sources/ZhuoDaziMac/PetInteractionPanel.swift" \
  "$mac_root/Tests/DisplayLifecycleSmoke.swift" \
  -o "$display_test_tmp/display-lifecycle-smoke"

"$display_test_tmp/display-lifecycle-smoke"
