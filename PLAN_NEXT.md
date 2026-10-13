# Next round plan (owner: Denis, 13/10) — for the cloud Claude

Read TODO.md first (detailed history and numbers). This file sets the ORDER for the next releases. Keep the eval rules: test split scored once per release, thresholds tuned on train/dev only.

## v1.3.2 — usability first (users feel it immediately)
1. **Installer + autostart** (TODO #7): a simple per-user installer (no admin): copies to %LOCALAPPDATA%\KeySwitch, Start-menu shortcut, «Запускать при входе в Windows» toggle in the tray menu (HKCU Run key), clean uninstall. Ship as `KeySwitch-Setup.exe` (Inno Setup or WiX in CI on windows-latest) + keep the zips.
2. **Update check (opt-in, off by default)**: tray item «Проверить обновления» → GitHub Releases API latest tag → show changelog + download link. No background network calls unless enabled.
3. **Small build as the main download** (TODO #9): README/download buttons point to the small build first, with a one-click link to the .NET 8 Desktop Runtime; the installer detects a missing runtime and offers the self-contained build instead.
4. **Settings polish** (TODO #11): per-app exclusions picker (pick a running window), hotkey editor, per-app auto on/off, local stats (counts only, never text).

## v1.4.0 — quality
5. **Owner review loop**: rows 59–66 of eval/dev_label_review.tsv await Denis; then make the review flow easier — a tiny HTML report in CI artifacts with the new dev changes and «верно/неверно» columns to fill.
6. **RU auto precision ≥ 95 % on test** (TODO #3 next): opt-in personal word list (words only), Pause with previous-word context, proper-noun guard (capitalised mid-sentence words not in the dictionary are never auto-changed), GitHub-style text guard (code/terms near backticks, paths, CamelCase).
7. **Short words & names** (TODO #4): context model for 1–3 letter words and mixed-language phrases; add a regression set from real false switches.

## v1.5.0 — reach
8. **Real-Windows smoke tests** (TODO #2) in CI: UI Automation on Notepad + a WinForms/WPF test window (layout switch, Pause undo, Shift+Pause selection, password field skip).
9. **Ukrainian layout pack** (TODO #10) as an optional pack (most requested after RU/EN).
10. **Signing** (TODO #8): apply for free OSS signing (e.g. SignPath Foundation) since the repo is public; until then document SmartScreen «Подробнее → Выполнить в любом случае».

Rules: no text logging/telemetry ever; every feature behind a tray toggle; each release = one PR + green CI + one test-split scoring + README/README_RU tables updated; report results to Denis in Russian.
