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
        var root = PhysicalPath(repositoryRoot);
        foreach (var rawEntry in (searchPath ?? "").Split(Path.PathSeparator))
        {
            // Windows PATH commonly quotes installation paths containing spaces. Unix quotes are literal.
            var entry = OperatingSystem.IsWindows() && rawEntry is ['"', .., '"']
                ? rawEntry[1..^1] : rawEntry;
            if (entry.Contains('\0') || !Path.IsPathFullyQualified(entry)) continue;
            try
            {
                var candidate = Path.Combine(entry, OperatingSystem.IsWindows() ? "git.exe" : "git");
                if (!File.Exists(candidate)) continue;
                var physical = PhysicalPath(candidate);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
                if (physical.Equals(root, comparison) || physical.StartsWith(prefix, comparison)) continue;
                if (!OperatingSystem.IsWindows() && Access(physical, 1) != 0) continue;
                return OperatingSystem.IsWindows() ? WindowsExecutablePath(candidate, physical) : physical;
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
        if (OperatingSystem.IsWindows())
        {
            using var handle = OpenWindowsPath(path);
            // Volume GUID form gives the same comparison namespace across drive aliases and junctions.
            return FinalWindowsPath(handle, 1);
        }
        var pointer = RealPath(path, IntPtr.Zero);
        if (pointer == IntPtr.Zero) throw new IOException("Cannot resolve the physical path.");
        try { return Marshal.PtrToStringUTF8(pointer) ?? throw new IOException("Cannot resolve the physical path."); }
        finally { Free(pointer); }
    }

    private static string WindowsExecutablePath(string candidate, string expectedPhysicalPath)
    {
        using var handle = OpenWindowsPath(candidate);
        var path = FinalWindowsPath(handle, 0);
        // Git for Windows' launcher must receive its ordinary absolute installation path.
        path = path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
            : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
        if (!Path.IsPathFullyQualified(path) || !PhysicalPath(path).Equals(expectedPhysicalPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Unsupported executable path.");
        return path;
    }

    private static SafeFileHandle OpenWindowsPath(string path)
    {
        var handle = CreateFile(Path.GetFullPath(path), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();
        throw new Win32Exception(Marshal.GetLastPInvokeError());
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
