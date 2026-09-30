using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace KeySwitch.App;

internal static class Diagnostics
{
    private static readonly ConcurrentQueue<string> pending = new();
    private static int writing;
    internal static bool Enabled { get; set; }
    internal static string PathName => Path.Combine(Settings.Folder, "diag.log");
    internal static void Write(string message)
    {
        if (!Enabled) return;
        pending.Enqueue($"{DateTimeOffset.Now:O} {message}");
        if (Interlocked.CompareExchange(ref writing, 1, 0) == 0) ThreadPool.QueueUserWorkItem(_ => Flush());
    }
    private static void Flush()
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            using var file = new StreamWriter(PathName, append: true);
            while (pending.TryDequeue(out string? line)) file.WriteLine(line);
        }
        catch { while (pending.TryDequeue(out _)) { } }
        finally
        {
            Volatile.Write(ref writing, 0);
            if (!pending.IsEmpty && Interlocked.CompareExchange(ref writing, 1, 0) == 0) ThreadPool.QueueUserWorkItem(_ => Flush());
        }
    }
    internal static void FlushNow()
    {
        for (int i = 0; i < 100 && (!pending.IsEmpty || Volatile.Read(ref writing) != 0); i++) Thread.Sleep(10);
    }
    internal static void Startup()
    {
        if (!Enabled) return;
        string layouts;
        try
        {
            int count = Native.GetKeyboardLayoutList(0, null);
            var items = new IntPtr[Math.Max(0, count)];
            Native.GetKeyboardLayoutList(items.Length, items);
            layouts = string.Join(",", items.Select(x => $"0x{x.ToInt64():X}"));
        }
        catch { layouts = "unavailable"; }
        bool elevated = false;
        try { using var identity = WindowsIdentity.GetCurrent(); elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); } catch { }
        Write($"startup windows={Environment.OSVersion} runtime={Environment.Version} x64={Environment.Is64BitProcess} elevated={elevated} layouts={layouts} input_size={Marshal.SizeOf<Native.Input>()}");
    }
}
