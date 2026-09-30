# TODO — what to improve (priority order)

1. **CI on Windows (GitHub Actions)** — `windows-latest`: `dotnet test`, publish both builds (self-contained single-file + small framework-dependent), run `KeySwitch.exe --selftest` headless, upload zips as artifacts, attach to a GitHub Release on tag `v*`. The app was only built on Linux so far and never exercised by CI on real Windows.
2. **Real-Windows smoke tests** — scripted checks for the hook path: `ghbdtn ` → `привет `, undo (Pause), selection (Shift+Pause), password fields skipped, elevated windows (UIPI), IME active. Notepad + a WinForms/WPF test window via UI Automation.
3. **Russian typo correction quality** — RU precision 65.6% / recall 24.3% (EN 97.6% / 61.5%), so RU typos are OFF by default. Needs a full RU word-form dictionary (OpenCorpora/pymorphy forms) as candidates, keyboard-adjacency error model, frequency ranking; target ≥ 95% precision before enabling by default.
4. **Short/ambiguous words and names** — context model for 1–3 letter words, proper names, mixed-language phrases (known false-switch sources).
5. **Focus change during deferred Enter/Tab** — the key can be lost; make the deferral safe.
6. **Hung UI Automation checks** — when two UIA checks of different windows hang, correction in a third field is skipped until restart; add timeouts/cancellation per window.
7. **Installer & autostart** — MSI/winget or a simple installer, autostart toggle, update check (opt-in).
8. **Code signing** — the exe is unsigned (SmartScreen warnings). Document/sign.
9. **Size** — self-contained single file is ~84 MB; try ReadyToRun off / compression / trimming-safe subset, and ship the small (needs .NET 8 Desktop Runtime) build as the default download.
10. **More layouts** — Ukrainian/Belarusian/German etc. as optional packs.
11. **Settings UI polish** — exclusions per app, hotkey editor, per-app auto on/off, stats of corrections.

Baseline numbers (v1.1/v1.2): layout switch precision 99.4%, recall 96%, false switches 0.07% on held-out real text.
