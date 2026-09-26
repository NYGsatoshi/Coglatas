namespace Coglatas.Domain.Common;

public abstract class Entity
{
    // The setter remains non-public to production callers. `internal` access is
    // used only by the friend test assembly for deterministic integration seeds.
    public Guid Id { get; protected internal set; } = Guid.NewGuid();
}
