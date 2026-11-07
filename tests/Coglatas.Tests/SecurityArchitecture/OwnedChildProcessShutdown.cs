using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Coglatas.Tests.SecurityArchitecture;

// Only accepts the original Process returned by Process.Start for our own child.
internal static class OwnedChildProcessShutdown
{
    internal static async Task StopAsync(Process child, TimeSpan gracefulTimeout)
    {
        if (child.HasExited) return;
        if (TryTerminate(child))
        {
            try
            {
                await child.WaitForExitAsync().WaitAsync(gracefulTimeout);
                return;
            }
            catch (TimeoutException) { /* Retain the existing bounded force-stop fallback. */ }
        }
        if (!child.HasExited)
        {
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static bool TryTerminate(Process child)
    {
        if (!OperatingSystem.IsLinux() ||
            RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)) return false;
        try
        {
            // Linux x64/arm64 syscall numbers: pidfd_open=434, pidfd_send_signal=424.
            // Pin a process identity before checking the original child's exit state.
            // .NET retains that child's exit state even after reaping it. If the PID
            // was already reused, do not signal the newly opened descriptor. Once
            // checked, this descriptor cannot retarget a subsequently reused PID.
            var descriptor = OpenPidFileDescriptor(434, child.Id, 0);
            if (descriptor < 0) return false;
            using var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
            if (child.HasExited) return true;
            return SendPidFileDescriptorSignal(424, handle, 15, IntPtr.Zero, 0) == 0;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("libc", EntryPoint = "syscall")]
    private static extern long OpenPidFileDescriptor(long number, int pid, uint flags);

    [DllImport("libc", EntryPoint = "syscall")]
    private static extern long SendPidFileDescriptorSignal(long number, SafeFileHandle descriptor,
        int signal, IntPtr information, uint flags);
}
