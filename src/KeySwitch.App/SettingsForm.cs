namespace KeySwitch.App;

internal sealed class SettingsForm : Form
{
    private readonly Settings settings;
    private readonly TextBox exceptions = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox excluded = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox typoExceptions = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly ComboBox grammarLanguage = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly TextBox convert = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox selection = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox toggle = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox grammar = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly CheckBox autostart = new() { Text = "Запускать вместе с Windows", AutoSize = true };
    private readonly CheckBox sound = new() { Text = "Звук при исправлении", AutoSize = true };
    private readonly CheckBox doubleShift = new() { Text = "Двойной Shift: исправить слово / отменить", AutoSize = true };
    private readonly CheckBox typo = new() { Text = "Исправлять опечатки", AutoSize = true };
    private readonly CheckBox grammarEnabled = new() { Text = "Проверка грамматики по горячей клавише", AutoSize = true };
    private Hotkey convertValue, selectionValue, toggleValue, grammarValue;

    internal SettingsForm(Settings settings, int page)
    {
        this.settings = settings;
        convertValue = settings.ConvertHotkey;
        selectionValue = settings.SelectionHotkey;
        toggleValue = settings.ToggleHotkey;
        grammarValue = settings.GrammarHotkey;
        Text = "Настройки KeySwitch";
        Width = 580; Height = 470; MinimumSize = new Size(500, 400);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(EditPage("Исключения", exceptions, "Одно слово в строке. Эти слова KeySwitch не исправляет автоматически."));
        tabs.TabPages.Add(EditPage("Приложения", excluded, "Имена процессов, по одному в строке (например, mstsc.exe)."));
        tabs.TabPages.Add(HotkeyPage());
        tabs.TabPages.Add(GeneralPage());
        tabs.TabPages.Add(EditPage("Опечатки: исключения", typoExceptions, "Опечатки, отменённые дважды, сохраняются здесь. Удалите строку, чтобы снова разрешить исправление."));
        tabs.SelectedIndex = Math.Clamp(page, 0, tabs.TabPages.Count - 1);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var save = new Button { Text = "Сохранить", Width = 110 };
        var cancel = new Button { Text = "Отмена", Width = 100, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => SaveSettings();
        footer.Controls.Add(save); footer.Controls.Add(cancel);
        Controls.Add(tabs); Controls.Add(footer);
        AcceptButton = save; CancelButton = cancel;
        exceptions.Text = string.Join(Environment.NewLine, settings.Exceptions);
        excluded.Text = string.Join(Environment.NewLine, settings.ExcludedProcesses);
        typoExceptions.Text = string.Join(Environment.NewLine, settings.TypoExceptions);
        autostart.Checked = Settings.AutoStart;
        sound.Checked = settings.SoundEnabled;
        doubleShift.Checked = settings.DoubleShiftEnabled;
        typo.Checked = settings.TypoEnabled;
        grammarEnabled.Checked = settings.GrammarEnabled;
        grammarLanguage.Items.AddRange(new object[] { "auto", "ru-RU", "en-US" });
        grammarLanguage.SelectedItem = grammarLanguage.Items.Contains(settings.GrammarLanguage) ? settings.GrammarLanguage : "auto";
        RefreshHotkeys();
    }

    private static TabPage EditPage(string title, Control editor, string help)
    {
        var page = new TabPage(title);
        var label = new Label { Text = help, Dock = DockStyle.Top, Height = 43, Padding = new Padding(8) };
        page.Controls.Add(editor); page.Controls.Add(label);
        return page;
    }

    private TabPage HotkeyPage()
    {
        var page = new TabPage("Клавиши");
        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 5, Padding = new Padding(12), Height = 250 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        AddHotkeyRow(table, 0, "Исправить слово / отменить:", convert, value => convertValue = value);
        AddHotkeyRow(table, 1, "Исправить выделение:", selection, value => selectionValue = value);
        AddHotkeyRow(table, 2, "Включить / выключить:", toggle, value => toggleValue = value);
        AddHotkeyRow(table, 3, "Проверить грамматику выделения:", grammar, value => grammarValue = value);
        table.Controls.Add(doubleShift, 0, 4);
        table.SetColumnSpan(doubleShift, 2);
        var help = new Label { Text = "Щёлкните поле и нажмите сочетание. Для исправления слова используйте клавишу без Ctrl, Alt и Shift.", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(12) };
        page.Controls.Add(help); page.Controls.Add(table);
        return page;
    }
    private static void AddHotkeyRow(TableLayoutPanel table, int row, string title, TextBox box, Action<Hotkey> setter)
    {
        table.Controls.Add(new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(box, 1, row);
        box.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.ShiftKey or Keys.ControlKey or Keys.Menu) return;
            var hotkey = Hotkey.FromKeyEvent(e);
            if (hotkey.Key == Keys.Cancel && hotkey.Control) hotkey.Key = Keys.Pause;
            if (!hotkey.Valid) return;
            setter(hotkey);
            box.Text = hotkey.ToString();
        };
    }
    private TabPage GeneralPage()
    {
        var page = new TabPage("Общие");
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(15) };
        stack.Controls.Add(autostart); stack.Controls.Add(sound); stack.Controls.Add(typo); stack.Controls.Add(grammarEnabled);
        stack.Controls.Add(new Label { Text = "Язык проверки LanguageTool:", AutoSize = true });
        stack.Controls.Add(grammarLanguage);
        var privacy = new LinkLabel { Text = "LanguageTool: политика конфиденциальности", AutoSize = true };
        privacy.LinkClicked += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://languagetool.org/legal/privacy") { UseShellExecute = true });
        stack.Controls.Add(privacy);
        page.Controls.Add(stack);
        return page;
    }
    private void RefreshHotkeys()
    {
        convert.Text = convertValue.ToString(); selection.Text = selectionValue.ToString(); toggle.Text = toggleValue.ToString(); grammar.Text = grammarValue.ToString();
    }
    private void SaveSettings()
    {
        var hotkeys = new[] { convertValue, selectionValue, toggleValue, grammarValue };
        if (!convertValue.Valid || convertValue.Shift || convertValue.Control || convertValue.Alt || hotkeys.Any(x => !x.Valid) ||
            hotkeys.Select(x => x.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != hotkeys.Length)
        {
            MessageBox.Show("Проверьте горячие клавиши: исправление слова должно быть без модификаторов, сочетания не должны совпадать.", "KeySwitch");
            return;
        }
        settings.Exceptions = SplitLines(exceptions.Text);
        settings.ExcludedProcesses = SplitLines(excluded.Text);
        settings.TypoExceptions = SplitLines(typoExceptions.Text);
        settings.ConvertHotkey = convertValue;
        settings.SelectionHotkey = selectionValue;
        settings.ToggleHotkey = toggleValue;
        settings.GrammarHotkey = grammarValue;
        settings.SoundEnabled = sound.Checked;
        settings.DoubleShiftEnabled = doubleShift.Checked;
        settings.TypoEnabled = typo.Checked;
        settings.GrammarEnabled = grammarEnabled.Checked;
        settings.GrammarLanguage = grammarLanguage.SelectedItem?.ToString() ?? "auto";
        Settings.AutoStart = autostart.Checked;
        DialogResult = DialogResult.OK;
    }
    private static List<string> SplitLines(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
