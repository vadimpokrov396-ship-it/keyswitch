using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KeySwitch.App;

internal readonly record struct FocusTarget(IntPtr Window, IntPtr Focus, uint Thread, string Process);

internal sealed class FocusGuard
{
    private readonly Settings settings;
    private readonly record struct ProbeResult(bool Password, string? Identity);
    private static readonly object probeLock = new();
    private static readonly List<(FocusTarget Target, Task<ProbeResult> Task)> activeProbes = new();
    private static readonly Dictionary<(IntPtr Window, IntPtr Focus), long> passwordFocuses = new();
    internal FocusGuard(Settings settings) => this.settings = settings;

    internal bool TryGetTarget(out FocusTarget target)
    {
        target = default;
        if (!TryGetSnapshot(out var snapshot)) { Diagnostics.Write("guard reject=snapshot-unavailable"); return false; }
        try
        {
            uint pid;
            Native.GetWindowThreadProcessId(snapshot.Window, out pid);
            using var process = Process.GetProcessById((int)pid);
            string name = process.ProcessName + ".exe";
            if (settings.ExcludedProcesses.Any(x => x.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))) { Diagnostics.Write($"guard reject=excluded-process process={name}"); return false; }
            if (IsFullscreen(snapshot.Window)) { Diagnostics.Write("guard reject=fullscreen"); return false; }
            var className = new StringBuilder(128);
            Native.GetClassName(snapshot.Focus, className, className.Capacity);
            if (className.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase) &&
                (Native.GetWindowLongPtr(snapshot.Focus, Native.GWL_STYLE).ToInt64() & Native.ES_PASSWORD) != 0) { Diagnostics.Write("guard reject=native-password"); return false; }
            IntPtr context = Native.ImmGetContext(snapshot.Focus);
            if (context != IntPtr.Zero)
            {
                try { if (Native.ImmGetOpenStatus(context)) { Diagnostics.Write("guard reject=ime-open"); return false; } }
                finally { Native.ImmReleaseContext(snapshot.Focus, context); }
            }
            ushort lang = unchecked((ushort)Native.GetKeyboardLayout(snapshot.Thread).ToInt64());
            if (lang is not (0x0409 or 0x0419)) { Diagnostics.Write($"guard reject=unsupported-layout hkl=0x{Native.GetKeyboardLayout(snapshot.Thread).ToInt64():X}"); return false; }
            target = snapshot with { Process = name };
            return true;
        }
        catch (Exception error) { Diagnostics.Write($"guard reject=exception type={error.GetType().Name} hresult=0x{error.HResult:X}"); return false; }
    }

    internal bool IsSame(FocusTarget target) => TryGetTarget(out var current) && current.Window == target.Window && current.Focus == target.Focus;
    internal static bool TryGetSnapshot(out FocusTarget target)
    {
        target = default;
        IntPtr window = Native.GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        uint thread = Native.GetWindowThreadProcessId(window, out _);
        if (thread == 0) return false;
        var gui = new Native.GuiThreadInfo { cbSize = (uint)Marshal.SizeOf<Native.GuiThreadInfo>() };
        if (!Native.GetGUIThreadInfo(thread, ref gui) || gui.hwndFocus == IntPtr.Zero) return false;
        target = new FocusTarget(window, gui.hwndFocus, thread, "");
        return true;
    }

    // UIA providers can hang. Probe on a worker with a short timeout and trust the
    // native ES_PASSWORD check when a provider does not answer (fail open for ordinary fields).
    internal async Task<string?> GetSafeIdentityAsync(FocusTarget target, int timeoutMs = 100)
    {
        Task<ProbeResult>? task;
        bool knownPassword;
        var key = (target.Window, target.Focus);
        lock (probeLock)
        {
            knownPassword = passwordFocuses.TryGetValue(key, out long until) && until > Environment.TickCount64;
            activeProbes.RemoveAll(probe => probe.Task.IsCompleted);
            task = activeProbes.FirstOrDefault(probe => probe.Target.Window == target.Window &&
                probe.Target.Focus == target.Focus).Task;
            if (task is null && activeProbes.Count < 2)
            {
                task = Task.Run(() => ProbeFocusedElement(target));
                activeProbes.Add((target, task));
            }
        }
        // Both slots can be occupied by hung providers. With no probe for this field,
        // do not infer that it is safe, even though an individual timed-out probe fails open.
        if (task is null) { Diagnostics.Write("uia outcome=capacity-exhausted"); return null; }
        ProbeResult result = default;
        bool completed = false;
        bool failed = false;
        try
        {
            if (await Task.WhenAny(task, Task.Delay(timeoutMs)) == task)
            { result = await task; completed = true; }
        }
        catch (Exception error) { failed = true; Diagnostics.Write($"uia outcome=provider-error type={error.GetType().Name} hresult=0x{error.HResult:X}"); }
        if (completed && result.Password)
        {
            Diagnostics.Write("uia outcome=password");
            lock (probeLock) passwordFocuses[key] = Environment.TickCount64 + 60000;
            return null;
        }
        if (completed && result.Identity is not null)
        {
            Diagnostics.Write("uia outcome=completed identity=available");
            lock (probeLock) passwordFocuses.Remove(key);
        }
        else if (knownPassword) { Diagnostics.Write("uia outcome=known-password"); return null; }
        else if (!failed) Diagnostics.Write(completed ? "uia outcome=completed identity=unavailable" : "uia outcome=timed-out");
        if (!IsSame(target)) { Diagnostics.Write("uia outcome=identity-changed"); return null; }
        return $"{target.Window:X}:{target.Focus:X}:{result.Identity}";
    }

    private static ProbeResult ProbeFocusedElement(FocusTarget target)
    {
        if (!TryGetSnapshot(out var before) || before.Window != target.Window || before.Focus != target.Focus) return default;
        object? automationObject = null;
        IUiaElement? element = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E"));
            if (type is null) return default;
            automationObject = Activator.CreateInstance(type);
            if (automationObject is not IUiaAutomation automation) return default;
            if (automation.GetFocusedElement(out element) != 0 || element is null) return default;
            if (!TryGetSnapshot(out var after) || after.Window != target.Window || after.Focus != target.Focus) return default;
            if (element.GetCurrentPropertyValue(30019, out object value) == 0 && value is bool isPassword && isPassword)
                return new ProbeResult(true, null);
            if (element.GetRuntimeId(out int[] runtimeId) == 0 && runtimeId is { Length: > 0 })
                return new ProbeResult(false, string.Join(",", runtimeId));
            return default;
        }
        catch { return default; }
        finally
        {
            try { if (element is not null) Marshal.ReleaseComObject(element); } catch { }
            try { if (automationObject is not null) Marshal.ReleaseComObject(automationObject); } catch { }
        }
    }

    private static bool IsFullscreen(IntPtr window)
    {
        const long WS_CAPTION = 0x00C00000;
        if ((Native.GetWindowLongPtr(window, Native.GWL_STYLE).ToInt64() & WS_CAPTION) == WS_CAPTION) return false;
        if (!Native.GetWindowRect(window, out var rect)) return true;
        IntPtr monitor = Native.MonitorFromWindow(window, Native.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return true;
        var info = new Native.MonitorInfo { cbSize = (uint)Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return true;
        var screen = info.rcMonitor;
        return rect.left <= screen.left + 2 && rect.top <= screen.top + 2 &&
               rect.right >= screen.right - 2 && rect.bottom >= screen.bottom - 2;
    }

    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUiaAutomation
    {
        [PreserveSig] int CompareElements();
        [PreserveSig] int CompareRuntimeIds();
        [PreserveSig] int GetRootElement();
        [PreserveSig] int ElementFromHandle();
        [PreserveSig] int ElementFromPoint();
        [PreserveSig] int GetFocusedElement(out IUiaElement element);
    }
    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUiaElement
    {
        [PreserveSig] int SetFocus();
        [PreserveSig] int GetRuntimeId([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] out int[] runtimeId);
        [PreserveSig] int FindFirst();
        [PreserveSig] int FindAll();
        [PreserveSig] int FindFirstBuildCache();
        [PreserveSig] int FindAllBuildCache();
        [PreserveSig] int BuildUpdatedCache();
        [PreserveSig] int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object value);
    }
}
