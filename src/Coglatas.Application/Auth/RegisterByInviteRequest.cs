using System.ComponentModel.DataAnnotations;

namespace Coglatas.Application.Auth;

public sealed record RegisterByInviteRequest
{
    public RegisterByInviteRequest(string inviteToken, string displayName, string email, string password)
    {
        InviteToken = inviteToken;
        DisplayName = displayName;
        Email = email;
        Password = password;
    }

    [Required, RegularExpression("^[a-f0-9]{64}$")]
    public string InviteToken { get; init; }

    [Required]
    public string DisplayName { get; init; }

    [Required, EmailAddress]
    public string Email { get; init; }

    [Required, MinLength(8)]
    public string Password { get; init; }
}

public sealed record AcceptInviteRequest
{
    public AcceptInviteRequest(string token, string displayName, string password)
    {
        Token = token;
        DisplayName = displayName;
        Password = password;
    }

    [Required, RegularExpression("^[a-f0-9]{64}$")]
    public string Token { get; init; }

    [Required]
    public string DisplayName { get; init; }

    [Required, MinLength(8)]
    public string Password { get; init; }
}

public sealed record InviteValidationResponse(
    bool Valid,
    string Email,
    string Role,
    string TenantName,
    string WorkspaceName,
    DateTimeOffset ExpiresAt);
