# Requirement predicate reference review validation

Review: #1657, thread `PRRT_kwDOSy0QwM6y9NKW`, comment 4450295287.
CodeRabbit published one Major finding in review 5746497216 at exact Product
`48ff196985ddbf90db82386c5413c8f659ea64a7` on 2026-11-03 02:30 UTC.
This is a completed COMMENTED review, not Owner/CODEOWNER approval.

## Observed result

The reported path is not reproducible. `PurePredicate.Read` first decodes the
literal, then rejects any type other than `coglatas.boolean` and any Null state.
A Known Boolean cannot decode a reference-shaped object. Requirement construction
calls that parser; `PlanDeclarationReader` retains Invalid/Unsupported data,
and the binder emits its existing fail-closed declaration finding. Recursive
predicate parsing validates operands before evaluation/short circuit.

Seven independent read-only Windows probes passed against the unchanged Product:

| Probe | Observed boundary |
| --- | --- |
| Entity reference | Valid decoded reference; predicate rejected; invalid declaration, BindingComplete false, no IR, Fail |
| Document reference | Same fail-closed result |
| Resource reference | Same fail-closed result |
| External snapshot reference | Same fail-closed result |
| Reference in short-circuited OR operand | Parsing still rejects it before evaluation; no IR/Pass |
| Reference object under Boolean type | Codec/parser rejects the spoof; no IR/Pass |
| Boolean Unknown with reference-shaped provenance | Basis remains opaque and unchanged; structural binding may complete, but Requirement evidence stays unavailable, Partial/Inconclusive |

Build: SDK 10.0.403, Release, warnings-as-errors, zero warnings/errors. Probes:
7 PASS / 0 FAIL / 0 skipped. These console probes are separate from the 51
maintained new Core cases and their full cross-host qualification; no additional
TRX/unit count or Ubuntu probe execution is claimed. No Product Runtime Source
was copied or patched, no branch HEAD changed and the original Product checkout
is clean. Initial invocation wrote a result to an argument-named output file;
that owned output was preserved separately, then the corrected build/run below
was executed and the final result hashed. No generated cache was deleted.

Executed Program.cs SHA-256:
`145c04603aa2ca47dfa92718556f7d3b1926c7bb8d869341fb19eebdd4b74ce8`.
Final results.json SHA-256:
`51959c7351750eacd0f38a8352f5b6952b3ed0108f540f2499f9380f5d590732`.
The linked independent fixture is blob
`0c3853b66ffdd086ba3f06c5d93f49b93be2ff67`, identical at Test #1658
`8f701e8db3097c74c9a63c30669fbfe36be7bb48` and Test #1668
`6edddea75a8dfd0ecaca8559827ffb92ba06a365`.

A new raw payload scan would not fix a reproducible bypass and could incorrectly
interpret Unknown provenance as an execution dependency. No runtime patch is
justified by this finding. CodeRabbit subsequently withdrew the finding and resolved the original thread
in comment 4450375540 after inspecting exact Source `48ff1969`. The reviewer
explicitly did not rerun the seven probes. No runtime patch/raw scan is needed;
this withdrawal is not PR approval or Owner/CODEOWNER acceptance.

## Reproduction

Save the following two blocks as `PredicateReviewProbe.csproj` and `Program.cs`
in a new verification directory. Use exact clean Product `48ff1969` and independent
Test `6edddea7` checkouts. From the Product checkout (its global.json fixes SDK),
substitute their absolute roots and the probe/output paths:

```text
dotnet build <probe>/PredicateReviewProbe.csproj -c Release -warnaserror -p:ProductRoot=<product-checkout> -p:IndependentTestRoot=<test-checkout>
dotnet <probe>/bin/Release/net10.0/PredicateReviewProbe.dll <probe>/results.json
```

Quote any property/path containing spaces. The project links the independent
fixture and references the actual Product assembly; it contains no copied runtime.
The project block uses portable root properties rather than the executed local
absolute paths. Newline spelling in copied source may change its raw file hash;
all inputs and every assertion remain explicit.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$(ProductRoot)/src/Coglatas.Domain/Coglatas.Domain.csproj" />
    <Compile Include="$(IndependentTestRoot)/tests/Coglatas.Tests/ProjectIde/CompilerContractFixtures.cs" Link="IndependentCompilerContractFixtures.cs" />
  </ItemGroup>
</Project>
```

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

var source = Source();
var outcomes = new List<object>();
void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
JsonObject Boolean(bool value) => Obj(new { kind = "coglatas.literal", value = new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value } });
CompilerInputManifest ForCondition(JsonObject condition)
{
    var declaration = Obj(new { entityId = Id(8), documentId = Id(4), kind = "coglatas.requirement", schemaVersion = 1, payload = new
    { target = new { tenantId = Id(1), projectId = Id(2), entityId = Id(6), context = source.Context.Data.Value }, condition, evidenceKind = "coglatas.model-proof" } });
    var settings = Settings();
    foreach (var ruleId in new[] { CoreCompilerRules.Constraints, CoreCompilerRules.Requirements, CoreCompilerRules.Resources })
        settings["rules"]!.AsArray().Add(Obj(new { ruleId, version = 1, required = true, configuration = new { } }));
    return Input(settings, ProjectSource.Create(source.Context, source.Documents, Json(new JsonArray(declaration))));
}
void Rejected(JsonObject predicate, string label)
{
    var parsed = false;
    try { _ = PurePredicate.Parse(Json(predicate)); parsed = true; }
    catch (Exception error) when (error is FormatException or UnsupportedSourceException) { }
    Require(!parsed, label + " unexpectedly parsed");
    var input = ForCondition(predicate); var model = ProjectSemanticBinder.Bind(input);
    Require(!model.BindingComplete, label + " unexpectedly bound");
    Require(model.Findings.Any(f => f.Diagnostic.Code is "coglatas.binding.invalid-declaration" or "coglatas.binding.unsupported-declaration"), label + " missing fail-closed finding");
    var compiled = PureCoreCompiler.Compile(input, input.Compiler);
    Require(compiled.Validation?.Ir is null && compiled.Result.Verdict != CompilerVerdict.Pass, label + " unexpectedly executable/Pass");
    outcomes.Add(new { name = label, outcome = "Passed", bindingComplete = model.BindingComplete, verdict = compiled.Result.Verdict.ToString(), findings = model.Findings.Select(f => f.Diagnostic.Code).ToArray() });
}
JsonObject? entityLiteral = null;
foreach (var kind in new[] { "coglatas.entity-reference", "coglatas.document-reference", "coglatas.resource-reference", "coglatas.external-snapshot-reference" })
{
    var expectedKind = kind == "coglatas.document-reference" ? SourceDocument.SchemaId : kind == "coglatas.resource-reference" ? "coglatas.human" : kind == "coglatas.external-snapshot-reference" ? "example.snapshot" : "coglatas.project";
    var type = Obj(new { kind, schemaVersion = 1, nullable = false, acceptsUnknown = true, expectedKind });
    var reference = Obj(new { tenantId = Id(1), projectId = Id(2), context = source.Context.Data.Value,
        origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), source.Context, "/payload/condition/value", entityId: EntityId.Parse(Id(8))).Data.Value,
        manifest = new { version = "1", digest = source.Digest.Data.Value } });
    reference[kind switch { "coglatas.document-reference" => "documentId", "coglatas.resource-reference" => "resourceId", "coglatas.external-snapshot-reference" => "snapshotId", _ => "entityId" }] = Id(kind == "coglatas.document-reference" ? 4 : 6);
    var value = Obj(new { type, state = "coglatas.known", value = reference });
    var decoded = TypedProjectValueCodec.Decode(Json(value));
    Require(decoded.Status == ValueDecodeStatus.Decoded && decoded.Value!.KnownValue is TypedProjectReference, kind + " is not a valid typed-reference fixture");
    var literal = Obj(new { kind = "coglatas.literal", value });
    Rejected(literal, kind);
    if (kind == "coglatas.entity-reference") entityLiteral = literal;
}
Rejected(Obj(new { kind = "coglatas.or", operands = new[] { Boolean(true), entityLiteral! } }), "short-circuited-reference-operand");
var spoofed = Obj(new { kind = "coglatas.literal", value = new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value = entityLiteral!["value"]!["value"]!.DeepClone() } });
Rejected(spoofed, "reference-object-cannot-impersonate-boolean");
var unknown = Obj(new { kind = "coglatas.literal", value = new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.unknown", reason = "coglatas.missing-input", basis = entityLiteral!["value"]!.DeepClone() } });
var unknownInput = ForCondition(unknown); var before = unknownInput.Source.Data.CanonicalText;
var unknownModel = ProjectSemanticBinder.Bind(unknownInput); var unknownCompiled = PureCoreCompiler.Compile(unknownInput, unknownInput.Compiler);
Require(unknownModel.BindingComplete, "Opaque Unknown provenance was spuriously treated as a reference");
Require(unknownCompiled.Result.Verdict != CompilerVerdict.Pass && unknownCompiled.Result.Coverage == CompilerCoverage.Partial, "Unknown predicate/evidence unexpectedly passed");
Require(unknownCompiled.Result.Diagnostics.Any(d => d.Code == "coglatas.analysis.requirement-evidence-unavailable"), "Missing evidence gate was removed");
Require(before == unknownInput.Source.Data.CanonicalText, "Opaque provenance was mutated");
outcomes.Add(new { name = "unknown-basis-remains-opaque-and-incomplete", outcome = "Passed", bindingComplete = unknownModel.BindingComplete, verdict = unknownCompiled.Result.Verdict.ToString() });
File.WriteAllText(args[0], JsonSerializer.Serialize(new { productHead = "48ff196985ddbf90db82386c5413c8f659ea64a7", independentFixtureHead = "6edddea75a8dfd0ecaca8559827ffb92ba06a365", cases = outcomes, total = outcomes.Count, failed = 0, skipped = 0 }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Passed {outcomes.Count} independent predicate/reference boundary probes; unchanged Product; no source patch.");
```
