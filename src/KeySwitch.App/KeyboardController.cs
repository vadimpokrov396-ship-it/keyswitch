using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using KeySwitch.Core;

namespace KeySwitch.App;

internal sealed class KeyboardController : IDisposable
{
    private readonly Settings settings;
    private readonly FocusGuard guard;
    private readonly BoundaryEngine boundary = new(new DecisionEngine());
    private readonly ShiftTapDetector shiftTaps = new();
    private readonly Native.HookProc keyboardCallback, mouseCallback;
    private readonly Control dispatcher = new();
    private readonly bool selfTest;
    private IntPtr keyboardHook, mouseHook;
    private readonly StringBuilder word = new();
    private FocusTarget wordTarget;
    private FocusTarget contextTarget;
    private Task<string?>? wordProbe;
    private string? lastWord;
    private Task<string?>? lastWordProbe;
    private FocusTarget lastWordTarget;
    private Conversion? lastConversion;
    private readonly Dictionary<string, int> revertCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> learnedExceptions = new(StringComparer.OrdinalIgnoreCase);
    private long inputRevision;
    private bool suspended;
    private bool suppressToken;
    private readonly HashSet<Keys> swallowedUp = new();
    private long lastUipiWarning;
    internal IntPtr SelfTestFocus { get; set; }
    internal bool Suspended { get => suspended; set { suspended = value; Reset(); } }
    internal long InputRevision => Interlocked.Read(ref inputRevision);
    internal event Action? StateChanged;
    internal event Action<long>? SelectionRequested;
    internal event Action<long>? GrammarRequested;
    internal bool AutoEnabled { get => settings.AutoEnabled; set { settings.AutoEnabled = value; Reset(); settings.Save(); StateChanged?.Invoke(); } }
    internal bool TypoEnabled { get => settings.TypoEnabled; set { settings.TypoEnabled = value; Reset(); settings.Save(); StateChanged?.Invoke(); } }

    internal KeyboardController(Settings settings, bool selfTest = false)
    {
        this.settings = settings;
        this.selfTest = selfTest;
        guard = new FocusGuard(settings);
        _ = dispatcher.Handle; // Created on the Application.Run UI thread; BeginInvoke drains there.
        keyboardCallback = OnKeyboard;
        mouseCallback = OnMouse;
        keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyboardCallback, Native.GetModuleHandle(null), 0);
        int keyboardError = keyboardHook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseCallback, Native.GetModuleHandle(null), 0);
        int mouseError = mouseHook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        Diagnostics.Write($"hook keyboard=0x{keyboardHook.ToInt64():X} error={keyboardError} hresult=0x{(keyboardError == 0 ? 0 : 0x80070000u | (uint)keyboardError):X} mouse=0x{mouseHook.ToInt64():X} error={mouseError} hresult=0x{(mouseError == 0 ? 0 : 0x80070000u | (uint)mouseError):X}");
        if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
        {
            Dispose();
            throw new InvalidOperationException($"Не удалось установить системные хуки: keyboard={keyboardError}, mouse={mouseError}.");
        }
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam != (IntPtr)Native.WM_MOUSEMOVE &&
            (wParam == (IntPtr)Native.WM_LBUTTONDOWN || wParam == (IntPtr)Native.WM_RBUTTONDOWN || wParam == (IntPtr)Native.WM_MBUTTONDOWN))
        {
            var mouse = Marshal.PtrToStructure<Native.MouseHookData>(lParam);
            if ((mouse.flags & 1) == 0)
            {
                Interlocked.Increment(ref inputRevision);
                Post(Reset);
            }
        }
        return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    private IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
    {
        long started = Stopwatch.GetTimestamp();
        bool suppress = false;
        try
        {
            bool down = wParam == (IntPtr)Native.WM_KEYDOWN || wParam == (IntPtr)Native.WM_SYSKEYDOWN;
            bool up = wParam == (IntPtr)Native.WM_KEYUP || wParam == (IntPtr)Native.WM_SYSKEYUP;
            if (code >= 0 && (down || up) && !Suspended)
            {
                var key = Marshal.PtrToStructure<Native.KbdHookData>(lParam);
                bool injected = (key.flags & Native.LLKHF_INJECTED) != 0;
                bool testKey = injected && selfTest && key.dwExtraInfo == Native.SelfTestTag &&
                    FocusGuard.TryGetSnapshot(out var testTarget) && testTarget.Focus == SelfTestFocus;
                if (!injected || testKey)
                {
                    Keys vk = (Keys)key.vkCode;
                    bool shift = Down(Keys.ShiftKey), control = Down(Keys.ControlKey), alt = Down(Keys.Menu), win = Down(Keys.LWin) || Down(Keys.RWin);
                    bool shiftKey = vk is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey;
                    FocusGuard.TryGetSnapshot(out var snapshot);
                    IntPtr layout = snapshot.Thread == 0 ? IntPtr.Zero : Native.GetKeyboardLayout(snapshot.Thread);
                    if (up && swallowedUp.Remove(vk)) suppress = true;
                    if (shiftKey)
                    {
                        bool gesture = shiftTaps.Update((int)vk, down, Environment.TickCount64, control || alt || win);
                        if (gesture && settings.DoubleShiftEnabled)
                        {
                            long revision = InputRevision;
                            Post(() => _ = ManualConvertAsync(snapshot, revision));
                        }
                    }
                    else if (down)
                    {
                        shiftTaps.Reset();
                        long revision = Interlocked.Increment(ref inputRevision);
                        Keys hotkey = vk == Keys.Cancel && control ? Keys.Pause : vk;
                        if (!win && MatchHotkey(hotkey, shift, control, alt, out int action))
                        {
                            suppress = true;
                            swallowedUp.Add(vk);
                            Post(() => HandleHotkey(action, snapshot, revision));
                        }
                        else
                        {
                            bool deferredBoundary = (settings.AutoEnabled || settings.TypoEnabled) && (vk is Keys.Return or Keys.Tab) && !shift && !control && !alt && !win;
                            if (deferredBoundary) { suppress = true; swallowedUp.Add(vk); }
                            var captured = key;
                            Post(() => ProcessKey(captured, vk, shift, control, alt, win, snapshot, layout, revision, deferredBoundary));
                        }
                    }
                }
                else if (key.dwExtraInfo != Native.InjectionTag) Post(Reset);
                Diagnostics.Write($"event vk={key.vkCode} scan={key.scanCode} flags=0x{key.flags:X} injected={injected} accepted={(!injected || testKey)} suppress={suppress} callback_us={(Stopwatch.GetTimestamp() - started) * 1000000 / Stopwatch.Frequency}");
            }
        }
        catch (Exception error) { Diagnostics.Write($"hook_exception={error.GetType().Name} hresult=0x{error.HResult:X}"); Post(Reset); }
        return suppress ? (IntPtr)1 : Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
    }

    private static void ReplayDeferred(Keys key, FocusTarget snapshot)
    {
        if (FocusGuard.TryGetSnapshot(out var current) && Same(current, snapshot)) Native.Press((ushort)key);
    }

    private void Post(Action action)
    {
        try { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    private void HandleHotkey(int action, FocusTarget snapshot, long revision)
    {
        if (action == 2) { AutoEnabled = !AutoEnabled; return; }
        if (!guard.TryGetTarget(out var target) || !Same(target, snapshot)) return;
        if (action == 1) SelectionRequested?.Invoke(revision);
        else if (action == 3) { if (settings.GrammarEnabled) GrammarRequested?.Invoke(revision); }
        else _ = ManualConvertAsync(target, revision);
    }

    private void ProcessKey(Native.KbdHookData key, Keys vk, bool shift, bool control, bool alt, bool win,
        FocusTarget snapshot, IntPtr layout, long revision, bool deferredBoundary)
    {
        if (Suspended) { if (deferredBoundary) ReplayDeferred(vk, snapshot); return; }
        if (control || alt || win) { Reset(); if (deferredBoundary) ReplayDeferred(vk, snapshot); return; }
        if (!guard.TryGetTarget(out var target) || !Same(target, snapshot))
        {
            Reset(); if (deferredBoundary) ReplayDeferred(vk, snapshot); return;
        }
        if (!Same(contextTarget, target)) { Reset(); contextTarget = target; }
        if (word.Length > 0 && !Same(wordTarget, target)) Reset();
        if (vk == Keys.Back)
        {
            boundary.Reset();
            if (lastConversion is { } prior && Same(prior.Target, target)) _ = UndoAfterPhysicalBackspaceAsync(prior, revision);
            else if (word.Length > 0) word.Length--;
            else lastWord = null;
            return;
        }
        lastConversion = null;
        if (IsNavigation(vk)) { Reset(); return; }
        if (vk is Keys.Return or Keys.Tab && shift) { Reset(); return; }
        if (vk is Keys.Space or Keys.Return or Keys.Tab)
        {
            _ = CompleteAsync(target, vk == Keys.Space ? " " : vk == Keys.Tab ? "\t" : "\n", revision, deferredBoundary ? vk : null);
            return;
        }
        byte[] state = new byte[256];
        state[(int)vk] = 0x80;
        state[(int)Keys.ShiftKey] = shift ? (byte)0x80 : (byte)0;
        state[(int)Keys.ControlKey] = control ? (byte)0x80 : (byte)0;
        state[(int)Keys.Menu] = alt ? (byte)0x80 : (byte)0;
        state[(int)Keys.CapsLock] = (byte)(Native.GetKeyState((int)Keys.CapsLock) & 1);
        var chars = new StringBuilder(8);
        int count = Native.ToUnicodeEx(key.vkCode, key.scanCode, state, chars, chars.Capacity, 4, layout);
        if (count < 0 || count > 1) { Reset(); return; }
        if (count == 0) return;
        char ch = chars[0];
        if (word.Length == 0 && (ch == '/' || ch == '@' || ch == '#')) { suppressToken = true; return; }
        if (BoundaryEngine.IsTokenCharacter(ch))
        {
            if (word.Length == 0) { wordTarget = target; wordProbe = guard.GetSafeIdentityAsync(target); }
            if (word.Length >= 80) { Reset(); suppressToken = true; }
            else word.Append(ch);
        }
        else _ = CompleteAsync(target, ch.ToString(), revision, null);
    }

    private async Task CompleteAsync(FocusTarget target, string suffix, long revision, Keys? deferredKey)
    {
        // Let the target dispatch the physical word/space key messages before erasing.
        await Task.Delay(12);
        if (word.Length == 0 || suppressToken)
        {
            ResetWord(); boundary.Reset();
            if (deferredKey is Keys key && guard.IsSame(target)) Native.Press((ushort)key);
            return;
        }
        string original = word.ToString();
        Task<string?>? probe = wordProbe;
        ResetWord();
        lastWord = suffix == " " ? original : null;
        lastWordProbe = lastWord is null ? null : probe;
        lastWordTarget = target;
        if ((!AutoEnabled && !TypoEnabled) || !guard.IsSame(target)) { boundary.Reset(); if (deferredKey is Keys key && guard.IsSame(target)) Native.Press((ushort)key); return; }
        boundary.LayoutEnabled = AutoEnabled;
        boundary.TypoEnabled = TypoEnabled;
        boundary.TypoExceptions = settings.TypoExceptions;
        var result = boundary.Complete(original, suffix, settings.Exceptions.Concat(learnedExceptions));
        Diagnostics.Write($"token length={original.Length} decision={result.Reason} confidence={result.Confidence:F3} changed={result.Changed}");
        if (!result.Changed) { if (deferredKey is Keys key && guard.IsSame(target)) Native.Press((ushort)key); return; }
        string? identity = await (probe ?? guard.GetSafeIdentityAsync(target));
        string? currentIdentity = identity is null ? null : await guard.GetSafeIdentityAsync(target, 60);
        if (identity is null || currentIdentity != identity || !guard.IsSame(target) || InputRevision != revision)
        {
            boundary.Reset();
            if (deferredKey is Keys key && guard.IsSame(target)) Native.Press((ushort)key);
            return;
        }
        int erase = original.Length + result.PreviousCharacters + (deferredKey is null ? suffix.Length : 0);
        var sent = Native.Replace(erase, result.Replacement, deferredKey is null ? suffix : null, deferredKey is Keys virtualKey ? (ushort)virtualKey : null);
        if (!sent.Complete) { Reset(); WarnUipi(target, sent); if (deferredKey is Keys key && sent.Untouched && guard.IsSame(target)) Native.Press((ushort)key); return; }
        bool layoutChanged = IsRussian(result.Original) != IsRussian(result.Replacement);
        if (layoutChanged) SwitchLayout(target, result.Replacement);
        lastConversion = suffix is "\n" or "\t" ? null : new Conversion(result.Original, result.Replacement, suffix, target, identity, result.Reason == "typo-autocorrect", layoutChanged);
        lastWord = null; lastWordProbe = null;
        if (settings.SoundEnabled) SystemSounds.Asterisk.Play();
        StateChanged?.Invoke();
    }

    private async Task ManualConvertAsync(FocusTarget snapshot, long revision)
    {
        if (suppressToken || !guard.TryGetTarget(out var target) || !Same(target, snapshot)) return;
        if (lastConversion is { } prior && Same(prior.Target, target))
        {
            string? undoIdentity = await guard.GetSafeIdentityAsync(target);
            if (undoIdentity == prior.Identity && guard.IsSame(target) && InputRevision == revision) Undo(prior);
            return;
        }
        bool trailingSpace = word.Length == 0 && lastWord is not null && Same(lastWordTarget, target);
        Task<string?>? sourceProbe = trailingSpace ? lastWordProbe : wordProbe;
        string? sourceIdentity = sourceProbe is null ? null : await sourceProbe;
        string? identity = await guard.GetSafeIdentityAsync(target);
        if (identity is null || sourceIdentity != identity || !guard.IsSame(target) || InputRevision != revision) return;
        string original = trailingSpace ? lastWord! : word.ToString();
        if (original.Length is 0 or > 80) return;
        string converted = LayoutMap.Convert(original);
        Diagnostics.Write($"token length={original.Length} decision=manual confidence=1 changed={converted != original}");
        if (converted == original) return;
        await Task.Delay(12);
        if (!guard.IsSame(target) || InputRevision != revision) return;
        var sent = Native.Replace(original.Length + (trailingSpace ? 1 : 0), converted + (trailingSpace ? " " : ""));
        if (!sent.Complete) { Reset(); WarnUipi(target, sent); return; }
        SwitchLayout(target, converted);
        lastConversion = new Conversion(original, converted, trailingSpace ? " " : "", target, identity, false, true);
        lastWord = null; lastWordProbe = null;
        boundary.Reset();
        if (trailingSpace) ResetWord();
        else
        {
            word.Clear();
            word.Append(converted);
            wordTarget = target;
            wordProbe = Task.FromResult<string?>(identity);
        }
        StateChanged?.Invoke();
    }

    private async Task UndoAfterPhysicalBackspaceAsync(Conversion conversion, long revision)
    {
        // Physical Backspace already removed the suffix; restore the original and suffix.
        string? identity = await guard.GetSafeIdentityAsync(conversion.Target);
        if (identity != conversion.Identity || !guard.IsSame(conversion.Target) || InputRevision != revision) return;
        await Task.Delay(12);
        if (!guard.IsSame(conversion.Target) || InputRevision != revision) return;
        var sent = Native.Replace(conversion.Converted.Length + conversion.Suffix.Length - 1, conversion.Original + conversion.Suffix);
        if (!sent.Complete) { Reset(); WarnUipi(conversion.Target, sent); return; }
        CompleteUndo(conversion);
    }
    private void Undo(Conversion conversion)
    {
        var sent = Native.Replace(conversion.Converted.Length + conversion.Suffix.Length, conversion.Original + conversion.Suffix);
        if (!sent.Complete) { Reset(); WarnUipi(conversion.Target, sent); return; }
        CompleteUndo(conversion);
    }
    private void CompleteUndo(Conversion conversion)
    {
        if (conversion.LayoutChanged) SwitchLayout(conversion.Target, conversion.Original);
        lastConversion = null; lastWord = null; lastWordProbe = null; boundary.Reset();
        if (conversion.Suffix.Length == 0)
        {
            word.Clear();
            word.Append(conversion.Original);
            wordTarget = conversion.Target;
            wordProbe = Task.FromResult<string?>(conversion.Identity);
        }
        else ResetWord();
        if (conversion.Typo)
        {
            string typoOriginal = conversion.LayoutChanged ? LayoutMap.Convert(conversion.Original) : conversion.Original;
            int count = revertCounts.GetValueOrDefault(typoOriginal) + 1;
            revertCounts[typoOriginal] = count;
            if (count >= 2 && !settings.TypoExceptions.Contains(typoOriginal, StringComparer.OrdinalIgnoreCase))
            {
                settings.TypoExceptions.Add(typoOriginal);
                settings.Save();
            }
        }
        else
        {
            int count = revertCounts.GetValueOrDefault(conversion.Original) + 1;
            revertCounts[conversion.Original] = count;
            if (count >= 2) learnedExceptions.Add(conversion.Original);
        }
        StateChanged?.Invoke();
    }
    private void WarnUipi(FocusTarget target, Native.SendResult sent)
    {
        if (!sent.Untouched || Environment.TickCount64 - lastUipiWarning < 15000) return;
        if (Native.IsCurrentProcessElevated() || !Native.IsProcessElevated(target.Window)) return;
        lastUipiWarning = Environment.TickCount64;
        StateChanged?.Invoke();
        ElevationBlocked?.Invoke();
    }
    internal event Action? ElevationBlocked;
    internal void LogHookStatus() => Diagnostics.Write($"hook keyboard=0x{keyboardHook.ToInt64():X} mouse=0x{mouseHook.ToInt64():X}");
    private static void SwitchLayout(FocusTarget target, string text)
    {
        bool russian = text.Any(c => c is >= 'А' and <= 'я' or 'ё' or 'Ё');
        IntPtr layout = Native.LoadKeyboardLayout(russian ? "00000419" : "00000409", 0);
        bool posted = layout != IntPtr.Zero && Native.PostMessage(target.Window, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout);
        int error = posted ? 0 : Marshal.GetLastWin32Error();
        IntPtr activated = !posted && layout != IntPtr.Zero ? Native.ActivateKeyboardLayout(layout, 0) : IntPtr.Zero;
        Diagnostics.Write($"layout target=0x{target.Window.ToInt64():X} requested=0x{layout.ToInt64():X} posted={posted} error={error} fallback=0x{activated.ToInt64():X}");
    }
    private bool MatchHotkey(Keys key, bool shift, bool control, bool alt, out int action)
    {
        if (settings.ToggleHotkey.Matches(key, shift, control, alt)) { action = 2; return true; }
        if (settings.GrammarHotkey.Matches(key, shift, control, alt)) { action = 3; return true; }
        if (settings.SelectionHotkey.Matches(key, shift, control, alt)) { action = 1; return true; }
        if (settings.ConvertHotkey.Matches(key, shift, control, alt)) { action = 0; return true; }
        action = -1; return false;
    }
    private static bool Down(Keys key) => (Native.GetAsyncKeyState((int)key) & 0x8000) != 0;
    private static bool Same(FocusTarget a, FocusTarget b) => a.Window != IntPtr.Zero && a.Window == b.Window && a.Focus == b.Focus;
    private static bool IsRussian(string value) => value.Any(c => c is >= 'А' and <= 'я' or 'ё' or 'Ё');
    private static bool IsNavigation(Keys key) => key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.Delete or Keys.Escape or Keys.PageDown or Keys.PageUp or Keys.Insert;
    private void ResetWord() { word.Clear(); wordProbe = null; suppressToken = false; }
    private void Reset() { ResetWord(); boundary.Reset(); shiftTaps.Reset(); lastWord = null; lastWordProbe = null; lastConversion = null; contextTarget = default; }
    public void Dispose()
    {
        if (keyboardHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
        if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        dispatcher.Dispose();
    }
    private sealed record Conversion(string Original, string Converted, string Suffix, FocusTarget Target, string Identity, bool Typo, bool LayoutChanged);
}
