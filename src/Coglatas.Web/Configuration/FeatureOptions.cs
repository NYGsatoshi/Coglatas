namespace Coglatas.Web.Configuration;

public sealed class FeatureOptions
{
    public bool EnableRadialMenu { get; set; } = true;

    public bool EnableDockingLayout { get; set; } = true;

    public bool EnableForms { get; set; } = true;

    public bool EnableEvents { get; set; } = true;

    public bool EnableProductionTracking { get; set; } = true;

    public bool EnableWebhooks { get; set; }

    public bool EnableApiTokens { get; set; }
}
