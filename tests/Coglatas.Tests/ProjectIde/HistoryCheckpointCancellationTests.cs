using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class HistoryCheckpointCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_when_the_parent_supplier_finishes_prevents_checkpoint_publication(bool materialized)
    {
        var source = Source(50);
        var parent = Source(49).Context.CommittedRevision!;
        using var cancellation = new CancellationTokenSource();

        var exception = Assert.Throws<OperationCanceledException>(() => materialized
            ? ProjectHistoryRevision.MaterializedCheckpoint(source.Context.CommittedRevision!, 1, source.Digest,
                source.Data, Parents(), cancellation.Token)
            : ProjectHistoryRevision.Checkpoint(source, 1, Parents(), cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);

        IEnumerable<RevisionRef> Parents()
        {
            yield return parent;
            cancellation.Cancel();
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void RejectedCheckpointParentDisposalPreservesCancellation(bool materialized, bool nullParent, bool cancelOnDispose)
    {
        var source = Source(50);
        var parent = Source(49).Context.CommittedRevision!;
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var requiredCount = nullParent ? 1 : 9;
        var observed = 0;
        var disposed = false;
        Action cancel = cancellation.Cancel;

        if (cancelOnDispose)
        {
            var exception = Assert.Throws<OperationCanceledException>(Read);
            Assert.Equal(token, exception.CancellationToken);
        }
        else
        {
            var exception = Assert.Throws<ArgumentException>(Read);
            Assert.Equal("parents", exception.ParamName);
        }
        Assert.True(disposed);
        Assert.Equal(requiredCount, observed);

        ProjectHistoryRevision Read() => materialized
            ? ProjectHistoryRevision.MaterializedCheckpoint(source.Context.CommittedRevision!, 1, source.Digest,
                source.Data, Parents(cancel), token)
            : ProjectHistoryRevision.Checkpoint(source, 1, Parents(cancel), token);

        IEnumerable<RevisionRef> Parents(Action requestCancellation)
        {
            try
            {
                for (var index = 0; index < requiredCount; index++)
                {
                    observed++;
                    yield return nullParent ? null! : parent;
                }
                throw new InvalidOperationException("Parent enumeration continued beyond refusal.");
            }
            finally
            {
                disposed = true;
                if (cancelOnDispose) requestCancellation();
            }
        }
    }

    [Theory]
    [InlineData(HistoryReadState.Corrupt)]
    [InlineData(HistoryReadState.OutOfScope)]
    [InlineData(HistoryReadState.ResourceLimit)]
    public void CancellationDuringDisposalOfRejectedHistorySupplyReachesTheCaller(HistoryReadState expectedState)
    {
        var source = Source(50);
        var target = source.Context.CommittedRevision!;
        var baseline = ProjectSourceHistory.Read(target, Supply(static () => { }));
        Assert.Equal(expectedState, baseline.State);
        Assert.Null(baseline.Source);
        Assert.Empty(baseline.ReconstructionPath);

        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        Action cancel = cancellation.Cancel;
        var error = Assert.Throws<OperationCanceledException>(() => ProjectSourceHistory.Read(target, Supply(cancel), token));
        Assert.Equal(token, error.CancellationToken);

        IEnumerable<ProjectHistoryRevision?> Supply(Action finish)
        {
            try
            {
                if (expectedState == HistoryReadState.Corrupt)
                {
                    yield return null;
                    yield break;
                }

                if (expectedState == HistoryReadState.OutOfScope)
                {
                    var foreign = new RevisionRef(new(TenantId.Parse(Id(1)), ProjectId.Parse(Id(99)), BranchId.Parse(Id(3))),
                        RevisionId.Parse(Id(50)));
                    yield return ProjectHistoryRevision.Unavailable(foreign, 0, source.Digest);
                    yield break;
                }

                for (var index = 0; index <= ProjectSourceHistory.MaximumRecords; index++)
                {
                    var revision = new RevisionRef(target.Branch, RevisionId.Parse(Id(index + 200)));
                    yield return ProjectHistoryRevision.Unavailable(revision, index, source.Digest);
                }
            }
            finally
            {
                finish();
            }
        }
    }
}
