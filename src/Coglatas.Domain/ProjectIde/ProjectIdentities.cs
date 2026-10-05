using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coglatas.Domain.ProjectIde;

public interface IProjectIdentity<TSelf> where TSelf : struct, IProjectIdentity<TSelf>
{
    Guid Value { get; }
    static abstract TSelf Parse(string text);
}

internal static class ProjectIdentity
{
    internal static Guid Validate(Guid value)
    {
        if (value.Version != 4 || (value.ToByteArray(true)[8] & 0xc0) != 0x80)
            throw new ArgumentException("A domain identity must be an RFC UUID version 4.", nameof(value));
        return value;
    }

    internal static Guid Parse(string text)
    {
        if (text.Length != 36 || !Guid.TryParseExact(text, "D", out var value))
            throw new FormatException("An identity must be a hyphenated UUID.");
        try { return Validate(value); }
        catch (ArgumentException error) { throw new FormatException("An identity must be an RFC UUID version 4.", error); }
    }

    internal static bool TryParse(string? text, out Guid value)
    {
        value = default;
        if (text is null) return false;
        try { value = Parse(text); return true; }
        catch (FormatException) { return false; }
    }

    internal static string Format(Guid value) => Validate(value).ToString("D");
}

public sealed class ProjectIdentityJsonConverter<T> : JsonConverter<T> where T : struct, IProjectIdentity<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("An identity must be a UUID string.");
        try { return T.Parse(reader.GetString()!); }
        catch (FormatException error) { throw new JsonException("Invalid typed identity.", error); }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ProjectIdentity.Format(value.Value));
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<TenantId>))]
public readonly record struct TenantId : IProjectIdentity<TenantId>
{
    public Guid Value { get; }
    public TenantId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static TenantId New() => new(Guid.NewGuid());
    public static TenantId Parse(string text) => new(ProjectIdentity.Parse(text));
    public static bool TryParse(string? text, out TenantId value)
    {
        var valid = ProjectIdentity.TryParse(text, out var parsed);
        value = valid ? new(parsed) : default;
        return valid;
    }
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<ProjectId>))]
public readonly record struct ProjectId : IProjectIdentity<ProjectId>
{
    public Guid Value { get; }
    public ProjectId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static ProjectId New() => new(Guid.NewGuid());
    public static ProjectId Parse(string text) => new(ProjectIdentity.Parse(text));
    public static bool TryParse(string? text, out ProjectId value)
    {
        var valid = ProjectIdentity.TryParse(text, out var parsed);
        value = valid ? new(parsed) : default;
        return valid;
    }
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<EntityId>))]
public readonly record struct EntityId : IProjectIdentity<EntityId>
{
    public Guid Value { get; }
    public EntityId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static EntityId New() => new(Guid.NewGuid());
    public static EntityId Parse(string text) => new(ProjectIdentity.Parse(text));
    public static bool TryParse(string? text, out EntityId value)
    {
        var valid = ProjectIdentity.TryParse(text, out var parsed);
        value = valid ? new(parsed) : default;
        return valid;
    }
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<RelationId>))]
public readonly record struct RelationId : IProjectIdentity<RelationId>
{
    public Guid Value { get; }
    public RelationId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static RelationId New() => new(Guid.NewGuid());
    public static RelationId Parse(string text) => new(ProjectIdentity.Parse(text));
    public static bool TryParse(string? text, out RelationId value)
    {
        var valid = ProjectIdentity.TryParse(text, out var parsed);
        value = valid ? new(parsed) : default;
        return valid;
    }
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<BranchId>))]
public readonly record struct BranchId : IProjectIdentity<BranchId>
{
    public Guid Value { get; }
    public BranchId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static BranchId New() => new(Guid.NewGuid());
    public static BranchId Parse(string text) => new(ProjectIdentity.Parse(text));
    public static bool TryParse(string? text, out BranchId value)
    {
        var valid = ProjectIdentity.TryParse(text, out var parsed);
        value = valid ? new(parsed) : default;
        return valid;
    }
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<RevisionId>))]
public readonly record struct RevisionId : IProjectIdentity<RevisionId>
{
    public Guid Value { get; }
    public RevisionId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static RevisionId New() => new(Guid.NewGuid());
    public static RevisionId Parse(string text) => new(ProjectIdentity.Parse(text));
    public static bool TryParse(string? text, out RevisionId value)
    {
        var valid = ProjectIdentity.TryParse(text, out var parsed);
        value = valid ? new(parsed) : default;
        return valid;
    }
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<ProposalId>))]
public readonly record struct ProposalId : IProjectIdentity<ProposalId>
{
    public Guid Value { get; }
    public ProposalId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static ProposalId New() => new(Guid.NewGuid());
    public static ProposalId Parse(string text) => new(ProjectIdentity.Parse(text));
    public static bool TryParse(string? text, out ProposalId value)
    {
        var valid = ProjectIdentity.TryParse(text, out var parsed);
        value = valid ? new(parsed) : default;
        return valid;
    }
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<CandidateRevisionId>))]
public readonly record struct CandidateRevisionId : IProjectIdentity<CandidateRevisionId>
{
    public Guid Value { get; }
    public CandidateRevisionId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static CandidateRevisionId New() => new(Guid.NewGuid());
    public static CandidateRevisionId Parse(string text) => new(ProjectIdentity.Parse(text));
    public static bool TryParse(string? text, out CandidateRevisionId value)
    {
        var valid = ProjectIdentity.TryParse(text, out var parsed);
        value = valid ? new(parsed) : default;
        return valid;
    }
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<ScenarioId>))]
public readonly record struct ScenarioId : IProjectIdentity<ScenarioId>
{
    public Guid Value { get; }
    public ScenarioId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static ScenarioId New() => new(Guid.NewGuid());
    public static ScenarioId Parse(string text) => new(ProjectIdentity.Parse(text));
    public static bool TryParse(string? text, out ScenarioId value)
    {
        var valid = ProjectIdentity.TryParse(text, out var parsed);
        value = valid ? new(parsed) : default;
        return valid;
    }
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<ScenarioRevisionId>))]
public readonly record struct ScenarioRevisionId : IProjectIdentity<ScenarioRevisionId>
{
    public Guid Value { get; }
    public ScenarioRevisionId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static ScenarioRevisionId New() => new(Guid.NewGuid());
    public static ScenarioRevisionId Parse(string text) => new(ProjectIdentity.Parse(text));
    public static bool TryParse(string? text, out ScenarioRevisionId value)
    {
        var valid = ProjectIdentity.TryParse(text, out var parsed);
        value = valid ? new(parsed) : default;
        return valid;
    }
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<DocumentId>))]
public readonly record struct DocumentId : IProjectIdentity<DocumentId>
{
    public Guid Value { get; }
    public DocumentId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static DocumentId New() => new(Guid.NewGuid());
    public static DocumentId Parse(string text) => new(ProjectIdentity.Parse(text));
    public static bool TryParse(string? text, out DocumentId value)
    {
        var valid = ProjectIdentity.TryParse(text, out var parsed);
        value = valid ? new(parsed) : default;
        return valid;
    }
    public override string ToString() => ProjectIdentity.Format(Value);
}
