using System.Text.Json;
using Microsoft.Win32;

namespace KeySwitch.App;

internal sealed class Settings
{
    public bool AutoEnabled { get; set; } = true;
    public bool TypoEnabled { get; set; } = true;
    public bool RussianTypoEnabled { get; set; } = false;   // 1.2: RU typo precision 66% -> off by default
    public bool GrammarEnabled { get; set; } = true;
    public bool GrammarConsent { get; set; }
    public string GrammarLanguage { get; set; } = "auto";
    public bool DoubleShiftEnabled { get; set; } = true;
    public bool SoundEnabled { get; set; }
    public List<string> Exceptions { get; set; } = new();
    public List<string> TypoExceptions { get; set; } = new();
    public List<string> ExcludedProcesses { get; set; } = new() { "mstsc.exe", "KeePass.exe" };
    public Hotkey ConvertHotkey { get; set; } = new() { Key = Keys.Pause };
    public Hotkey SelectionHotkey { get; set; } = new() { Key = Keys.Pause, Shift = true };
    public Hotkey ToggleHotkey { get; set; } = new() { Key = Keys.Pause, Control = true };
    public Hotkey GrammarHotkey { get; set; } = new() { Key = Keys.G, Control = true, Shift = true };

    internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeySwitch");
    internal static readonly string FilePath = Path.Combine(Folder, "settings.json");

    internal static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Settings();
            var result = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
            return result ?? new Settings();
        }
        catch { return new Settings(); }
    }
    internal void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
        }
        catch { /* Settings cannot interrupt typing. */ }
    }
    internal static bool AutoStart
    {
        get
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("KeySwitch") is string; }
            catch { return false; }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (value) key.SetValue("KeySwitch", $"\"{Environment.ProcessPath}\"");
                else key.DeleteValue("KeySwitch", false);
            }
            catch { }
        }
    }
}

internal sealed class Hotkey
{
    public Keys Key { get; set; }
    public bool Shift { get; set; }
    public bool Control { get; set; }
    public bool Alt { get; set; }
    public bool Matches(Keys key, bool shift, bool control, bool alt) => Key == key && Shift == shift && Control == control && Alt == alt;
    public override string ToString() => $"{(Control ? "Ctrl+" : "")}{(Shift ? "Shift+" : "")}{(Alt ? "Alt+" : "")}{Key}";
    internal static Hotkey FromKeyEvent(KeyEventArgs e) => new()
    {
        Key = e.KeyCode, Shift = e.Shift, Control = e.Control, Alt = e.Alt
    };
    internal bool Valid => Key is >= Keys.F1 and <= Keys.F12 or Keys.Pause or Keys.Scroll or Keys.Insert or Keys.Home or Keys.End
        || (Key is >= Keys.A and <= Keys.Z && (Shift || Control || Alt));
}
