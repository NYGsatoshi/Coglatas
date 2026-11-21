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
}
