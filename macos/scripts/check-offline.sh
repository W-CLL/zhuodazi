#!/usr/bin/env bash

set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
    printf '%s\n' 'The macOS source check requires macOS with Xcode Command Line Tools.' >&2
    exit 1
fi
if ! command -v xcrun >/dev/null 2>&1 || ! command -v swift >/dev/null 2>&1 || ! xcrun --find swift >/dev/null 2>&1; then
    printf '%s\n' 'Swift is unavailable. Install Xcode Command Line Tools first.' >&2
    exit 1
fi

mac_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

swift --version
swift test --package-path "$mac_root"
swift build --package-path "$mac_root" --configuration release

printf '%s\n' 'Offline macOS tests and release build completed successfully.'
