using System.Collections;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
using static Coglatas.Tests.ProjectIde.CompilerAnalysisFixtures;
namespace Coglatas.Tests.ProjectIde;
public sealed class ValidationSnapshotImmutabilityTests
{
    [Fact]
    public void RelationValidityCannotChangeAfterItsResultsAreRead()
    {
        var original=Source();var context=original.Context;
        var task=Obj(new{entityId=Id(10),documentId=Id(4),kind="coglatas.task",schemaVersion=1,payload=new{duration=new{type=new{kind="coglatas.elapsed-duration",schemaVersion=1,nullable=false,acceptsUnknown=false},state="coglatas.known",value="1"},ports=Array.Empty<object>()}});
        var endpoint=new{kind="coglatas.entity",expectedKind="coglatas.task",reference=Reference(10,context)};
        var edge=Obj(new{relationId=Id(80),documentId=Id(4),kind=RelationKinds.DependsOn,schemaVersion=1,descriptor=new{descriptorId=RelationKinds.DependsOn,version=1},source=endpoint,target=endpoint,payload=new{mode="coglatas.finish-to-start",lag=new{type=new{kind="coglatas.elapsed-duration",schemaVersion=1,nullable=false,acceptsUnknown=false},state="coglatas.known",value="0"}}});
        var source=ProjectSource.Create(context,original.Documents,Json(new JsonArray(task)),Json(new JsonArray(edge)));
        var result=RelationGraphValidator.Validate(new(source,true,true,true));Assert.True(result.HasInvalid);
        if(result.Findings is ICollection legacy && legacy.SyncRoot is RelationFinding[] storage)
            for(var i=0;i<storage.Length;i++)storage[i]=storage[i] with{State=RelationFindingState.Unknown};
        Assert.True(result.HasInvalid);
    }
    [Fact]
    public void RetainedRequirementClaimsReproduceTheirFreshness()
    {
        var original=Source();var source=ProjectSource.Create(original.Context,original.Documents,Json(new JsonArray(RequirementDeclaration(original.Context))));
        var input=Input(AllRulesSettings(),source);var location=ProjectLocation.Create(DocumentId.Parse(Id(4)),source.Context,entityId:EntityId.Parse(Id(8)));
        var target=new PlanEntityReference(Json(Reference(6,source.Context)));var claim=new RequirementEvidenceClaim(location,target,Pass(input));
        var result=RequirementEvidenceValidator.Assess(input,location,[claim]);Assert.Equal(CompilerFreshness.Current,result.Freshness);
        if(result.Claims is ICollection legacy && legacy.SyncRoot is RequirementEvidenceClaim[] storage)
            storage[0]=new(location,target,Pass(Input(AllRulesSettings(),Source(9))));
        var replay=RequirementEvidenceValidator.Assess(input,location,result.Claims);
        Assert.Equal(result.Freshness,replay.Freshness);
        Assert.Equal(result.Findings.Select(item => (item.State, item.Diagnostic.Data.CanonicalText)),
            replay.Findings.Select(item => (item.State, item.Diagnostic.Data.CanonicalText)));
        Assert.Equal(CompilerVerdict.Inconclusive,replay.Outcome.Verdict);
    }
    [Fact]
    public void FullCompilationKeepsItsValidationFindingState()
    {
        var settings=Settings();settings["semantics"]!["version"]="2";
        var input=Input(settings);var compilation=PureCoreCompiler.Compile(input,input.Compiler);
        var validation=Assert.IsType<CompilerSemanticValidation>(compilation.Validation);var expected=Assert.Single(validation.Findings);
        var data=compilation.Result.Data;
        if(validation.Findings is ICollection legacy && legacy.SyncRoot is CompilerBindingFinding[] storage)
            storage[0]=expected with{State=RelationFindingState.Invalid};
        Assert.Equal(expected,Assert.Single(validation.Findings));Assert.Equal(data,compilation.Result.Data);
    }
}
