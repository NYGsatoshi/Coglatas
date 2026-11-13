using System.Text.Json;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
namespace Coglatas.Tests.ProjectIde;
[CollectionDefinition("Semantic contract mutation", DisableParallelization = true)]
public sealed class SemanticContractMutationCollection { }
[Collection("Semantic contract mutation")]
public sealed class SemanticContractImmutabilityTests
{
    private static SourceJson Encode(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static UnitDefinition Unit(string id)
    {
        var data=Encode(new{unitId=id,version=1,dimension=new Dictionary<string,int>{{"coglatas.length",1}}});
        return new(data,ContentDigest.Compute("coglatas.unit-definition/1",data.ToCanonicalBytes()));
    }
    private static UnitConversionDefinition Conversion(UnitDefinition from,UnitDefinition to,string factor)
    {
        object Identity(UnitDefinition unit)=>new{unitId=unit.UnitId,version=unit.Version,digest=unit.Digest.Data.Value};
        var data=Encode(new{conversionId="fixture.owned-conversion",version=1,kind="coglatas.multiplicative",sourceUnit=Identity(from),targetUnit=Identity(to),factor=new{numerator=factor,denominator="1"}});
        return new(data,ContentDigest.Compute("coglatas.unit-conversion/1",data.ToCanonicalBytes()),from,to);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedConversionProvenanceReplaysItsRetainedExactResult(bool replaceWithAnotherValidFactor)
    {
        var from=Unit("fixture.meter");var to=Unit("fixture.centimeter");var definition=Conversion(from,to,"100");
        var quantity=new Quantity(Encode(new{amount="2",unitId=from.UnitId,dimension=from.Dimension.Data.Value}));
        var result=ProjectUnitConversions.Convert(quantity,from,to,[definition]);Assert.Equal(NumericEvaluationStatus.Known,result.Status);
        Assert.Equal("200/1",result.Value!.Amount.ToString());
        if(result.Value.Provenance is System.Collections.ICollection legacy && legacy.SyncRoot is UnitConversionDefinition[] backing)
            backing[0]=replaceWithAnotherValidFactor?Conversion(from,to,"101"):null!;
        var replay=ProjectUnitConversions.Convert(quantity,from,to,result.Value.Provenance);
        Assert.Equal(result.Status,replay.Status);Assert.Equal(result.Value.Data,replay.Value!.Data);
    }
    [Fact]
    public void AccessToOneBuiltinDescriptorCannotChangeSameInputCompilationAcrossCallers()
    {
        var baseline=ReferenceCompletionTests.CompletionInput(2);var context=baseline.Source.Context;
        object Endpoint(int id)=>new{kind="coglatas.entity",expectedKind="coglatas.task",reference=new{tenantId=context.Branch.TenantId.ToString(),projectId=context.Branch.ProjectId.ToString(),entityId=Id(id),context=context.Data.Value}};
        var edge=new{relationId=Id(80),documentId=Id(4),kind=RelationKinds.DependsOn,schemaVersion=1,descriptor=new{descriptorId=RelationKinds.DependsOn,version=1},source=Endpoint(10),target=Endpoint(11),
            payload=new{mode="coglatas.finish-to-start",lag=new{type=new{kind="coglatas.elapsed-duration",schemaVersion=1,nullable=false,acceptsUnknown=false},state="coglatas.known",value="0"}}};
        var source=ProjectSource.Create(context,baseline.Source.Documents,baseline.Source.Declarations,Encode(new[]{edge}));
        var input=new CompilerInputManifest(source,baseline.Settings);var before=PureCoreCompiler.Compile(input,input.Compiler);
        Assert.Equal(CompilerVerdict.Pass,before.Result.Verdict);
        var descriptor=RelationKindDescriptor.Find(RelationKinds.DependsOn,1)!;var digest=descriptor.Digest;
        var backing=(descriptor.SourceKinds as System.Collections.ICollection)?.SyncRoot as string[];var original=backing?[0];
        try
        {
            if(backing is not null)backing[0]="coglatas.requirement";
            Assert.Equal(digest,ContentDigest.Compute("coglatas.relation-descriptor/1",descriptor.Data.ToCanonicalBytes()));
            Assert.Equal(before.Result.Data,PureCoreCompiler.Compile(input,input.Compiler).Result.Data);
        }
        finally { if(backing is not null)backing[0]=original!; }
    }

    [Theory]
    [InlineData("sourceKinds")]
    [InlineData("targetKinds")]
    [InlineData("allowedScopes")]
    [InlineData("requiredCapabilities")]
    [InlineData("invariants")]
    public void EveryDescriptorListRetainsItsCanonicalInterpretationAfterPublicAccess(string field)
    {
        var descriptor=RelationKindDescriptor.Find(RelationKinds.RelatedTo,1)!;
        var view=field switch {
            "sourceKinds"=>descriptor.SourceKinds,"targetKinds"=>descriptor.TargetKinds,
            "allowedScopes"=>descriptor.AllowedScopes,"requiredCapabilities"=>descriptor.RequiredCapabilities,
            "invariants"=>descriptor.Invariants,_=>throw new ArgumentOutOfRangeException(nameof(field)) };
        var expected=Encode(descriptor.Data.Value.GetProperty(field));
        var backing=(view as System.Collections.ICollection)?.SyncRoot as string[];var original=backing?[0];
        try
        {
            if(backing is not null)backing[0]="example.changed";
            Assert.Equal(expected,Encode(view));
            if(view is IList<string> mutable)Assert.Throws<NotSupportedException>(()=>mutable[0]="example.changed");
        }
        finally { if(backing is not null)backing[0]=original!; }
    }
    [Fact]
    public void PublicCatalogStorageCannotReplaceAnotherCallersBuiltinInterpretation()
    {
        var expected=RelationKindDescriptor.BuiltIns.ToArray();
        var backing=(RelationKindDescriptor.BuiltIns as System.Collections.ICollection)?.SyncRoot as RelationKindDescriptor[];
        try
        {
            if(backing is not null)backing[0]=expected[1];
            Assert.Equal(expected,RelationKindDescriptor.BuiltIns);
            foreach(var descriptor in expected)Assert.Same(descriptor,RelationKindDescriptor.Find(descriptor.DescriptorId,1));
        }
        finally { if(backing is not null)Array.Copy(expected,backing,expected.Length); }
    }
    [Fact]
    public void EmptyIdentityConversionRetainsItsCanonicalResultAndRejectsGenericMutation()
    {
        var unit=Unit("fixture.meter");var quantity=new Quantity(Encode(new{amount="2",unitId=unit.UnitId,dimension=unit.Dimension.Data.Value}));
        var result=ProjectUnitConversions.Convert(quantity,unit,unit,[]);
        Assert.Equal(NumericEvaluationStatus.Known,result.Status);Assert.Empty(result.Value!.Provenance);
        Assert.Throws<NotSupportedException>(()=>((IList<UnitConversionDefinition>)result.Value.Provenance).Clear());
        Assert.Equal(result.Value.Data,ProjectUnitConversions.Convert(quantity,unit,unit,result.Value.Provenance).Value!.Data);
    }
    [Fact]
    public void MutatingTheCallersConversionPathCannotChangeTheRetainedProvenance()
    {
        var from=Unit("fixture.meter");var to=Unit("fixture.centimeter");var definition=Conversion(from,to,"100");
        var path=new[]{definition};var quantity=new Quantity(Encode(new{amount="2",unitId=from.UnitId,dimension=from.Dimension.Data.Value}));
        var result=ProjectUnitConversions.Convert(quantity,from,to,path);Assert.Equal(NumericEvaluationStatus.Known,result.Status);
        path[0]=Conversion(from,to,"101");
        Assert.Same(definition,Assert.Single(result.Value!.Provenance));
        Assert.Equal(result.Value.Data,ProjectUnitConversions.Convert(quantity,from,to,result.Value.Provenance).Value!.Data);
    }
}
