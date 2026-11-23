using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
namespace Coglatas.Tests.ProjectIde;
public sealed class CompilerHostImmutabilityTests
{
    private static CompilerHostProfile Profile(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
    }
    [Theory]
    [InlineData("capabilities")]
    [InlineData("contracts")]
    public void AFixedHostProfileKeepsItsAdmittedCompatibilityAfterCollectionAccess(string surface)
    {
        var input = Input(); var profile = Profile(input); Assert.True(profile.Supports(input));
        if(surface == "capabilities" && profile.Capabilities is System.Collections.ICollection capabilities && capabilities.SyncRoot is string[] list)
            Array.Fill(list, "example.changed");
        if(surface == "contracts" && profile.Contracts is System.Collections.ICollection contracts && contracts.SyncRoot is IDictionary<string,CompilerArtifactIdentity> map)
            try { map.Clear(); } catch (NotSupportedException) { }
        Assert.True(profile.Supports(input));
        Assert.Equal(6, profile.Contracts.Count);
    }
    [Fact]
    public void ARequestRetainsTheSameTypedSnapshotAuthorizationAsItsMaterializationDigest()
    {
        var input = Input(); var snapshot = new CompilerMaterializedSnapshot(ExternalSnapshotId.Parse(Id(60)), "1", new(Source(9), true, true, true));
        var request = new CompilerHostRequest(new(input), 1, [snapshot]);
        if(request.Snapshots is System.Collections.ICollection view && view.SyncRoot is CompilerMaterializedSnapshot[] backing)
            backing[0] = new(snapshot.SnapshotId, snapshot.Version, new(snapshot.Snapshot.Source, false, true, true));
        var rebuilt = new CompilerHostRequest(request.Input, request.ClientGeneration, request.Snapshots, request.Units, request.Calendars);
        Assert.Equal(request.Materializations, rebuilt.Materializations); Assert.Equal(request.ExecutionInputDigest, rebuilt.ExecutionInputDigest);
    }
    [Fact]
    public async Task AStaleHostResponseKeepsItsExplanationAfterCollectionAccess()
    {
        var input = Input(); var host = new LocalNetCompilerHost(Profile(input));
        var response = await host.CompileAsync(new(new(input),1));
        var assessment = CompilerHostFreshnessAssessment.Assess(response,input,2);
        Assert.Equal(CompilerFreshness.Stale,assessment.State);
        if(assessment.ChangedDimensions is System.Collections.ICollection view && view.SyncRoot is IList<string> backing)
            try { backing.Clear(); } catch (NotSupportedException) { }
        Assert.Equal("coglatas.client-generation",Assert.Single(assessment.ChangedDimensions));
    }

    [Fact]
    public async Task PublicProfileAccessCannotRemoveCompatibilityRejectionBeforeActualCompilation()
    {
        var input = Input(); var profile = Profile(input);
        if (profile.Contracts is System.Collections.ICollection view && view.SyncRoot is IDictionary<string,CompilerArtifactIdentity> map)
            try { map.Clear(); } catch (NotSupportedException) { }
        var settings = Settings(); settings["ruleset"]!["version"] = "2";
        var different = Input(settings);
        var response = await new LocalNetCompilerHost(profile).CompileAsync(new(new(different)));
        Assert.Equal(CompilerHostStatus.Unsupported, response.Status); Assert.Null(response.Compilation);
    }
}
