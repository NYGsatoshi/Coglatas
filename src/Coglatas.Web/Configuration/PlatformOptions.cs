namespace Coglatas.Web.Configuration;

public sealed class PlatformOptions
{
    public bool EnablePlatformAdmin { get; set; } = true;

    public bool PlatformAdminSetupMode { get; set; }

    public bool AllowTenantCreationFromAdmin { get; set; } = true;

    public bool EnablePlansAndSubscriptions { get; set; } = true;

    public bool EnableUsageQuota { get; set; } = true;
}
