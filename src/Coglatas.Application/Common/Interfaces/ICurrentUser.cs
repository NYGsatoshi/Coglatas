using Coglatas.Domain.Enums;

namespace Coglatas.Application.Common.Interfaces;

public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid? SessionId { get; }

    string? Email { get; }

    SystemRole? SystemRole { get; }

    bool IsAuthenticated { get; }
}
