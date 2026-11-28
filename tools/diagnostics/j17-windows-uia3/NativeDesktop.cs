#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace J17.WindowsProbe;

public sealed class DesktopAvailability
{
    public bool Windows { get; init; }
    public bool UserInteractive { get; init; }
    public bool NonServiceSession { get; init; }
    public bool InteractiveWindowStation { get; init; }
    public bool CurrentInputDesktop { get; init; }
    public bool Available => Windows && UserInteractive && NonServiceSession && InteractiveWindowStation && CurrentInputDesktop;
}

/// <summary>Read-only desktop feasibility and foreground ownership checks. Never switches or unlocks a desktop.</summary>
public static class NativeDesktop
{
    public static DesktopAvailability Capture()
    {
        if (!OperatingSystem.IsWindows()) return new DesktopAvailability();
        var input = OpenInputDesktop(0, false, 1);
        try
        {
            var currentName = Name(GetThreadDesktop(GetCurrentThreadId()));
            var inputName = Name(input);
            return new DesktopAvailability
            {
                Windows = true,
                UserInteractive = Environment.UserInteractive,
                NonServiceSession = ProcessIdToSessionId(GetCurrentProcessId(), out var session) && session != 0,
                InteractiveWindowStation = Name(GetProcessWindowStation()) == "WinSta0",
                CurrentInputDesktop = input != IntPtr.Zero && currentName == "Default" && currentName == inputName
            };
        }
        finally { if (input != IntPtr.Zero) CloseDesktop(input); }
    }

    public static bool ForegroundBelongsTo(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0) return false;
        var window = GetForegroundWindow();
        return window != IntPtr.Zero && GetWindowThreadProcessId(window, out var actual) != 0 && actual == (uint)processId;
    }

    public static bool ActivateOwnedWindow(IntPtr window, int processId)
    {
        if (!OperatingSystem.IsWindows() || window == IntPtr.Zero || processId <= 0) return false;
        return GetWindowThreadProcessId(window, out var actual) != 0 && actual == (uint)processId && SetForegroundWindow(window);
    }

    private static string? Name(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        var name = new StringBuilder(256);
        return GetUserObjectInformation(handle, 2, name, name.Capacity * sizeof(char), out _) ? name.ToString() : null;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int length, out int needed);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
