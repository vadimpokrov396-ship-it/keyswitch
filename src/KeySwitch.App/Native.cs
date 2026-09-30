using System.Runtime.InteropServices;
using System.Text;

namespace KeySwitch.App;

internal static class Native
{
    internal const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    internal const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    internal const int WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;
    internal const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
    internal const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;
    internal const int WM_MOUSEMOVE = 0x0200;
    internal const uint LLKHF_INJECTED = 0x10;
    internal const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004, KEYEVENTF_SCANCODE = 0x0008;
    internal const int GWL_STYLE = -16, ES_PASSWORD = 0x20;
    internal const uint MONITOR_DEFAULTTONEAREST = 2;
    internal static readonly UIntPtr InjectionTag = (UIntPtr)0x4B535743;
    internal static readonly UIntPtr SelfTestTag = (UIntPtr)0x4B535453;

    internal delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] internal struct KbdHookData
    {
        internal uint vkCode, scanCode, flags, time;
        internal UIntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseHookData
    {
        internal POINT pt;
        internal uint mouseData, flags, time;
        internal UIntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int x, y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo
    {
        internal uint cbSize, flags;
        internal IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        internal RECT rcCaret;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MonitorInfo
    {
        internal uint cbSize;
        internal RECT rcMonitor, rcWork;
        internal uint dwFlags;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Input
    {
        internal uint type;
        internal InputUnion union;
    }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
    {
        [FieldOffset(0)] internal KeybdInput keyboard;
        [FieldOffset(0)] internal MouseInput mouse;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct KeybdInput
    {
        internal ushort vk, scan;
        internal uint flags, time;
        internal UIntPtr extra;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput
    {
        internal int dx, dy;
        internal uint mouseData, flags, time;
        internal UIntPtr extra;
    }

    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int hook, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] internal static extern IntPtr SendMessageSelection(IntPtr hwnd, uint message, out int start, out int end);
    [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] internal static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] internal static extern int GetKeyboardLayoutList(int size, [Out] IntPtr[]? layouts);
    [DllImport("user32.dll")] internal static extern IntPtr ActivateKeyboardLayout(IntPtr layout, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder chars, int size, uint flags, IntPtr layout);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr LoadKeyboardLayout(string id, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("imm32.dll")] internal static extern IntPtr ImmGetContext(IntPtr hwnd);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ImmGetOpenStatus(IntPtr context);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr context);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("ole32.dll")] internal static extern int OleGetClipboard(out IntPtr dataObject);
    [DllImport("ole32.dll")] internal static extern int OleSetClipboard(IntPtr dataObject);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyIcon(IntPtr icon);

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int kind, out uint information, int length, out int returned);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    internal static bool IsCurrentProcessElevated()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out IntPtr token)) return false;
        try { return GetTokenInformation(token, 20, out uint elevated, 4, out _) && elevated != 0; }
        finally { CloseHandle(token); }
    }
    internal static bool IsProcessElevated(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint pid);
        IntPtr process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(process, 0x0008, out IntPtr token)) return false;
            try { return GetTokenInformation(token, 20, out uint elevated, 4, out _) && elevated != 0; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    internal static Input Key(ushort vk, bool up = false, UIntPtr? tag = null) => new()
    {
        type = 1,
        union = new InputUnion { keyboard = new KeybdInput { vk = vk, flags = up ? KEYEVENTF_KEYUP : 0, extra = tag ?? InjectionTag } }
    };
    internal static Input Unicode(char value, bool up = false) => new()
    {
        type = 1,
        union = new InputUnion { keyboard = new KeybdInput { scan = value, flags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0), extra = InjectionTag } }
    };
    internal static bool Send(params Input[] inputs)
    {
        if (inputs.Length == 0) return true;
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        int error = sent == inputs.Length ? 0 : Marshal.GetLastWin32Error();
        Diagnostics.Write($"sendinput requested={inputs.Length} sent={sent} error={error}");
        return sent == inputs.Length;
    }
    internal static bool TypeText(string text)
    {
        var inputs = new List<Input>(text.Length * 2);
        foreach (char ch in text) { inputs.Add(Unicode(ch)); inputs.Add(Unicode(ch, true)); }
        return Send(inputs.ToArray());
    }
    internal static bool Press(ushort vk) => Send(Key(vk), Key(vk, true));
    internal static bool Backspace(int count)
    {
        if (count < 0 || count > 256) return false;
        var inputs = new List<Input>(count * 2);
        for (int i = 0; i < count; i++) { inputs.Add(Key(0x08)); inputs.Add(Key(0x08, true)); }
        return Send(inputs.ToArray());
    }

    internal readonly record struct SendResult(uint Sent, uint Requested, int Error)
    {
        internal bool Complete => Sent == Requested;
        internal bool Untouched => Sent == 0;
    }

    internal static SendResult Replace(int eraseCount, string replacement, string? unicodeSuffix = null, ushort? virtualSuffix = null)
    {
        if (eraseCount < 0 || eraseCount > 256) return new SendResult(0, 1, 87);
        var inputs = new List<Input>((eraseCount + replacement.Length + (unicodeSuffix?.Length ?? 0) + (virtualSuffix.HasValue ? 1 : 0)) * 2);
        for (int i = 0; i < eraseCount; i++) { inputs.Add(Key(0x08)); inputs.Add(Key(0x08, true)); }
        foreach (char ch in replacement) { inputs.Add(Unicode(ch)); inputs.Add(Unicode(ch, true)); }
        if (unicodeSuffix is not null)
            foreach (char ch in unicodeSuffix) { inputs.Add(Unicode(ch)); inputs.Add(Unicode(ch, true)); }
        if (virtualSuffix is ushort vk) { inputs.Add(Key(vk)); inputs.Add(Key(vk, true)); }
        uint sent = inputs.Count == 0 ? 0 : SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        int error = sent == inputs.Count ? 0 : Marshal.GetLastWin32Error();
        Diagnostics.Write($"sendinput requested={inputs.Count} sent={sent} error={error}");
        return new SendResult(sent, (uint)inputs.Count, error);
    }
}
