#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
DOTNET_BIN="${DOTNET_BIN:-$HOME/.dotnet/dotnet}"
mkdir -p artifacts dist
KEYSWITCH_BENCHMARK_REPORT="$PWD/artifacts/benchmark.txt" "$DOTNET_BIN" test tests/KeySwitch.Core.Tests -c Release --logger 'trx;LogFileName=core.trx' --results-directory "$PWD/artifacts"
"$DOTNET_BIN" publish src/KeySwitch.App -r win-x64 -c Release -p:PublishSingleFile=true --self-contained true -p:EnableWindowsTargeting=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist
"$DOTNET_BIN" publish src/KeySwitch.App -r win-x64 -c Release -p:PublishSingleFile=true --self-contained false -p:EnableWindowsTargeting=true -p:DebugType=None -p:DebugSymbols=false -o dist/small
sha256sum dist/KeySwitch.exe dist/small/KeySwitch.exe > artifacts/SHA256SUMS
python3 tools/package.py
python3 tools/verify_artifacts.py
