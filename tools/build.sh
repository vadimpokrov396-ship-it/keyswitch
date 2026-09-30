#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if [[ -z "${DOTNET_BIN:-}" ]]; then
  if [[ -x "$HOME/.dotnet/dotnet" ]]; then DOTNET_BIN="$HOME/.dotnet/dotnet"; else DOTNET_BIN=dotnet; fi
fi
PYTHON="${PYTHON:-$(command -v python3 || command -v python)}"
mkdir -p artifacts dist
KEYSWITCH_BENCHMARK_REPORT="$PWD/artifacts/benchmark.txt" "$DOTNET_BIN" test tests/KeySwitch.Core.Tests -c Release --logger 'trx;LogFileName=core.trx' --results-directory "$PWD/artifacts"
"$DOTNET_BIN" publish src/KeySwitch.App -r win-x64 -c Release -p:PublishSingleFile=true --self-contained true -p:EnableWindowsTargeting=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist
"$DOTNET_BIN" publish src/KeySwitch.App -r win-x64 -c Release -p:PublishSingleFile=true --self-contained false -p:EnableWindowsTargeting=true -p:DebugType=None -p:DebugSymbols=false -o dist/small
sha256sum dist/KeySwitch.exe dist/small/KeySwitch.exe > artifacts/SHA256SUMS
"$PYTHON" tools/package.py
"$PYTHON" tools/verify_artifacts.py
