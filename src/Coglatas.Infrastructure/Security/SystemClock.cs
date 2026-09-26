using Coglatas.Application.Common.Interfaces;

namespace Coglatas.Infrastructure.Security;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
