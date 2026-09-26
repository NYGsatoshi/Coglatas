using System.Globalization;
using System.Text.Json.Serialization;

namespace Coglatas.Application.Projects;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskExecutionSourceKind
{
    Web = 0,
    WebSite = 1,
    ProjectFile = 2,
    ConnectedApp = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskExecutionSourceState
{
    Allow = 0,
    Prioritize = 1,
    Exclude = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskExecutionSourcePolicyOwnerType
{
    Project = 0,
    Task = 1,
    Run = 2
}

public sealed record TaskExecutionSourceRule(
    TaskExecutionSourceKind Kind,
    string SourceId,
    TaskExecutionSourceState State);

/// <summary>
/// Versioned itemized source policy introduced by Issue #361. Item rules
/// override their kind default. Prioritize is an allowed state with a stronger
/// ordering preference; it never needs a second allow flag.
/// </summary>
public sealed record TaskExecutionSourcePolicyV2(
    int SchemaVersion,
    TaskExecutionSourceState Web,
    TaskExecutionSourceState WebSite,
    TaskExecutionSourceState ProjectFile,
    TaskExecutionSourceState ConnectedApp,
    IReadOnlyList<TaskExecutionSourceRule> Items)
{
    public const int CurrentSchemaVersion = 2;
    public const int MaxItemRules = 256;
    public const int MaxSourceIdLength = 256;

    public static TaskExecutionSourcePolicyV2 FromLegacy(bool webEnabled, bool projectFilesEnabled) => new(
        CurrentSchemaVersion,
        webEnabled ? TaskExecutionSourceState.Allow : TaskExecutionSourceState.Exclude,
        TaskExecutionSourceState.Exclude,
        projectFilesEnabled ? TaskExecutionSourceState.Allow : TaskExecutionSourceState.Exclude,
        TaskExecutionSourceState.Exclude,
        []);

    public TaskExecutionSourceState DefaultFor(TaskExecutionSourceKind kind) => kind switch
    {
        TaskExecutionSourceKind.Web => Web,
        TaskExecutionSourceKind.WebSite => WebSite,
        TaskExecutionSourceKind.ProjectFile => ProjectFile,
        TaskExecutionSourceKind.ConnectedApp => ConnectedApp,
        _ => TaskExecutionSourceState.Exclude
    };

    public TaskExecutionSourceState Resolve(TaskExecutionSourceKind kind, string sourceId)
    {
        var item = Items.FirstOrDefault(rule =>
            rule.Kind == kind && string.Equals(rule.SourceId, sourceId, StringComparison.Ordinal));
        return item?.State ?? DefaultFor(kind);
    }

    public bool WebEnabled =>
        Web != TaskExecutionSourceState.Exclude ||
        Items.Any(rule => rule.Kind == TaskExecutionSourceKind.Web && rule.State != TaskExecutionSourceState.Exclude);

    public bool ProjectFilesEnabled =>
        ProjectFile != TaskExecutionSourceState.Exclude ||
        Items.Any(rule => rule.Kind == TaskExecutionSourceKind.ProjectFile && rule.State != TaskExecutionSourceState.Exclude);

    public bool HasUnsupportedExecutableSources =>
        Web != TaskExecutionSourceState.Exclude ||
        WebSite != TaskExecutionSourceState.Exclude ||
        ConnectedApp != TaskExecutionSourceState.Exclude ||
        Items.Any(rule =>
            (rule.Kind is TaskExecutionSourceKind.Web or TaskExecutionSourceKind.WebSite or TaskExecutionSourceKind.ConnectedApp) &&
            rule.State != TaskExecutionSourceState.Exclude);

    public bool TryNormalize(out TaskExecutionSourcePolicyV2 normalized, out string? target, out string? message)
    {
        normalized = this;
        target = null;
        message = null;

        if (SchemaVersion != CurrentSchemaVersion)
        {
            target = "policyV2.schemaVersion";
            message = $"Source-policy schemaVersion must be {CurrentSchemaVersion}.";
            return false;
        }

        if (Items is null)
        {
            target = "policyV2.items";
            message = $"Source policy may contain at most {MaxItemRules} item rules.";
            return false;
        }

        var itemCount = Items.Count;
        if (itemCount > MaxItemRules)
        {
            target = "policyV2.items";
            message = $"Source policy may contain at most {MaxItemRules} item rules.";
            return false;
        }

        // Treat MaxItemRules as an execution-resource boundary, not merely a
        // validation hint. Request-bound itemCount is used only to detect the
        // end of the already-bounded input; it never controls allocation size
        // or the loop's maximum number of iterations.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalizedItems = new List<TaskExecutionSourceRule>(MaxItemRules);
        for (var index = 0; index < MaxItemRules; index++)
        {
            if (index == itemCount)
            {
                break;
            }

            var rule = Items[index];
            if (!TryNormalizeSourceId(rule.Kind, rule.SourceId, out var sourceId))
            {
                target = $"policyV2.items[{index}].sourceId";
                message = "Source identifier is invalid for its source kind.";
                return false;
            }

            var key = $"{(int)rule.Kind}:{sourceId}";
            if (!seen.Add(key))
            {
                target = $"policyV2.items[{index}]";
                message = "A source may have only one item rule.";
                return false;
            }

            normalizedItems.Add(rule with { SourceId = sourceId });
        }

        normalized = this with { Items = normalizedItems };
        return true;
    }

    public static string ProjectFileSourceId(Guid fileObjectId) => $"file:{fileObjectId:N}";
    public static string ConnectedAppSourceId(Guid integrationAccountId) => $"app:{integrationAccountId:N}";

    public static bool TryParseProjectFileSourceId(string sourceId, out Guid fileObjectId) =>
        TryParseGuidSourceId(sourceId, "file:", out fileObjectId);

    public static bool TryParseConnectedAppSourceId(string sourceId, out Guid integrationAccountId) =>
        TryParseGuidSourceId(sourceId, "app:", out integrationAccountId);

    private static bool TryNormalizeSourceId(TaskExecutionSourceKind kind, string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxSourceIdLength)
        {
            return false;
        }

        value = value.Trim();
        switch (kind)
        {
            case TaskExecutionSourceKind.Web:
                if (!string.Equals(value, "web", StringComparison.OrdinalIgnoreCase)) return false;
                normalized = "web";
                return true;
            case TaskExecutionSourceKind.ProjectFile:
                if (!TryParseProjectFileSourceId(value, out var fileId)) return false;
                normalized = ProjectFileSourceId(fileId);
                return true;
            case TaskExecutionSourceKind.ConnectedApp:
                if (!TryParseConnectedAppSourceId(value, out var appId)) return false;
                normalized = ConnectedAppSourceId(appId);
                return true;
            case TaskExecutionSourceKind.WebSite:
                const string prefix = "site:";
                if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
                var host = value[prefix.Length..].Trim().TrimEnd('.');
                if (host.Length is 0 or > 253) return false;
                try
                {
                    host = new IdnMapping().GetAscii(host).ToLowerInvariant();
                }
                catch (ArgumentException)
                {
                    return false;
                }
                if (Uri.CheckHostName(host) is UriHostNameType.Unknown) return false;
                normalized = prefix + host;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseGuidSourceId(string sourceId, string prefix, out Guid value)
    {
        value = Guid.Empty;
        if (!sourceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return Guid.TryParse(sourceId[prefix.Length..], out value) && value != Guid.Empty;
    }
}

/// <summary>
/// Storage envelope for a Project policy, complete Task override, or immutable
/// Run policy snapshot. The policy body is provider-neutral and contains no
/// source bytes, credentials, URLs with paths, or file storage keys.
/// </summary>
public sealed record TaskExecutionSourcePolicyDocument(
    TaskExecutionSourcePolicyOwnerType OwnerType,
    Guid OwnerId,
    Guid TenantId,
    Guid WorkspaceId,
    Guid ProjectId,
    Guid? TaskItemId,
    long ProjectScopeVersion,
    long? TaskOverrideVersion,
    TaskExecutionSourcePolicyV2 Policy);

public sealed record TaskExecutionSourceInventoryItemResponse(
    TaskExecutionSourceKind Kind,
    string SourceId,
    string Label);
