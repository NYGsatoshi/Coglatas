using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Core;

public sealed record ProjectRelationValidationReceipt(ContentDigest SourceDigest, SourceRevisionContext Context, RelationValidationResult Result);

/// <summary>Pure application entry point shared by both clients. Hosts supply authorized frozen inputs.</summary>
public static class ProjectRelationValidation
{
    public static ProjectRelationValidationReceipt Validate(RelationScopeSnapshot input,
        IEnumerable<RelationScopeSnapshot>? snapshots = null, IEnumerable<UnitDefinition>? units = null,
        RelationValidationLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var result = RelationGraphValidator.Validate(input, snapshots, units, limits);
        cancellationToken.ThrowIfCancellationRequested();
        return new(input.Source.Digest, input.Source.Context, result);
    }
}
