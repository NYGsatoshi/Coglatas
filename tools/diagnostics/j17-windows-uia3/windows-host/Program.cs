using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<ProbeApplication>()
        .UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

public sealed class ProbeApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var input = new TextBox { Width = 240 };
            AutomationProperties.SetAutomationId(input, "probe-input");
            AutomationProperties.SetName(input, "Synthetic input");
            var action = new Button { Content = "Synthetic action" };
            AutomationProperties.SetAutomationId(action, "probe-action");
            var status = new TextBlock { Text = "Idle" };
            AutomationProperties.SetAutomationId(status, "probe-status");
            action.Click += (_, _) => status.Text = "Observed:" + input.Text;
            desktop.MainWindow = new Window
            {
                Title = "Isolated Avalonia compatibility probe",
                Width = 360, Height = 240,
                Content = new StackPanel { Children = { input, action, status } }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
