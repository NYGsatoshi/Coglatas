namespace Coglatas.Domain.ProjectIde;

/// <summary>Versioned declarative tests of immutable local Task input defaults, never live execution.</summary>
public sealed class ProjectTestPlanDeclaration : PlanDeclaration
{
    public IReadOnlyList<ProjectTestAssertion> Assertions { get; }
    internal ProjectTestPlanDeclaration(SourceJson data, DocumentId document) : base(data, document)
    {
        Fields("assertions");
        var items = SourceFields.Array(Payload.Value, "assertions");
        if (items.GetArrayLength() > 64) throw new FormatException("Test assertion bound exceeded.");
        var assertions = items.EnumerateArray().Select(item => new ProjectTestAssertion(SourceJson.FromElement(item, Data.Limits))).ToArray();
        if (assertions.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != assertions.Length)
            throw new FormatException("Duplicate assertion key.");
        Assertions = new ImmutableListView<ProjectTestAssertion>(assertions);
    }
}

public sealed class ProjectTestAssertion
{
    public string Key { get; }
    public string Kind { get; }
    public PlanEntityReference Target { get; }
    public PortId PortId { get; }
    public TypedProjectValue? Expected { get; }
    public int? ExpectedCount { get; }
    public string? Comparison { get; }
    public SourceJson Data { get; }
    internal ProjectTestAssertion(SourceJson data)
    {
        Data = data; Key = SourceFields.Token(data.Value, "key"); Kind = SourceFields.Token(data.Value, "kind");
        Target = new(SourceJson.FromElement(SourceFields.Object(data.Value, "target"), data.Limits));
        PortId = SourceFields.Identity<PortId>(data.Value, "portId");
        var fields = new List<string> { "key", "kind", "target", "portId" };
        switch (Kind)
        {
            case "coglatas.assert-boolean":
            case "coglatas.assert-numeric-comparison":
                Expected = DeclarationFields.Value(SourceJson.FromElement(SourceFields.Object(data.Value, "expected"), data.Limits),
                    Kind == "coglatas.assert-boolean" ? ["coglatas.boolean"] : ["coglatas.decimal", "coglatas.rational"]);
                if (Expected.State != ValueState.Known) throw new FormatException("Assertion expectation must be known.");
                fields.Add("expected");
                if (Kind == "coglatas.assert-numeric-comparison")
                {
                    Comparison = SourceFields.Token(data.Value, "comparison"); fields.Add("comparison");
                    if (Comparison is not ("coglatas.equal" or "coglatas.less" or "coglatas.greater"))
                        throw new UnsupportedSourceException("Unsupported exact comparison.");
                }
                break;
            case "coglatas.assert-collection-count":
                ExpectedCount = ValueFields.Integer(data.Value, "expectedCount");
                if (ExpectedCount < 0) throw new FormatException("Collection count must be nonnegative.");
                fields.Add("expectedCount"); break;
            case "coglatas.assert-reference-resolved": break;
            default: throw new UnsupportedSourceException("Unsupported test assertion; Source retained.");
        }
        if (ValueFields.HasUnknown(data.Value, fields.ToArray())) throw new UnsupportedSourceException("Unknown assertion members.");
    }
}
