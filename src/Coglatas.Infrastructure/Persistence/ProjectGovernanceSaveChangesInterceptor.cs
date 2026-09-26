using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// Persistence guard for WPC-02A Project governance state. Application commands
/// remain responsible for their lifecycle policy; this interceptor guarantees
/// that an accepted status mutation cannot be persisted without exact suspend /
/// archive provenance and coherent activation metadata. It also closes alternate
/// Task/Milestone command paths that attempt to persist operational mutations
/// outside an activated, writable Project lifecycle state.
/// </summary>
public sealed class ProjectGovernanceSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> BrowserSmokeFixtureProjectSlugs = new(StringComparer.Ordinal)
    {
        "browser-smoke-project",
        "browser-smoke-pr04-second-project",
        "browser-smoke-pr05-kanban",
        "browser-smoke-pr06-gantt",
        "browser-smoke-pr07-notifications"
    };

    private readonly bool _browserSmokeFixtureSeedEnabled;

    public ProjectGovernanceSaveChangesInterceptor()
    {
    }

    public ProjectGovernanceSaveChangesInterceptor(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var environmentName =
            configuration["ASPNETCORE_ENVIRONMENT"] ??
            configuration["DOTNET_ENVIRONMENT"];
        _browserSmokeFixtureSeedEnabled =
            string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase) &&
            (IsEnabled(configuration["BrowserSmokeSeed:Enabled"]) ||
             IsEnabled(configuration["COGLATAS_BROWSER_SMOKE_SEED_ENABLED"]));
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        ApplyBrowserSmokeFixtureActivationProvenance(context);

        foreach (var entry in context.ChangeTracker.Entries<Project>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            var project = entry.Entity;
            if (entry.State == EntityState.Modified && entry.Property(item => item.Status).IsModified)
            {
                var previous = entry.Property(item => item.Status).OriginalValue;
                var current = project.Status;
                if (previous != current)
                {
                    // Recovery never guesses. Leaving Archived/Suspended for an
                    // ordinary lifecycle state must return to the exact status
                    // recorded when that recovery layer was entered. Transition
                    // to Deleted is terminal mutation, not restore/resume, and is
                    // therefore left to the owning deletion command/policy.
                    if (previous == ProjectStatus.Archived && current != ProjectStatus.Deleted)
                    {
                        ValidateArchivedRestore(project, current);
                        project.ArchivedFromStatus = null;

                        if (current != ProjectStatus.Suspended)
                        {
                            project.SuspendedFromStatus = null;
                        }
                    }
                    else if (previous == ProjectStatus.Suspended &&
                             current is not ProjectStatus.Archived and not ProjectStatus.Deleted)
                    {
                        ValidateSuspendedResume(project, current);
                        project.SuspendedFromStatus = null;
                    }

                    if (current == ProjectStatus.Suspended)
                    {
                        if (previous == ProjectStatus.Archived)
                        {
                            // Archived -> Suspended restores the outer archive
                            // layer. The original pre-suspension state must have
                            // survived the archive so the subsequent resume is
                            // still exact and fail-closed.
                            ValidateStoredRecoveryState(project, project.SuspendedFromStatus, "suspension");
                        }
                        else
                        {
                            project.SuspendedFromStatus = previous;
                        }
                    }

                    if (current == ProjectStatus.Archived)
                    {
                        project.ArchivedFromStatus = previous;
                    }

                    // First activation is owned by the explicit activation
                    // command. A Planning -> Active write is valid only when
                    // that same unit of work has established canonical Activated
                    // provenance; generic update remains blocked in the service.
                    if (previous == ProjectStatus.Planning &&
                        current == ProjectStatus.Active &&
                        !HasActivatedProvenance(project))
                    {
                        throw new InvalidOperationException(
                            "Planning to Active requires canonical activation provenance.");
                    }
                }
            }

            ValidateActivationProvenance(project);
        }

        ValidateOperationalChildMutations(context);
    }

    /// <summary>
    /// Browser-smoke fixtures are synthetic data created only inside the explicit
    /// Test-environment seed boundary. They predate the canonical activation
    /// command and are created together with their Task/Gantt fixture graph.
    /// Normalize only newly-added, reserved fixture Projects so the fixture obeys
    /// the same persistence invariant. Existing Projects, arbitrary slugs, and all
    /// non-Test runtime paths remain untouched and fail closed normally.
    /// </summary>
    private void ApplyBrowserSmokeFixtureActivationProvenance(DbContext context)
    {
        if (!_browserSmokeFixtureSeedEnabled)
        {
            return;
        }

        var activatedAtUtc = DateTimeOffset.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries<Project>()
                     .Where(entry => entry.State == EntityState.Added &&
                                     BrowserSmokeFixtureProjectSlugs.Contains(entry.Entity.Slug)))
        {
            var project = entry.Entity;
            if (project.Status != ProjectStatus.Active ||
                project.ActivationState != ProjectActivationState.NeverActivated ||
                project.ActivatedAtUtc.HasValue ||
                project.ActivationVersion.HasValue)
            {
                throw new InvalidOperationException(
                    "Browser-smoke fixture Project activation seed state is inconsistent.");
            }

            project.ActivationState = ProjectActivationState.Activated;
            project.ActivatedAtUtc = activatedAtUtc;
            project.ActivationVersion = 1;
        }
    }

    /// <summary>
    /// Task and Milestone commands have several adapters, including the Gantt
    /// compatibility endpoints. Every accepted write must therefore converge on
    /// the same persistence invariant instead of relying on one controller or
    /// service branch. The Project must already be tracked because a current
    /// authorization/lifecycle decision is required in the same unit of work.
    /// </summary>
    private static void ValidateOperationalChildMutations(DbContext context)
    {
        var affectedProjectIds = context.ChangeTracker.Entries<TaskItem>()
            .Where(entry => IsMutation(entry.State))
            .Select(entry => entry.Entity.ProjectId)
            .Concat(context.ChangeTracker.Entries<Milestone>()
                .Where(entry => IsMutation(entry.State))
                .Select(entry => entry.Entity.ProjectId))
            .Where(projectId => projectId != Guid.Empty)
            .Distinct()
            .ToArray();

        if (affectedProjectIds.Length == 0)
        {
            return;
        }

        var trackedProjects = context.ChangeTracker.Entries<Project>()
            .Where(entry => entry.State != EntityState.Detached)
            .Select(entry => entry.Entity)
            .GroupBy(project => project.Id)
            .ToDictionary(group => group.Key, group => group.Last());

        foreach (var projectId in affectedProjectIds)
        {
            if (!trackedProjects.TryGetValue(projectId, out var project))
            {
                throw new InvalidOperationException(
                    "Task and Milestone mutations require the current Project governance state in the same unit of work.");
            }

            if (project.DeletedAt.HasValue ||
                project.ActivationState != ProjectActivationState.Activated ||
                !HasActivatedProvenance(project) ||
                project.Status is not (ProjectStatus.Active or ProjectStatus.Review))
            {
                throw new InvalidOperationException(
                    "Task and Milestone mutations require an activated Project in Active or Review status.");
            }
        }
    }

    private static bool IsMutation(EntityState state) =>
        state is EntityState.Added or EntityState.Modified or EntityState.Deleted;

    private static bool IsEnabled(string? value) =>
        bool.TryParse(value, out var enabled) && enabled;

    private static void ValidateArchivedRestore(Project project, ProjectStatus requestedStatus)
    {
        if (project.ActivationState == ProjectActivationState.LegacyUnknown)
        {
            throw new InvalidOperationException(
                "LegacyUnknown Project provenance cannot be restored implicitly.");
        }

        if (!project.ArchivedFromStatus.HasValue || project.ArchivedFromStatus.Value != requestedStatus)
        {
            throw new InvalidOperationException(
                "Archived Project restoration must return to its exact recorded lifecycle state.");
        }

        if (requestedStatus == ProjectStatus.Suspended)
        {
            ValidateStoredRecoveryState(project, project.SuspendedFromStatus, "suspension");
            return;
        }

        ValidateRecoveryState(project, requestedStatus);
    }

    private static void ValidateSuspendedResume(Project project, ProjectStatus requestedStatus)
    {
        if (project.ActivationState == ProjectActivationState.LegacyUnknown)
        {
            throw new InvalidOperationException(
                "LegacyUnknown Project provenance cannot be resumed implicitly.");
        }

        if (!project.SuspendedFromStatus.HasValue || project.SuspendedFromStatus.Value != requestedStatus)
        {
            throw new InvalidOperationException(
                "Suspended Project recovery must return to its exact recorded lifecycle state.");
        }

        ValidateRecoveryState(project, requestedStatus);
    }

    private static void ValidateStoredRecoveryState(
        Project project,
        ProjectStatus? storedStatus,
        string provenanceKind)
    {
        if (!storedStatus.HasValue)
        {
            throw new InvalidOperationException(
                $"Project {provenanceKind} provenance is unavailable.");
        }

        ValidateRecoveryState(project, storedStatus.Value);
    }

    private static void ValidateRecoveryState(Project project, ProjectStatus status)
    {
        switch (status)
        {
            case ProjectStatus.Planning
                when project.ActivationState == ProjectActivationState.NeverActivated &&
                     !project.ActivatedAtUtc.HasValue &&
                     !project.ActivationVersion.HasValue:
                return;

            case ProjectStatus.Active or ProjectStatus.Review or ProjectStatus.Completed
                when HasActivatedProvenance(project):
                return;

            default:
                throw new InvalidOperationException(
                    "Project lifecycle recovery provenance is missing or inconsistent with activation state.");
        }
    }

    private static bool HasActivatedProvenance(Project project) =>
        project.ActivationState == ProjectActivationState.Activated &&
        project.ActivatedAtUtc.HasValue &&
        project.ActivationVersion is > 0;

    private static void ValidateActivationProvenance(Project project)
    {
        switch (project.ActivationState)
        {
            case ProjectActivationState.Activated when !HasActivatedProvenance(project):
                throw new InvalidOperationException(
                    "Activated Projects require ActivatedAtUtc and a positive ActivationVersion.");

            case ProjectActivationState.NeverActivated
                when project.ActivatedAtUtc.HasValue || project.ActivationVersion.HasValue:
                throw new InvalidOperationException(
                    "NeverActivated Projects cannot carry activation timestamp/version metadata.");

            case ProjectActivationState.LegacyUnknown
                when project.ActivatedAtUtc.HasValue || project.ActivationVersion.HasValue:
                throw new InvalidOperationException(
                    "LegacyUnknown Projects cannot fabricate canonical activation metadata.");
        }
    }
}
