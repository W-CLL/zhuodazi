#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != Darwin ]]; then
  printf '%s\n' 'This Swift heartbeat smoke test requires macOS.' >&2
  exit 1
fi

mac_root="$(cd "$(dirname "$0")/.." && pwd)"
heartbeat_test_target="$(uname -m)-apple-macosx13.0"
heartbeat_test_tmp="$(mktemp -d "${TMPDIR:-/tmp}/zhuodazi-heartbeat-test.XXXXXX")"
trap 'rm -rf -- "$heartbeat_test_tmp"' EXIT

xcrun swiftc -swift-version 5 -target "$heartbeat_test_target" -parse-as-library \
  "$mac_root/Sources/ZhuoDaziMac/DeviceHeartbeatLoop.swift" \
  "$mac_root/Tests/DeviceHeartbeatSmoke.swift" \
  -o "$heartbeat_test_tmp/device-heartbeat-smoke"

"$heartbeat_test_tmp/device-heartbeat-smoke"
