using System.Runtime.InteropServices;
using KeySwitch.Core;

namespace KeySwitch.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            Diagnostics.Enabled = true;
            Diagnostics.Startup();
            // --quiet: no result dialog, exit code 0 = pass, 1 = fail (for CI / scripted checks).
            Application.Run(new SelfTestForm(quiet: args.Contains("--quiet", StringComparer.OrdinalIgnoreCase)));
            return;
        }
        using var mutex = new Mutex(true, "Local\\KeySwitch.SingleInstance", out bool first);
        if (!first) return;
        try { Application.Run(new TrayApplication()); }
        catch (Exception error) { MessageBox.Show("KeySwitch не запущен: " + error.Message, "KeySwitch", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}

internal sealed class TrayApplication : ApplicationContext
{
    private readonly Settings settings;
    private readonly KeyboardController keyboard;
    private readonly FocusGuard guard;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer;
    private readonly ToolStripMenuItem enabledItem;
    private readonly ToolStripMenuItem typoItem;
    private readonly ToolStripMenuItem grammarItem;
    private readonly ToolStripMenuItem grammarHint = new() { Enabled = false };
    private readonly GrammarClient grammarClient = new();
    private readonly ToolStripMenuItem convertHint = new() { Enabled = false };
    private readonly ToolStripMenuItem selectionHint = new() { Enabled = false };
    private readonly ToolStripMenuItem toggleHint = new() { Enabled = false };
    private Icon? currentIcon;
    private string? previousIconState;
    private bool selectionBusy;
    private bool grammarBusy;

    internal TrayApplication()
    {
        settings = Settings.Load();
        TypoCorrector.RussianEnabled = settings.RussianTypoEnabled;
        guard = new FocusGuard(settings);
        Diagnostics.Startup();
        keyboard = new KeyboardController(settings);
        keyboard.ElevationBlocked += () => tray?.ShowBalloonTip(5000, "KeySwitch", "Запустите KeySwitch от администратора для этого окна.", ToolTipIcon.Warning);
        keyboard.StateChanged += UpdateTray;
        keyboard.SelectionRequested += revision => _ = ConvertSelectionAsync(revision);
        keyboard.GrammarRequested += revision => _ = CheckGrammarAsync(revision);
        var menu = new ContextMenuStrip();
        enabledItem = new ToolStripMenuItem("Автозамена включена") { CheckOnClick = true };
        enabledItem.Click += (_, _) => keyboard.AutoEnabled = enabledItem.Checked;
        menu.Items.Add(enabledItem);
        typoItem = new ToolStripMenuItem("Исправлять опечатки") { CheckOnClick = true };
        typoItem.Click += (_, _) => keyboard.TypoEnabled = typoItem.Checked;
        menu.Items.Add(typoItem);
        grammarItem = new ToolStripMenuItem("Проверка грамматики") { CheckOnClick = true };
        grammarItem.Click += (_, _) => { settings.GrammarEnabled = grammarItem.Checked; settings.Save(); UpdateTray(); };
        menu.Items.Add(grammarItem);
        menu.Items.Add(convertHint);
        menu.Items.Add(selectionHint);
        menu.Items.Add(toggleHint);
        menu.Items.Add(grammarHint);
        menu.Items.Add("Исключения…", null, (_, _) => OpenSettings(0));
        menu.Items.Add("Исключённые приложения…", null, (_, _) => OpenSettings(1));
        menu.Items.Add("Горячие клавиши…", null, (_, _) => OpenSettings(2));
        menu.Items.Add("Настройки…", null, (_, _) => OpenSettings(3));
        var diagnosticsItem = new ToolStripMenuItem("Диагностика (лог)") { CheckOnClick = true, Checked = Diagnostics.Enabled };
        diagnosticsItem.Click += (_, _) => { Diagnostics.Enabled = diagnosticsItem.Checked; if (Diagnostics.Enabled) { Diagnostics.Startup(); keyboard.LogHookStatus(); } };
        menu.Items.Add(diagnosticsItem);
        menu.Items.Add("О программе", null, (_, _) => MessageBox.Show("KeySwitch — исправление раскладки и опечаток RU ⇄ EN.\nГрамматика проверяется только по запросу: выделенный текст отправляется LanguageTool после вашего согласия.", "О KeySwitch"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());
        tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "KeySwitch" };
        tray.DoubleClick += (_, _) => keyboard.AutoEnabled = !keyboard.AutoEnabled;
        timer = new System.Windows.Forms.Timer { Interval = 800 };
        timer.Tick += (_, _) => UpdateTray();
        timer.Start();
        UpdateTray();
    }

    private void OpenSettings(int page)
    {
        keyboard.Suspended = true;
        try
        {
            using var form = new SettingsForm(settings, page);
            if (form.ShowDialog() == DialogResult.OK) { settings.Save(); UpdateTray(); }
        }
        finally { keyboard.Suspended = false; }
    }

    private void UpdateTray()
    {
        convertHint.Text = $"{settings.ConvertHotkey}{(settings.DoubleShiftEnabled ? " / двойной Shift" : "")} — раскладка или опечатка слова / отмена";
        selectionHint.Text = $"{settings.SelectionHotkey} — выделение";
        toggleHint.Text = $"{settings.ToggleHotkey} — автозамена";
        grammarHint.Text = $"{settings.GrammarHotkey} — грамматика выделения";
        enabledItem.Checked = settings.AutoEnabled;
        typoItem.Checked = settings.TypoEnabled;
        grammarItem.Checked = settings.GrammarEnabled;
        enabledItem.Text = settings.AutoEnabled ? "Автозамена включена" : "Автозамена выключена";
        string language = "—";
        IntPtr foreground = Native.GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            uint thread = Native.GetWindowThreadProcessId(foreground, out _);
            ushort lang = unchecked((ushort)Native.GetKeyboardLayout(thread).ToInt64());
            language = lang == 0x0419 ? "RU" : lang == 0x0409 ? "EN" : "—";
        }
        string state = $"{settings.AutoEnabled}:{language}";
        if (state == previousIconState) return;
        previousIconState = state;
        tray.Text = $"KeySwitch — {(settings.AutoEnabled ? "включён" : "выключен")}, {language}";
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(settings.AutoEnabled ? Color.FromArgb(26, 145, 83) : Color.FromArgb(110, 110, 110));
            using var font = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.White);
            var size = graphics.MeasureString(language, font);
            graphics.DrawString(language, font, brush, (32 - size.Width) / 2, (32 - size.Height) / 2);
        }
        IntPtr handle = bitmap.GetHicon();
        try
        {
            var icon = (Icon)Icon.FromHandle(handle).Clone();
            tray.Icon = icon;
            currentIcon?.Dispose();
            currentIcon = icon;
        }
        finally { Native.DestroyIcon(handle); }
    }

    private async Task ConvertSelectionAsync(long revision)
    {
        if (selectionBusy) return;
        selectionBusy = true;
        try { await ConvertSelectionCoreAsync(revision); }
        finally { selectionBusy = false; }
    }

    private async Task CheckGrammarAsync(long revision)
    {
        if (grammarBusy || !settings.GrammarEnabled) return;
        grammarBusy = true;
        try
        {
            if (!settings.GrammarConsent)
            {
                keyboard.Suspended = true;
                try
                {
                    var consent = MessageBox.Show("Для проверки грамматики только выделенный текст уйдёт на сервер LanguageTool. Запрос выполняется лишь по горячей клавише. Согласиться?\n\nПосле согласия выделите текст и нажмите сочетание ещё раз.",
                        "KeySwitch — согласие на отправку текста", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (consent == DialogResult.Yes) { settings.GrammarConsent = true; settings.Save(); }
                }
                finally { keyboard.Suspended = false; }
                return;
            }
            if (!guard.TryGetTarget(out var target)) return;
            string? identity = await guard.GetSafeIdentityAsync(target);
            if (identity is null) return;
            string? selected = await CaptureSelectedAsync(target, identity, revision);
            if (selected is null) return;
            GrammarCheck result;
            try { result = await grammarClient.CheckAsync(selected, settings.GrammarLanguage); }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
            {
                MessageBox.Show(error is InvalidOperationException ? error.Message : "LanguageTool сейчас недоступен. Проверьте подключение и повторите позже.", "KeySwitch — грамматика");
                return;
            }
            if (keyboard.InputRevision != revision || !guard.IsSame(target) || await guard.GetSafeIdentityAsync(target) != identity) return;
            if (result.Corrected == selected)
            {
                MessageBox.Show("LanguageTool не предложил изменений.", "KeySwitch — грамматика");
                return;
            }
            keyboard.Suspended = true;
            string? replacement;
            try
            {
                using var preview = new GrammarPreview(result.Corrected, result.Suggestions);
                replacement = preview.ShowDialog() == DialogResult.OK ? preview.Corrected : null;
            }
            finally { keyboard.Suspended = false; }
            if (replacement is null || replacement == selected) return;
            Native.SetForegroundWindow(target.Window);
            await Task.Delay(100);
            long applyRevision = keyboard.InputRevision;
            if (!guard.IsSame(target) || await guard.GetSafeIdentityAsync(target) != identity) return;
            string? stillSelected = await CaptureSelectedAsync(target, identity, applyRevision);
            if (stillSelected != selected) return;
            await PasteSelectedAsync(target, identity, applyRevision, replacement);
        }
        finally { grammarBusy = false; }
    }

    private async Task<string?> CaptureSelectedAsync(FocusTarget target, string identity, long revision)
    {
        for (int i = 0; i < 30 && (Control.ModifierKeys & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None; i++) await Task.Delay(20);
        if ((Control.ModifierKeys & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None || keyboard.InputRevision != revision ||
            !guard.IsSame(target) || await guard.GetSafeIdentityAsync(target) != identity) return null;
        if (Native.OleGetClipboard(out IntPtr originalClipboard) != 0) return null;
        uint ownSequence = 0;
        try
        {
            uint before = Native.GetClipboardSequenceNumber();
            if (!Native.Send(Native.Key((ushort)Keys.ControlKey), Native.Key((ushort)Keys.C), Native.Key((ushort)Keys.C, true), Native.Key((ushort)Keys.ControlKey, true))) return null;
            await Task.Delay(140);
            uint copied = Native.GetClipboardSequenceNumber();
            if (copied == before || keyboard.InputRevision != revision || !guard.IsSame(target) || await guard.GetSafeIdentityAsync(target) != identity) return null;
            ownSequence = copied;
            if (!Clipboard.ContainsText()) return null;
            string text = Clipboard.GetText();
            return text.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(text) <= 20_000 ? text : null;
        }
        catch { return null; }
        finally
        {
            try { if (ownSequence != 0 && Native.GetClipboardSequenceNumber() == ownSequence) Native.OleSetClipboard(originalClipboard); } catch { }
            if (originalClipboard != IntPtr.Zero) Marshal.Release(originalClipboard);
        }
    }

    private async Task PasteSelectedAsync(FocusTarget target, string identity, long revision, string replacement)
    {
        if (Native.OleGetClipboard(out IntPtr originalClipboard) != 0) return;
        uint ownSequence = 0;
        try
        {
            Clipboard.SetText(replacement);
            ownSequence = Native.GetClipboardSequenceNumber();
            if (keyboard.InputRevision != revision || !guard.IsSame(target) || await guard.GetSafeIdentityAsync(target) != identity ||
                Native.GetClipboardSequenceNumber() != ownSequence) return;
            Native.Send(Native.Key((ushort)Keys.ControlKey), Native.Key((ushort)Keys.V), Native.Key((ushort)Keys.V, true), Native.Key((ushort)Keys.ControlKey, true));
            await Task.Delay(220);
        }
        catch { }
        finally
        {
            try { if (ownSequence != 0 && Native.GetClipboardSequenceNumber() == ownSequence) Native.OleSetClipboard(originalClipboard); } catch { }
            if (originalClipboard != IntPtr.Zero) Marshal.Release(originalClipboard);
        }
    }

    private async Task ConvertSelectionCoreAsync(long revision)
    {
        if (!guard.TryGetTarget(out var target)) return;
        string? identity = await guard.GetSafeIdentityAsync(target);
        if (identity is null) return;
        for (int i = 0; i < 30 && (Control.ModifierKeys & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None; i++) await Task.Delay(20);
        if ((Control.ModifierKeys & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None || keyboard.InputRevision != revision ||
            !guard.IsSame(target) || await guard.GetSafeIdentityAsync(target) != identity) return;
        if (Native.OleGetClipboard(out IntPtr originalClipboard) != 0) return;
        uint ownSequence = 0;
        try
        {
            uint before = Native.GetClipboardSequenceNumber();
            if (!Native.Send(Native.Key((ushort)Keys.ControlKey), Native.Key((ushort)Keys.C), Native.Key((ushort)Keys.C, true), Native.Key((ushort)Keys.ControlKey, true))) return;
            await Task.Delay(140);
            uint copiedSequence = Native.GetClipboardSequenceNumber();
            if (copiedSequence != before) ownSequence = copiedSequence;
            if (copiedSequence == before || keyboard.InputRevision != revision || !guard.IsSame(target) || await guard.GetSafeIdentityAsync(target) != identity) return;
            if (!Clipboard.ContainsText()) return;
            string selected = Clipboard.GetText();
            if (selected.Length is 0 or > 100000) return;
            string converted = LayoutMap.Convert(selected);
            if (selected == converted) return;
            Clipboard.SetText(converted);
            ownSequence = Native.GetClipboardSequenceNumber();
            if ((Control.ModifierKeys & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None || keyboard.InputRevision != revision ||
                !guard.IsSame(target) || await guard.GetSafeIdentityAsync(target) != identity || Native.GetClipboardSequenceNumber() != ownSequence) return;
            Native.Send(Native.Key((ushort)Keys.ControlKey), Native.Key((ushort)Keys.V), Native.Key((ushort)Keys.V, true), Native.Key((ushort)Keys.ControlKey, true));
            await Task.Delay(220);
        }
        catch { }
        finally
        {
            try
            {
                // Never overwrite a clipboard update made by another application in the meantime.
                if (ownSequence != 0 && Native.GetClipboardSequenceNumber() == ownSequence) Native.OleSetClipboard(originalClipboard);
            }
            catch { }
            if (originalClipboard != IntPtr.Zero) Marshal.Release(originalClipboard);
        }
    }

    protected override void ExitThreadCore()
    {
        timer.Stop(); timer.Dispose();
        keyboard.Dispose();
        grammarClient.Dispose();
        tray.Visible = false; tray.Dispose();
        currentIcon?.Dispose();
        settings.Save();
        base.ExitThreadCore();
    }
}

internal sealed class SelfTestForm : Form
{
    private readonly TextBox editor = new() { Dock = DockStyle.Fill };
    private readonly bool quiet;
    private KeyboardController? keyboard;
    internal SelfTestForm(bool quiet)
    {
        this.quiet = quiet;
        Environment.ExitCode = 1;
        Text = "KeySwitch selftest";
        Size = new Size(240, 80);
        ShowInTaskbar = false;
        Opacity = 0;
        Controls.Add(editor);
        Shown += async (_, _) => await RunAsync();
    }
    private async Task RunAsync()
    {
        bool pass = false;
        string status = "unknown";
        try
        {
            var settings = new Settings { AutoEnabled = true, SoundEnabled = false };
            keyboard = new KeyboardController(settings, selfTest: true) { SelfTestFocus = editor.Handle };
            IntPtr english = Native.LoadKeyboardLayout("00000409", 0);
            Native.ActivateKeyboardLayout(english, 0);
            Activate(); editor.Focus();
            await Task.Delay(150);
            if (!editor.Focused) throw new InvalidOperationException("Не удалось сфокусировать тестовое поле");
            await TypeKeysAsync("ghbdtn ");
            await Task.Delay(300);
            string layoutText = editor.Text;
            bool layoutPass = layoutText == "привет ";
            editor.Clear();
            await Task.Delay(100);
            // The first stage switched the field to Russian; typo correction is checked in English.
            // Words shorter than 4 letters are never typo-corrected, so the typo must be longer.
            Native.ActivateKeyboardLayout(english, 0);
            await Task.Delay(100);
            await TypeKeysAsync("becuase ");
            await Task.Delay(300);
            bool typoPass = editor.Text == "because ";
            pass = layoutPass && typoPass;
            // The test field only ever holds the synthetic selftest text, so logging it is safe.
            status = $"layout_match={layoutPass} typo_match={typoPass} layout_actual=\"{layoutText}\" typo_actual=\"{editor.Text}\"";
        }
        catch (Exception error) { status = $"exception={error.GetType().Name} hresult=0x{error.HResult:X} message={error.Message}"; }
        finally { keyboard?.Dispose(); Diagnostics.Write($"selftest pass={pass} {status}"); Diagnostics.FlushNow(); }
        Environment.ExitCode = pass ? 0 : 1;
        if (!quiet) MessageBox.Show(pass ? "Самопроверка пройдена: ghbdtn → привет; becuase → because" : "Самопроверка не пройдена. Откройте %AppData%\\KeySwitch\\diag.log и передайте лог разработчику.",
            "KeySwitch — самопроверка", MessageBoxButtons.OK, pass ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        Close();
    }

    // Types physical keys by their US positions (a-z, space); the active layout decides the characters.
    private async Task TypeKeysAsync(string keys)
    {
        foreach (char ch in keys)
        {
            if (!FocusGuard.TryGetSnapshot(out var focused) || focused.Focus != editor.Handle || focused.Window != Handle)
                throw new InvalidOperationException("Фокус покинул тестовое поле");
            ushort vk = ch switch
            {
                ' ' => (ushort)Keys.Space,
                >= 'a' and <= 'z' => (ushort)((int)Keys.A + (ch - 'a')),
                _ => throw new InvalidOperationException("Недопустимый символ самопроверки")
            };
            if (!Native.Send(Native.Key(vk, tag: Native.SelfTestTag), Native.Key(vk, true, Native.SelfTestTag)))
                throw new InvalidOperationException("SendInput не доставил тестовую клавишу");
            await Task.Delay(45);
        }
    }
}
