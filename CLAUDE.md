# CLAUDE.md — KeySwitch

KeySwitch is a keyboard layout auto-switcher for Windows (RU/EN): tray app, C# .NET 8 WinForms.

## Layout
- `src/KeySwitch.Core` — platform-independent decision engine (layout detection: word lists + trigram LM + Bloom filters of word forms; typo correction). All tests target this project.
- `src/KeySwitch.App` — Windows-only: WH_KEYBOARD_LL hook, SendInput (unicode), UI Automation checks, tray UI, settings.
- `tests/KeySwitch.Core.Tests` — xUnit tests (683 passing at v1.2).
- `tools/` — `build.sh` (tests + both Windows x64 publishes + zips + checksums), eval tools, Python data/model pipeline (`KS_DATA` env = working dir for corpora; `KS_DOMAIN_GLOBS` = optional extra texts, `:`-separated globs).
- `data/`, `licenses/` — word lists and dataset licences (see `licenses/DATASETS.md`).

## Build / test
- .NET SDK 8.0.4xx (`global.json`). `dotnet test tests/KeySwitch.Core.Tests` runs on Linux/macOS.
- The App project targets `net8.0-windows`; build it with `-p:EnableWindowsTargeting=true` on Linux, but it can only be RUN on Windows.
- Never commit build outputs (`dist/`, `artifacts/`, `bin/`, `obj/`).

## Rules
- Code and comments in English; user-facing docs in Russian (`README_RU.md`).
- Precision first: a wrong automatic switch is worse than a missed one. Keep false-switch rate on clean text ≤ 0.1%.
- Privacy: no network in the auto-correction path, no telemetry, no logging of typed text to disk.
- See `TODO.md` for the current work list.
