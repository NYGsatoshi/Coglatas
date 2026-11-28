using System.Text;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectTestRetainedResultTests
{
    [Theory]
    [InlineData("noncanonical-bytes")]
    [InlineData("source")]
    [InlineData("input")]
    [InlineData("plan")]
    [InlineData("outcome")]
    [InlineData("result-digest")]
    public void Original_bytes_and_existing_source_input_plan_and_result_pins_must_agree(string mismatch)
    {
        var input = ProjectTestRunnerTests.Fixture();
        var evaluator = new CompilerArtifactIdentity(SourceJson.Parse(input.Settings.Value.GetProperty("evaluator").GetRawText()));
        var run = ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), evaluator).Execute(input.InputDigest);
        var binding = new ProjectTestJobBinding(Guid.NewGuid(), SecuritySourceIdentitySnapshot.Capture(input.Source.Context, input.InputDigest),
            input.Source.Digest.Value, input.InputDigest.Value, input.Source.Digest.Value);
        var summary = new ProjectTestResultSummary(run.Execution, run.Coverage, run.Verdict, run.Freshness,
            run.EvidenceKind, run.DiscoveredTests, run.ExecutedAssertions, run.Data.Value.GetProperty("planDigest").GetProperty("value").GetString()!,
            run.Digest.Value);
        var bytes = run.Data.ToCanonicalBytes();
        var retained = ProjectTestRetainedResult.ReadStored(bytes, binding, summary);
        Assert.Equal(ProjectTestRetainedResultState.Available, retained.State);
        Assert.Equal(bytes, retained.RecoverCanonicalBytes());
        var recovered = Assert.IsType<byte[]>(retained.RecoverCanonicalBytes());
        recovered[0] ^= 1;
        Assert.Equal(bytes, retained.RecoverCanonicalBytes());

        if (mismatch == "noncanonical-bytes") bytes = Encoding.UTF8.GetBytes(run.Data.CanonicalText + "\n");
        if (mismatch == "source") binding = binding with { SourceDigest = input.InputDigest.Value };
        if (mismatch == "input") binding = binding with { Source = binding.Source with { InputDigest = input.Source.Digest.Value } };
        if (mismatch == "plan") summary = summary with { PlanDigest = input.Source.Digest.Value };
        if (mismatch == "outcome") summary = summary with { Verdict = CompilerVerdict.Fail };
        if (mismatch == "result-digest") summary = summary with { ResultDigest = input.Source.Digest.Value };

        var refused = ProjectTestRetainedResult.ReadStored(bytes, binding, summary);
        Assert.Equal(ProjectTestRetainedResultState.Mismatch, refused.State);
        Assert.Null(refused.RecoverCanonicalBytes());
        Assert.Null(refused.Digest);
        Assert.Equal(ProjectTestRetainedResultState.Unavailable, ProjectTestRetainedResult.ReadStored(null, binding, summary).State);
    }
}
