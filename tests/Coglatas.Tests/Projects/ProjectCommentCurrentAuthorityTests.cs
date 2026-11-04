using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Projects;

public sealed class ProjectCommentCurrentAuthorityTests
{
    [Theory]
    [InlineData("workspace-membership")]
    [InlineData("project-membership")]
    [InlineData("readonly")]
    [InlineData("viewer")]
    [InlineData("account")]
    [InlineData("workspace-archive")]
    public Task AuthorCannotMutateAfterCommittedAuthorityChangeDespiteWarmGraph(string change) =>
        VerifyWarmAuthorityChangeAsync(change);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task NativeCommentAuthorCannotUseRetainedTrackedAuthority()
    {
        foreach (var change in new[] { "workspace-membership", "project-membership", "readonly", "viewer", "account", "workspace-archive" })
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
                PostgreSqlTestEnvironment.RequireConnectionString(), database => VerifyWarmAuthorityChangeAsync(change, database));
        }
    }

    private static Task VerifyWarmAuthorityChangeAsync(string change, string? connectionString = null) =>
        ProjectServicePersistenceContext.WithAsync(async (fixture, _) =>
        {
            await fixture.AddTargetMemberAsync();
            fixture.Actor.UserId = fixture.TargetId;
            var created = await fixture.Service.AddCommentAsync(
                new CreateCommentRequest(CommentTargetType.Project, fixture.ProjectId, "Original"), fixture.Token);
            Assert.True(created.IsSuccess, created.Error);
            var commentId = created.Value!.Id;
            var workspaceMember = await fixture.Db.WorkspaceMembers.SingleAsync(row => row.UserId == fixture.TargetId, fixture.Token);
            var projectMember = await fixture.Db.ProjectMembers.SingleAsync(row => row.UserId == fixture.TargetId, fixture.Token);
            var actor = await fixture.Db.Users.SingleAsync(row => row.Id == fixture.TargetId, fixture.Token);
            var workspace = await fixture.Db.Workspaces.SingleAsync(fixture.Token);

            await using (var writer = fixture.NewContext())
            {
                switch (change)
                {
                    case "workspace-membership":
                        (await writer.WorkspaceMembers.SingleAsync(row => row.UserId == fixture.TargetId, fixture.Token)).Status = MembershipStatus.Suspended;
                        break;
                    case "project-membership":
                        writer.ProjectMembers.Remove(await writer.ProjectMembers.SingleAsync(row => row.UserId == fixture.TargetId, fixture.Token));
                        break;
                    case "readonly":
                        (await writer.WorkspaceMembers.SingleAsync(row => row.UserId == fixture.TargetId, fixture.Token)).Role = WorkspaceRole.ReadOnly;
                        break;
                    case "viewer":
                        (await writer.ProjectMembers.SingleAsync(row => row.UserId == fixture.TargetId, fixture.Token)).Role = ProjectRole.Viewer;
                        break;
                    case "account":
                        (await writer.Users.SingleAsync(row => row.Id == fixture.TargetId, fixture.Token)).Status = UserStatus.Suspended;
                        break;
                    case "workspace-archive":
                        (await writer.Workspaces.SingleAsync(fixture.Token)).Status = WorkspaceStatus.Archived;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown authority change.");
                }
                Assert.True(await writer.SaveChangesAsync(fixture.Token) > 0);
            }

            Assert.Equal(MembershipStatus.Active, workspaceMember.Status);
            Assert.Equal(WorkspaceRole.Member, workspaceMember.Role);
            Assert.Equal(ProjectRole.Contributor, projectMember.Role);
            Assert.Equal(UserStatus.Active, actor.Status);
            Assert.Equal(WorkspaceStatus.Active, workspace.Status);
            await AssertDeniedWithoutMutationAsync(fixture, commentId);
            Assert.Equal(QueryTrackingBehavior.TrackAll, fixture.Db.ChangeTracker.QueryTrackingBehavior);
            Assert.Same(workspaceMember, fixture.Db.Entry(workspaceMember).Entity);
            Assert.Equal(EntityState.Unchanged, fixture.Db.Entry(projectMember).State);
        }, connectionString: connectionString);

    [Theory]
    [InlineData("author")]
    [InlineData("target")]
    [InlineData("workspace")]
    [InlineData("deleted")]
    public Task StaleCommentIdentityCannotAuthorizeMutationEvenWithCurrentManagement(string changedField) =>
        ProjectServicePersistenceContext.WithAsync(async (fixture, _) =>
        {
            var created = await fixture.Service.AddCommentAsync(
                new CreateCommentRequest(CommentTargetType.Project, fixture.ProjectId, "Original"), fixture.Token);
            Assert.True(created.IsSuccess, created.Error);
            var commentId = created.Value!.Id;
            var tracked = fixture.Db.Comments.Local.Single(row => row.Id == commentId);
            await using (var writer = fixture.NewContext())
            {
                var current = await writer.Comments.SingleAsync(row => row.Id == commentId, fixture.Token);
                switch (changedField)
                {
                    case "author": current.AuthorUserId = fixture.TargetId; break;
                    case "target":
                        var parent = await writer.Projects.SingleAsync(fixture.Token);
                        var milestone = new Milestone { ProjectId = parent.Id, Name = "Current parent", DueDate = new DateOnly(2026, 11, 5) };
                        writer.Milestones.Add(milestone);
                        current.TargetType = CommentTargetType.Milestone;
                        current.TargetId = milestone.Id;
                        break;
                    case "workspace":
                        var workspace = new Workspace { Name = "New metadata", Slug = "new-metadata", CreatedByUserId = fixture.ActorId };
                        writer.Workspaces.Add(workspace);
                        current.WorkspaceId = workspace.Id;
                        break;
                    case "deleted": current.MarkDeleted(fixture.Clock.UtcNow); break;
                    default: throw new ArgumentOutOfRangeException(nameof(changedField), changedField, "Unknown identity change.");
                }
                await writer.SaveChangesAsync(fixture.Token);
            }

            Assert.Equal(fixture.ActorId, tracked.AuthorUserId);
            Assert.Equal(CommentTargetType.Project, tracked.TargetType);
            Assert.Equal(fixture.ProjectId, tracked.TargetId);
            Assert.Equal(fixture.WorkspaceId, tracked.WorkspaceId);
            Assert.Null(tracked.DeletedAt);
            Assert.True(await fixture.Authorization.CanManageProject(fixture.ActorId, fixture.ProjectId, fixture.Token));
            await AssertDeniedWithoutMutationAsync(fixture, commentId);
            Assert.Same(tracked, fixture.Db.Comments.Local.Single(row => row.Id == commentId));
        });

    [Fact]
    public Task DeletedMilestoneDeniesManagerMutationWhilePreservingLegacyListingAndCreation() =>
        ProjectServicePersistenceContext.WithAsync(async (fixture, _) =>
        {
            var parent = await fixture.Db.Projects.SingleAsync(fixture.Token);
            var milestone = new Milestone { ProjectId = parent.Id, Name = "Milestone", DueDate = new DateOnly(2026, 11, 5) };
            fixture.Db.Milestones.Add(milestone);
            await fixture.Db.SaveChangesAsync(fixture.Token);
            var request = new CreateCommentRequest(CommentTargetType.Milestone, milestone.Id, "Original");
            var created = await fixture.Service.AddCommentAsync(request, fixture.Token);
            Assert.True(created.IsSuccess, created.Error);
            var commentId = created.Value!.Id;
            milestone.MarkDeleted(fixture.Clock.UtcNow);
            await fixture.Db.SaveChangesAsync(fixture.Token);

            Assert.True(await fixture.Authorization.CanManageProject(fixture.ActorId, fixture.ProjectId, fixture.Token));
            await AssertDeniedWithoutMutationAsync(fixture, commentId);
            Assert.Single(await fixture.Db.Comments.AsNoTracking().ToListAsync(fixture.Token));
            Assert.Equal("CommentAdded", (await fixture.Db.AuditLogs.AsNoTracking().SingleAsync(fixture.Token)).Action);

            var listed = await fixture.Service.ListCommentsAsync(CommentTargetType.Milestone, milestone.Id,
                new ProjectChildListQuery(), fixture.Token);
            Assert.True(listed.IsSuccess, listed.Error);
            var added = await fixture.Service.AddCommentAsync(request, fixture.Token);
            Assert.True(added.IsSuccess, added.Error);
            Assert.NotEqual(commentId, added.Value!.Id);
        });

    [Theory]
    [InlineData(CommentTargetType.Project)]
    [InlineData(CommentTargetType.Milestone)]
    [InlineData(CommentTargetType.TaskItem)]
    [InlineData(CommentTargetType.Artifact)]
    public Task MissingOrUnsupportedStoredTargetDeniesAuthorMutation(CommentTargetType type) =>
        ProjectServicePersistenceContext.WithAsync(async (fixture, _) =>
        {
            var comment = new Comment { WorkspaceId = fixture.WorkspaceId, AuthorUserId = fixture.ActorId,
                TargetType = type, TargetId = Guid.NewGuid(), Body = "Orphan" };
            fixture.Db.Comments.Add(comment);
            await fixture.Db.SaveChangesAsync(fixture.Token);
            await AssertDeniedWithoutMutationAsync(fixture, comment.Id);
            Assert.Empty(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync(fixture.Token));
        });

    [Fact]
    public Task DeletedLegacyTaskTargetDeniesManagerMutation() =>
        ProjectServicePersistenceContext.WithAsync(async (fixture, _) =>
        {
            var parent = await fixture.Db.Projects.SingleAsync(fixture.Token);
            var task = new TaskItem { WorkspaceId = parent.WorkspaceId, ProjectId = parent.Id,
                Title = "Legacy Task", CreatedByUserId = fixture.ActorId, Kind = WorkItemKind.Task };
            fixture.Db.TaskItems.Add(task);
            await fixture.Db.SaveChangesAsync(fixture.Token);
            var created = await fixture.Service.AddCommentAsync(
                new CreateCommentRequest(CommentTargetType.TaskItem, task.Id, "Original"), fixture.Token);
            Assert.True(created.IsSuccess, created.Error);
            task.MarkDeleted(fixture.Clock.UtcNow);
            await fixture.Db.SaveChangesAsync(fixture.Token);

            Assert.True(await fixture.Authorization.CanManageProject(fixture.ActorId, fixture.ProjectId, fixture.Token));
            await AssertDeniedWithoutMutationAsync(fixture, created.Value!.Id);
        });

    [Fact]
    public Task CurrentManagerRetainsLegacyRepeatedDeleteBehavior() =>
        ProjectServicePersistenceContext.WithAsync(async (fixture, _) =>
        {
            var created = await fixture.Service.AddCommentAsync(
                new CreateCommentRequest(CommentTargetType.Project, fixture.ProjectId, "Original"), fixture.Token);
            Assert.True(created.IsSuccess, created.Error);
            var commentId = created.Value!.Id;

            Assert.True((await fixture.Service.DeleteCommentAsync(commentId, fixture.Token)).IsSuccess);
            Assert.True((await fixture.Service.DeleteCommentAsync(commentId, fixture.Token)).IsSuccess);

            Assert.Equal(fixture.Clock.UtcNow, (await fixture.Db.Comments.AsNoTracking().SingleAsync(fixture.Token)).DeletedAt);
            Assert.Equal(2, await fixture.Db.AuditLogs.CountAsync(row => row.Action == "CommentDeleted", fixture.Token));
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AuthorityReadFailureRestoresTrackingAndLeavesPendingChanges(bool cancel) =>
        ProjectServicePersistenceContext.WithAsync(async (fixture, cancellation) =>
        {
            var workspace = await fixture.Db.Workspaces.SingleAsync(fixture.Token);
            var originalName = workspace.Name;
            workspace.Name = "Caller pending";
            fixture.Db.Entry(workspace).State = EntityState.Modified;
            fixture.Db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTrackingWithIdentityResolution;
            var failure = new IOException("Synthetic authorization dependency failure");
            var repository = new ProjectRepository(fixture.Db);

            var read = repository.ReadCurrentAuthorizationAsync(async token =>
            {
                Assert.Equal(fixture.Token, token);
                Assert.Equal(QueryTrackingBehavior.NoTracking, fixture.Db.ChangeTracker.QueryTrackingBehavior);
                if (cancel)
                {
                    await cancellation.CancelAsync();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                throw failure;
            }, fixture.Token);

            if (cancel)
            {
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
                Assert.Equal(fixture.Token, error.CancellationToken);
            }
            else Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => read));

            Assert.Equal(QueryTrackingBehavior.NoTrackingWithIdentityResolution, fixture.Db.ChangeTracker.QueryTrackingBehavior);
            Assert.Equal(EntityState.Modified, fixture.Db.Entry(workspace).State);
            Assert.Same(workspace, fixture.Db.Workspaces.Local.Single());
            Assert.Equal(originalName, (await fixture.Db.Workspaces.AsNoTracking().SingleAsync(CancellationToken.None)).Name);
        });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task NativeAuthorizedCommentMutationRetainsCallerTransactionAndRollback() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(),
            database => ProjectServicePersistenceContext.WithAsync(async (fixture, _) =>
            {
                var created = await fixture.Service.AddCommentAsync(
                    new CreateCommentRequest(CommentTargetType.Project, fixture.ProjectId, "Original"), fixture.Token);
                Assert.True(created.IsSuccess, created.Error);
                var commentId = created.Value!.Id;
                var workspace = await fixture.Db.Workspaces.SingleAsync(fixture.Token);
                var originalWorkspaceName = workspace.Name;
                await using var caller = await fixture.Db.Database.BeginTransactionAsync(fixture.Token);
                workspace.Name = "Caller pending workspace";

                var updated = await fixture.Service.UpdateCommentAsync(commentId, new UpdateCommentRequest("  New body  "), fixture.Token);

                Assert.True(updated.IsSuccess, updated.Error);
                Assert.Equal("New body", updated.Value!.Body);
                Assert.Same(caller, fixture.Db.Database.CurrentTransaction);
                Assert.Same(workspace, fixture.Db.Workspaces.Local.Single());
                Assert.Equal(QueryTrackingBehavior.TrackAll, fixture.Db.ChangeTracker.QueryTrackingBehavior);
                Assert.Equal("Caller pending workspace", (await fixture.Db.Workspaces.AsNoTracking().SingleAsync(fixture.Token)).Name);
                Assert.Equal("New body", (await fixture.Db.Comments.AsNoTracking().SingleAsync(fixture.Token)).Body);
                Assert.Equal(2, await fixture.Db.AuditLogs.CountAsync(fixture.Token));
                await caller.RollbackAsync(fixture.Token);

                await using var verifier = fixture.NewContext();
                Assert.Equal(originalWorkspaceName, (await verifier.Workspaces.AsNoTracking().SingleAsync(fixture.Token)).Name);
                Assert.Equal("Original", (await verifier.Comments.AsNoTracking().SingleAsync(fixture.Token)).Body);
                Assert.Equal("CommentAdded", (await verifier.AuditLogs.AsNoTracking().SingleAsync(fixture.Token)).Action);
            }, connectionString: database));

    private static async Task AssertDeniedWithoutMutationAsync(ProjectServicePersistenceContext fixture, Guid commentId)
    {
        var before = await fixture.Db.Comments.AsNoTracking().SingleAsync(row => row.Id == commentId, fixture.Token);
        var audits = await fixture.Db.AuditLogs.CountAsync(fixture.Token);
        var updated = await fixture.Service.UpdateCommentAsync(commentId, new UpdateCommentRequest("Unauthorized"), fixture.Token);
        var deleted = await fixture.Service.DeleteCommentAsync(commentId, fixture.Token);
        Assert.False(updated.IsSuccess, updated.Error);
        Assert.Equal("You are not allowed to edit this comment.", updated.Error);
        Assert.False(deleted.IsSuccess, deleted.Error);
        Assert.Equal("You are not allowed to delete this comment.", deleted.Error);
        var after = await fixture.Db.Comments.AsNoTracking().SingleAsync(row => row.Id == commentId, fixture.Token);
        Assert.Equal((before.Body, before.DeletedAt, before.AuthorUserId, before.TargetType, before.TargetId, before.WorkspaceId),
            (after.Body, after.DeletedAt, after.AuthorUserId, after.TargetType, after.TargetId, after.WorkspaceId));
        Assert.Equal(audits, await fixture.Db.AuditLogs.CountAsync(fixture.Token));
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.Empty(fixture.AuthorizationChanges.Calls);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task NativeRepeatDeleteDoesNotTreatTimestampPrecisionAsIdentityDrift() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(),
            database => ProjectServicePersistenceContext.WithAsync(async (fixture, _) =>
            {
                var created = await fixture.Service.AddCommentAsync(
                    new CreateCommentRequest(CommentTargetType.Project, fixture.ProjectId, "Original"), fixture.Token);
                Assert.True(created.IsSuccess, created.Error);
                var comment = fixture.Db.Comments.Local.Single();
                var unrounded = fixture.Clock.UtcNow.AddTicks(7);
                comment.MarkDeleted(unrounded);
                await fixture.Db.SaveChangesAsync(fixture.Token);
                var persisted = await fixture.Db.Comments.AsNoTracking().SingleAsync(fixture.Token);
                Assert.Equal(unrounded, comment.DeletedAt);
                Assert.NotEqual(comment.DeletedAt, persisted.DeletedAt);
                Assert.NotNull(persisted.DeletedAt);

                var repeated = await fixture.Service.DeleteCommentAsync(comment.Id, fixture.Token);

                Assert.True(repeated.IsSuccess, repeated.Error);
                Assert.Equal(fixture.Clock.UtcNow, (await fixture.Db.Comments.AsNoTracking().SingleAsync(fixture.Token)).DeletedAt);
                Assert.Equal(1, await fixture.Db.AuditLogs.CountAsync(row => row.Action == "CommentDeleted", fixture.Token));
            }, connectionString: database));
}
