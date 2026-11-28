using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using J17.WindowsProbe;
using UiApplication = FlaUI.Core.Application;

if (args.Length != 3)
{
    Console.Error.WriteLine("Expected synthetic host executable, public receipt and private child-ownership paths.");
    return 2;
}

if (!System.OperatingSystem.IsWindows())
{
    WriteReceipt(args[1], "NOT_RUN", "WindowsRequired", "NOT_RUN", new Dictionary<string, bool>(), true);
    return 3;
}

return RunWindowsProbe(Path.GetFullPath(args[0]), args[1], args[2]);

[SupportedOSPlatform("windows")]
static int RunWindowsProbe(string hostPath, string receiptPath, string ownershipPath)
{
    var checks = new Dictionary<string, bool>();
    var stage = "interactive-desktop";
    var desktop = NativeDesktop.Capture();
    if (!desktop.Available)
    {
        WriteReceipt(receiptPath, "NOT_RUN", "InteractiveDesktopUnavailable", "NOT_RUN", checks, true);
        return 3;
    }
    checks["interactiveDesktop"] = true;
    Process? child = null;
    var qualification = "FAIL";
    var errorType = "None";
    var attempted = false;
    var cleanupSucceeded = true;
    try
    {
        stage = "synthetic-host-admission";
        Require(File.Exists(hostPath) && Path.GetFileName(hostPath) == "WindowsHost.exe");
        var executableSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(hostPath)));
        stage = "owned-child-launch";
        attempted = true;
        child = Process.Start(new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(hostPath)!
        }) ?? throw new InvalidOperationException("Synthetic child did not start.");
        _ = child.Handle;
        File.WriteAllText(ownershipPath, JsonSerializer.Serialize(new
        {
            processId = child.Id,
            startTimeUtcTicks = child.StartTime.ToUniversalTime().Ticks,
            executablePath = hostPath,
            executableSha256
        }));
        using var app = UiApplication.Attach(child.Id);
        using var automation = new UIA3Automation();
        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException("Synthetic window unavailable.");
        RequireOwned(window, child.Id);
        var input = window.FindFirstDescendant(cf => cf.ByAutomationId("probe-input"))?.AsTextBox()
            ?? throw new InvalidOperationException("Synthetic input unavailable.");
        var button = window.FindFirstDescendant(cf => cf.ByAutomationId("probe-action"))?.AsButton()
            ?? throw new InvalidOperationException("Synthetic action unavailable.");
        var status = window.FindFirstDescendant(cf => cf.ByAutomationId("probe-status"))
            ?? throw new InvalidOperationException("Synthetic status unavailable.");
        foreach (var element in new AutomationElement[] { input, button, status }) RequireOwned(element, child.Id);
        checks["ownedProcessAndAutomationIds"] = true;

        stage = "uia-accessible-name";
        Require(input.Name == "Synthetic input" && status.Name == "Idle");
        checks["uiaAccessibleName"] = true;

        stage = "actual-keyboard-text";
        child.Refresh();
        Require(NativeDesktop.ActivateOwnedWindow(child.MainWindowHandle, child.Id));
        input.Focus();
        Require(Retry.WhileFalse(() => input.Properties.HasKeyboardFocus.Value, timeout: TimeSpan.FromSeconds(5)).Success);
        RequireForeground(child.Id);
        Keyboard.Type("synthetic");
        Require(Retry.WhileFalse(() => input.Text == "synthetic", timeout: TimeSpan.FromSeconds(5)).Success);
        checks["actualKeyboardText"] = true;

        stage = "tab-focus-traversal";
        Require(input.Properties.HasKeyboardFocus.Value);
        RequireForeground(child.Id);
        Keyboard.Type(VirtualKeyShort.TAB);
        Require(Retry.WhileFalse(() => button.Properties.HasKeyboardFocus.Value, timeout: TimeSpan.FromSeconds(5)).Success);
        checks["tabFocusTraversal"] = true;

        stage = "keyboard-activation";
        RequireForeground(child.Id);
        Require(button.Properties.HasKeyboardFocus.Value);
        Keyboard.Type(VirtualKeyShort.RETURN);
        Require(Retry.WhileFalse(() => status.Name == "Observed:synthetic", timeout: TimeSpan.FromSeconds(5)).Success);
        checks["keyboardActivation"] = true;

        stage = "window-and-process-close";
        RequireOwned(window, child.Id);
        window.Close();
        Require(child.WaitForExit(5000));
        checks["closedWindowAndProcess"] = true;
        qualification = "PASS";
    }
    catch (Exception error)
    {
        errorType = error.GetType().Name;
    }
    finally
    {
        if (child is not null)
        {
            try
            {
                if (!child.HasExited)
                {
                    _ = child.CloseMainWindow();
                    if (!child.WaitForExit(5000)) child.Kill();
                    cleanupSucceeded = child.WaitForExit(5000);
                }
            }
            catch (Exception) { cleanupSucceeded = false; }
            finally { child.Dispose(); }
        }
    }
    if (!cleanupSucceeded) qualification = "FAIL";
    WriteReceipt(receiptPath, qualification, stage, attempted ? qualification : "NOT_RUN", checks, cleanupSucceeded, errorType);
    return qualification == "PASS" ? 0 : 1;
}

[SupportedOSPlatform("windows")]
static void RequireForeground(int childId) => Require(NativeDesktop.Capture().Available && NativeDesktop.ForegroundBelongsTo(childId));

[SupportedOSPlatform("windows")]
static void RequireOwned(AutomationElement element, int childId) => Require(element.Properties.ProcessId.Value == childId);

static void Require(bool condition)
{
    if (!condition) throw new InvalidOperationException("Synthetic Windows probe condition failed.");
}

static void WriteReceipt(string path, string qualification, string stage, string actualUi,
    Dictionary<string, bool> checks, bool cleanupSucceeded, string errorType = "None")
{
    var receipt = JsonSerializer.Serialize(new
    {
        scope = "ISOLATED_SYNTHETIC_WINDOWS_UIA3_POC",
        qualification,
        stage,
        errorType,
        actualWindowsUiAutomation = actualUi,
        checks,
        cleanupSucceeded,
        imeComposition = "NOT_RUN",
        screenReaderAcceptance = "NOT_RUN",
        productClientAcceptance = "BLOCKED",
        dependencyAdoption = "NOT_ADOPTED",
        release = "NO_GO"
    }, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, receipt);
    Console.WriteLine(receipt);
}
