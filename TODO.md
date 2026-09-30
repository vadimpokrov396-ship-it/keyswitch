# TODO — what to improve (priority order)

1. ~~**CI on Windows (GitHub Actions)**~~ — done: `.github/workflows/ci.yml` runs Core tests on Linux, `tools/build.sh` on `windows-latest` (tests + both publishes + zips + PE/ZIP checks), `KeySwitch.exe --selftest --quiet` for both builds (ghbdtn → привет, becuase → because), uploads zips, and creates a GitHub Release on tag `v*` (tag must match `<Version>`). Next: extend the selftest toward item 2.
2. **Real-Windows smoke tests** — scripted checks for the hook path: `ghbdtn ` → `привет `, undo (Pause), selection (Shift+Pause), password fields skipped, elevated windows (UIPI), IME active. Notepad + a WinForms/WPF test window via UI Automation.
3. **Russian typo correction quality** — in progress. Measured on REAL misspellings (CI job `typo-eval`, ai-forever/spellcheck_benchmark, MIT; train splits = dev, test splits = report): the v1.2 engine had change precision 47.5% / recall 13.2% / false 0.153% on clean words. Root cause: `data/ru.txt` is a lemma list (has «получаться», not «получается»). Now: RU candidates from a word-FORM frequency list built from the Leipzig packs (`tools/build_ru_typo_list.py` → `data/ru-typo.txt` + `data/ru-typo-seen.txt`), single edits only, rivals from the OpenCorpora Bloom filter, cheap ь/ъ edits, 5+ letters, confidence ≥ 4.0 → test **88.8% / 22.8% / 0.063%**. Still OFF by default. Remaining: (a) bundle the two generated lists (built in CI; not yet committed), (b) slang/colloquial protection (ваще, рили), (c) context model for тся/ться and 2nd person, (d) decide the default.
4. **Short/ambiguous words and names** — context model for 1–3 letter words, proper names, mixed-language phrases (known false-switch sources).
5. **Focus change during deferred Enter/Tab** — the key can be lost; make the deferral safe.
6. **Hung UI Automation checks** — partly done: a probe hung for > 3 s no longer blocks other fields (max 6 pending probes, each holds a thread-pool thread). Remaining: real cancellation (a dedicated STA thread per probe that can be abandoned) and a Windows test with a deliberately hanging UIA provider.
7. **Installer & autostart** — MSI/winget or a simple installer, autostart toggle, update check (opt-in).
8. **Code signing** — the exe is unsigned (SmartScreen warnings). Document/sign.
9. **Size** — self-contained single file is ~84 MB; try ReadyToRun off / compression / trimming-safe subset, and ship the small (needs .NET 8 Desktop Runtime) build as the default download.
10. **More layouts** — Ukrainian/Belarusian/German etc. as optional packs.
11. **Settings UI polish** — exclusions per app, hotkey editor, per-app auto on/off, stats of corrections.

Baseline numbers (v1.1/v1.2): layout switch precision 99.4%, recall 96%, false switches 0.07% on held-out real text.
