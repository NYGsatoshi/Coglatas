using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Coglatas.SecurityArchitecture;

/// <summary>Selects Git from explicit host installation directories, never from the inspected repository.</summary>
internal static class RepositoryGitExecutable
{
    internal static string Resolve(string repositoryRoot, string? searchPath)
    {
        var windows = OperatingSystem.IsWindows();
        var windowsRoot = windows ? WindowsIdentity(repositoryRoot) : default;
        var root = windows ? string.Empty : PhysicalPath(repositoryRoot);
        var mounts = OperatingSystem.IsLinux() ? new RepositoryLinuxMountTable(File.ReadLines("/proc/self/mountinfo"), PhysicalPath) : null;
        foreach (var rawEntry in (searchPath ?? "").Split(Path.PathSeparator))
        {
            // Windows PATH commonly quotes installation paths containing spaces. Unix quotes are literal.
            var entry = OperatingSystem.IsWindows() && rawEntry is ['"', .., '"']
                ? rawEntry[1..^1] : rawEntry;
            if (entry.Contains('\0') || !Path.IsPathFullyQualified(entry)) continue;
            try
            {
                var candidate = Path.Combine(entry, OperatingSystem.IsWindows() ? "git.exe" : "git");
                if (windows)
                {
                    if (!File.Exists(candidate)) continue;
                    var executable = WindowsExecutablePath(candidate);
                    if (IsWithinWindowsRepository(executable, windowsRoot)) continue;
                    return executable;
                }
                // Resolve the directory physically but retain the Git invocation name for trusted multicall shims.
                var directory = PhysicalPath(entry);
                candidate = Path.Combine(directory, "git");
                var physical = PhysicalPath(candidate);
                if (!File.Exists(physical)) continue;
                var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
                if (directory.Equals(root, StringComparison.Ordinal) || directory.StartsWith(prefix, StringComparison.Ordinal) ||
                    physical.Equals(root, StringComparison.Ordinal) || physical.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (mounts is not null && (mounts.IsWithinRepository(root, directory) || mounts.IsWithinRepository(root, physical))) continue;
                if (Access(physical, 1) != 0) continue;
                return candidate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception)
            {
                // An unusable host PATH entry does not authorize falling back to implicit current-directory search.
            }
        }
        throw new FileNotFoundException("No supported Git installation is available outside the inspected repository.");
    }

    private static string PhysicalPath(string path)
    {
        if (path.Contains('\0')) throw new ArgumentException("Unsupported path.");
        var pointer = RealPath(path, IntPtr.Zero);
        if (pointer == IntPtr.Zero) throw new IOException("Cannot resolve the physical path.");
        try { return Marshal.PtrToStringUTF8(pointer) ?? throw new IOException("Cannot resolve the physical path."); }
        finally { Free(pointer); }
    }

    private static string WindowsExecutablePath(string candidate)
    {
        using var handle = OpenWindowsPath(candidate);
        var path = FinalWindowsPath(handle, 0);
        // Git for Windows' launcher must receive its ordinary absolute installation path.
        path = path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
            : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
        if (!Path.IsPathFullyQualified(path) || !SameWindowsIdentity(WindowsIdentity(path), WindowsIdentity(handle)))
            throw new IOException("Unsupported executable path.");
        return path;
    }

    private static bool IsWithinWindowsRepository(string executable, WindowsFileIdentity root)
    {
        // Compare actual ancestor identities, including local/UNC/mapped-drive aliases of the same directory.
        for (string? path = executable; path is not null; path = Path.GetDirectoryName(path))
            if (SameWindowsIdentity(WindowsIdentity(path), root)) return true;
        return false;
    }

    private static WindowsFileIdentity WindowsIdentity(string path)
    {
        if (path.Contains('\0')) throw new ArgumentException("Unsupported path.");
        using var handle = OpenWindowsPath(path);
        return WindowsIdentity(handle);
    }

    private static WindowsFileIdentity WindowsIdentity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 18, out var identity, 24))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return identity;
    }

    private static bool SameWindowsIdentity(WindowsFileIdentity first, WindowsFileIdentity second) =>
        first.VolumeSerialNumber == second.VolumeSerialNumber && first.FileIdLow == second.FileIdLow &&
        first.FileIdHigh == second.FileIdHigh;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileIdentity
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    private static SafeFileHandle OpenWindowsPath(string path)
    {
        var handle = CreateFile(Path.GetFullPath(path), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        throw new Win32Exception(error);
    }

    private static string FinalWindowsPath(SafeFileHandle handle, uint flags)
    {
        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, flags);
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder(checked((int)length + 1));
            length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, flags);
        }
        if (length == 0 || length >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return buffer.ToString();
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out WindowsFileIdentity information, uint size);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    private static extern IntPtr RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr resolved);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint = "free")]
    private static extern void Free(IntPtr pointer);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint = "access", SetLastError = true)]
    private static extern int Access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
}
