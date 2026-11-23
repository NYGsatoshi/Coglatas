namespace Coglatas.Domain.ProjectIde;

public enum RequirementTaskSuggestionState { Ready, StaleInput, OutOfScope, Unavailable, InvalidCursor }
public enum RequirementTaskSuggestionReason { DeclaredTaskInSnapshot }

/// <summary>Local continuation bound to one immutable inspection and Requirement origin.</summary>
public sealed class RequirementTaskSuggestionCursor
{
    internal CompilerSemanticInspection Owner { get; }
    internal string RequirementKey { get; }
    internal int Offset { get; }
    internal RequirementTaskSuggestionCursor(CompilerSemanticInspection owner, string requirementKey, int offset)
    { Owner = owner; RequirementKey = requirementKey; Offset = offset; }
}

public sealed record RequirementTaskSuggestion(CompilerInspectedSymbol Symbol, string? DisplayName, RequirementTaskSuggestionReason Reason);
public sealed record RequirementTaskSuggestionPage(
    RequirementTaskSuggestionState State, ContentDigest InputDigest, ProjectLocation Origin,
    IReadOnlyList<RequirementTaskSuggestion> Candidates, bool CoverageComplete, int ScannedSymbols,
    RequirementTaskSuggestionCursor? NextCursor)
{
    public bool CandidateOnly => true;
}

public sealed partial class CompilerSemanticInspection
{
    /// <summary>
    /// Read-only Task candidates with an explicit structural reason, never a
    /// satisfaction judgment or evidence. The caller owns current read authority
    /// for the materialized input; an inspection, digest or cursor grants none.
    /// Coverage describes binding; a continuation separately signals unscanned symbols.
    /// </summary>
    public RequirementTaskSuggestionPage SuggestRequirementTasks(ProjectLocation requirement, ContentDigest inputDigest,
        int pageSize = 16, RequirementTaskSuggestionCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirement); ArgumentNullException.ThrowIfNull(inputDigest);
        cancellationToken.ThrowIfCancellationRequested();
        if (pageSize is < 1 or > MaximumCompletionPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        RequirementTaskSuggestionPage Refused(RequirementTaskSuggestionState state) => new(state, Input.InputDigest, requirement,
            Array.Empty<RequirementTaskSuggestion>(), false, 0, null);
        if (!Input.InputDigest.Equals(inputDigest)) return Refused(RequirementTaskSuggestionState.StaleInput);
        if (!Input.Source.Context.Equals(requirement.Context)) return Refused(RequirementTaskSuggestionState.OutOfScope);
        var origin = Inspect(requirement);
        if (origin is null || !_uniqueKeys.Contains(origin.Symbol.Key) || origin.Symbol.Status != ValueDecodeStatus.Decoded ||
            origin.Symbol.PortId is not null || origin.Symbol.Kind != "coglatas.requirement")
            return Refused(RequirementTaskSuggestionState.Unavailable);
        if (cursor is not null && (!ReferenceEquals(cursor.Owner, this) || cursor.RequirementKey != origin.Symbol.Key))
            return Refused(RequirementTaskSuggestionState.InvalidCursor);
        var candidates = new List<RequirementTaskSuggestion>(); var offset = cursor?.Offset ?? 0; var scanned = 0;
        while (offset < Symbols.Count && scanned < MaximumCompletionScan && candidates.Count < pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested(); var candidate = Symbols[offset++]; scanned++;
            if (candidate.Symbol.PortId is not null || candidate.Symbol.Status != ValueDecodeStatus.Decoded ||
                candidate.Symbol.Kind != "coglatas.task" || !_uniqueKeys.Contains(candidate.Symbol.Key)) continue;
            candidates.Add(new(candidate, _displayNames.GetValueOrDefault(candidate.Symbol.EntityId), RequirementTaskSuggestionReason.DeclaredTaskInSnapshot));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(RequirementTaskSuggestionState.Ready, Input.InputDigest, requirement, new ImmutableListView<RequirementTaskSuggestion>(candidates), _completionCoverageComplete,
            scanned, offset < Symbols.Count ? new(this, origin.Symbol.Key, offset) : null);
    }
}
