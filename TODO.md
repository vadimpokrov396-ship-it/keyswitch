# TODO — what to improve (priority order)

1. ~~**CI on Windows (GitHub Actions)**~~ — done: `.github/workflows/ci.yml` runs Core tests on Linux, `tools/build.sh` on `windows-latest` (tests + both publishes + zips + PE/ZIP checks), `KeySwitch.exe --selftest --quiet` for both builds (ghbdtn → привет, becuase → because), uploads zips, and creates a GitHub Release on tag `v*` (tag must match `<Version>`). Next: extend the selftest toward item 2.
2. **Real-Windows smoke tests** — scripted checks for the hook path: `ghbdtn ` → `привет `, undo (Pause), selection (Shift+Pause), password fields skipped, elevated windows (UIPI), IME active. Notepad + a WinForms/WPF test window via UI Automation.
3. **Russian typo correction quality** — much improved; v1.3.0 ships RU auto OFF, Pause spelling fix ON. Real-typo eval in CI (`typo-eval`; ai-forever/spellcheck_benchmark, MIT; thresholds tuned on the train splits only; test scored once per release — do not re-run it before the next release): v1.2 engine 47.5% change precision / 13.2% recall / 0.153% false on clean → v1.3.0 auto **88.22% / 22.84% / 0.068%** (test); Pause **94.69% right / 35.01% of typos fixed** (test, threshold 2.5; dev 95.8% / 39.5%). Word-form list `data/ru-typo.txt` + `ru-typo-seen.txt` built by workflow_dispatch `build-ru-dictionary` from pinned Leipzig packs. Regression set `owner_typos` (plain `опечатка → правильно`). Colloquial words (`data/ru-colloquial.txt`) are protected. Two-edit corrections were measured and rejected (lower precision in both modes). **Next:** word-pair context (neighbouring words) for тся/ться, 2nd person and real-word errors (стаей/статей); tune on dev; enable RU auto by default once test precision ≥ 95%.
4. **Short/ambiguous words and names** — context model for 1–3 letter words, proper names, mixed-language phrases (known false-switch sources).
5. **Focus change during deferred Enter/Tab** — the key can be lost; make the deferral safe.
6. **Hung UI Automation checks** — partly done: a probe hung for > 3 s no longer blocks other fields (max 6 pending probes, each holds a thread-pool thread). Remaining: real cancellation (a dedicated STA thread per probe that can be abandoned) and a Windows test with a deliberately hanging UIA provider.
7. **Installer & autostart** — MSI/winget or a simple installer, autostart toggle, update check (opt-in).
8. **Code signing** — the exe is unsigned (SmartScreen warnings). Document/sign.
9. **Size** — self-contained single file is ~84 MB; try ReadyToRun off / compression / trimming-safe subset, and ship the small (needs .NET 8 Desktop Runtime) build as the default download.
10. **More layouts** — Ukrainian/Belarusian/German etc. as optional packs.
11. **Settings UI polish** — exclusions per app, hotkey editor, per-app auto on/off, stats of corrections.

Baseline numbers (v1.1/v1.2): layout switch precision 99.4%, recall 96%, false switches 0.07% on held-out real text.
