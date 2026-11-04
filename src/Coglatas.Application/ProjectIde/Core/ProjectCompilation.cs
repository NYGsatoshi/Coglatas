using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Core;

public static class ProjectCompilation
{
    public static CoreCompilation Compile(CompilerInputManifest input, CompilerArtifactIdentity expectedCompiler,
        IEnumerable<CompilerMaterializedSnapshot?>? snapshots = null, IEnumerable<UnitDefinition?>? units = null,
        IEnumerable<CalendarSnapshot?>? calendars = null, CancellationToken cancellationToken = default) =>
        PureCoreCompiler.Compile(input, expectedCompiler, snapshots, units, calendars, cancellationToken);
}
