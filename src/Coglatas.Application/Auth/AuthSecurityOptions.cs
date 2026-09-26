namespace Coglatas.Application.Auth;

public sealed class AuthSecurityOptions
{
    public bool LoginLockoutEnabled { get; set; } = true;

    public int MaxFailedLoginAttempts { get; set; } = 5;

    public int LoginLockoutDurationMinutes { get; set; } = 15;
}
