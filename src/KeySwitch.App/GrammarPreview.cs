namespace KeySwitch.App;

internal sealed class GrammarPreview : Form
{
    internal GrammarPreview(string corrected, int suggestions)
    {
        Text = "Предложения LanguageTool";
        Width = 680; Height = 420; MinimumSize = new Size(480, 280);
        StartPosition = FormStartPosition.CenterScreen;
        var editor = new TextBox { Text = corrected, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        var top = new Label { Text = $"Предложений: {suggestions}. Проверьте текст перед заменой выделения.", Dock = DockStyle.Top, Height = 34, Padding = new Padding(8) };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var apply = new Button { Text = "Применить", Width = 110, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Отмена", Width = 100, DialogResult = DialogResult.Cancel };
        var source = new LinkLabel { Text = "LanguageTool", AutoSize = true, Margin = new Padding(10, 8, 12, 0) };
        source.LinkClicked += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://languagetool.org") { UseShellExecute = true });
        footer.Controls.Add(apply); footer.Controls.Add(cancel); footer.Controls.Add(source);
        Controls.Add(editor); Controls.Add(top); Controls.Add(footer);
        AcceptButton = apply; CancelButton = cancel;
        apply.Click += (_, _) => Corrected = editor.Text;
    }
    internal string? Corrected { get; private set; }
}
