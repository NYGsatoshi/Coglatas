using System.Collections;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;
namespace Coglatas.Tests.ProjectIde;
public sealed class DraftPlanImmutabilityTests
{
    [Fact]
    public void RenamePlanOriginsStayBoundToTheRetainedReviewBasis()
    {
        var input=DraftRenameTests.Draft();
        var preview=ProjectDraftRename.Preview(ProjectSemanticBinder.Bind(input),DraftRenameTests.Target(input),input.InputDigest,"New name",DraftRenameTests.Next(input));
        Assert.Equal(DraftRenameState.Ready,preview.State);
        var plan=Assert.IsType<ProjectDraftRenamePlan>(preview.Plan);Assert.Equal(3,plan.References.Count);
        var retained=SourceJson.Parse(plan.Data.Value.GetProperty("referenceOrigins").GetRawText());
        var before=plan.Before.Data;var after=plan.After.Data;
        if(plan.References is ICollection legacy && legacy.SyncRoot is CompilerBoundReference[] storage)
            storage[0]=storage[0] with{Origin=ProjectLocation.Create(plan.Target.DocumentId,CompilerContractFixtures.Source(99).Context)};
        Assert.Same(plan.After,plan.ApplyToDraft(input));Assert.Equal(before,plan.Before.Data);Assert.Equal(after,plan.After.Data);
        var origins=SourceJson.Parse(JsonSerializer.Serialize(plan.References.Select(item=>item.Origin.Data.Value)));
        Assert.Equal(retained,origins);
    }
}
