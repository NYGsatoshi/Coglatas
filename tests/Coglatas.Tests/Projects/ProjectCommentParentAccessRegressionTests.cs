using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Coglatas.Tests.Projects;

public sealed class ProjectCommentParentAccessRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(CommentTargetType.Project, "edit", "workspace")]
    [InlineData(CommentTargetType.Project, "delete", "workspace")]
    [InlineData(CommentTargetType.Project, "edit", "project")]
    [InlineData(CommentTargetType.Project, "delete", "project")]
    [InlineData(CommentTargetType.Milestone, "edit", "workspace")]
    [InlineData(CommentTargetType.Milestone, "delete", "workspace")]
    [InlineData(CommentTargetType.Milestone, "edit", "project")]
    [InlineData(CommentTargetType.Milestone, "delete", "project")]
    public async Task CommentAuthorshipDoesNotOutliveCurrentParentAccess(
        CommentTargetType targetType, string operation, string revokedScope)
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        await fixture.AddTargetMemberAsync();
        var targetId = await SeedTargetAsync(fixture, targetType);
        fixture.Actor.UserId = fixture.TargetId;
        Assert.True(await fixture.Authorization.CanCommentOnTarget(fixture.TargetId, targetType, targetId, fixture.Token));
        var created = await fixture.Service.AddCommentAsync(new CreateCommentRequest(targetType, targetId, "Original comment"), fixture.Token);
        Assert.True(created.IsSuccess, created.Error);
        var commentId = created.Value!.Id;
        if (operation == "edit")
        {
            var legitimate = await fixture.Service.UpdateCommentAsync(commentId, new UpdateCommentRequest("Authorized edit"), fixture.Token);
            Assert.True(legitimate.IsSuccess, legitimate.Error);
        }
        else
        {
            var control = await fixture.Service.AddCommentAsync(new CreateCommentRequest(targetType, targetId, "Deletion positive control"), fixture.Token);
            Assert.True(control.IsSuccess, control.Error);
            var legitimate = await fixture.Service.DeleteCommentAsync(control.Value!.Id, fixture.Token);
            Assert.True(legitimate.IsSuccess, legitimate.Error);
            Assert.NotNull((await fixture.Db.Comments.AsNoTracking().SingleAsync(comment => comment.Id == control.Value.Id)).DeletedAt);
        }
        if (revokedScope == "workspace")
        {
            var membership = await fixture.Db.WorkspaceMembers.SingleAsync(member => member.UserId == fixture.TargetId);
            membership.Status = MembershipStatus.Suspended;
        }
        else
        {
            fixture.Db.ProjectMembers.Remove(await fixture.Db.ProjectMembers.SingleAsync(member => member.UserId == fixture.TargetId));
        }
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.False(await fixture.Authorization.CanViewProject(fixture.TargetId, fixture.ProjectId, fixture.Token));
        Assert.False(await fixture.Authorization.CanManageProject(fixture.TargetId, fixture.ProjectId, fixture.Token));
        Assert.False(await fixture.Authorization.CanCommentOnTarget(fixture.TargetId, targetType, targetId, fixture.Token));
        Assert.Equal(TenantUserStatus.Active, (await fixture.Db.TenantUsers.AsNoTracking().SingleAsync(member => member.UserId == fixture.TargetId)).Status);
        var before = await fixture.Db.Comments.AsNoTracking().SingleAsync(comment => comment.Id == commentId);
        var auditCount = await fixture.Db.AuditLogs.AsNoTracking().CountAsync();
        var projectStages = fixture.Invalidations.Calls.Count;
        var authorizationStages = fixture.AuthorizationChanges.Calls.Count;

        var result = await MutateCommentAsync(fixture, commentId, operation);

        var after = await fixture.Db.Comments.AsNoTracking().SingleAsync(comment => comment.Id == commentId);
        output.WriteLine($"Target={targetType}; operation={operation}; lost={revokedScope}; " +
            $"success={result.Success}; body={after.Body}; deleted={after.DeletedAt.HasValue}; " +
            $"audit delta={await fixture.Db.AuditLogs.AsNoTracking().CountAsync() - auditCount}");
        Assert.False(result.Success, result.Error);
        Assert.Equal(before.Body, after.Body);
        Assert.Equal(before.DeletedAt, after.DeletedAt);
        Assert.Equal(auditCount, await fixture.Db.AuditLogs.AsNoTracking().CountAsync());
        Assert.Equal(projectStages, fixture.Invalidations.Calls.Count);
        Assert.Equal(authorizationStages, fixture.AuthorizationChanges.Calls.Count);
    }

    [Theory]
    [InlineData(ProjectStatus.Active, false)]
    [InlineData(ProjectStatus.Active, true)]
    [InlineData(ProjectStatus.Planning, false)]
    [InlineData(ProjectStatus.Planning, true)]
    [InlineData(ProjectStatus.Suspended, false)]
    [InlineData(ProjectStatus.Suspended, true)]
    [InlineData(ProjectStatus.Archived, false)]
    [InlineData(ProjectStatus.Archived, true)]
    public async Task CurrentExplicitManagerRetainsExistingCommentManagementGrants(ProjectStatus status, bool isAuthor)
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        var projectId = fixture.ProjectId;
        if (status == ProjectStatus.Planning)
        {
            var planning = new Project
            {
                TenantId = fixture.TenantId, WorkspaceId = fixture.WorkspaceId, Name = "Planning", Slug = "planning",
                OwnerUserId = fixture.ActorId, CreatedByUserId = fixture.ActorId, Status = ProjectStatus.Planning,
                Visibility = ProjectVisibility.MembersOnly, ActivationState = ProjectActivationState.NeverActivated, VersionNo = 1
            };
            fixture.Db.Projects.Add(planning);
            fixture.Db.ProjectMembers.Add(new ProjectMember { ProjectId = planning.Id, UserId = fixture.ActorId, Role = ProjectRole.Owner, JoinedAt = fixture.Clock.UtcNow });
            projectId = planning.Id;
        }
        else if (status != ProjectStatus.Active)
        {
            var project = await fixture.Db.Projects.SingleAsync();
            project.Status = status;
        }
        // Historical generic Comments can remain present in non-operational
        // Projects. This preserves management grants, not creation authority.
        var comment = new Comment
        {
            WorkspaceId = fixture.WorkspaceId, AuthorUserId = isAuthor ? fixture.ActorId : fixture.TargetId,
            TargetType = CommentTargetType.Project, TargetId = projectId, Body = "Historical comment"
        };
        fixture.Db.Comments.Add(comment);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.True(await fixture.Authorization.CanManageProject(fixture.ActorId, projectId, fixture.Token));
        if (status != ProjectStatus.Active)
            Assert.False(await fixture.Authorization.CanCommentOnTarget(fixture.ActorId, CommentTargetType.Project, projectId, fixture.Token));

        var updated = await fixture.Service.UpdateCommentAsync(comment.Id, new UpdateCommentRequest("Managed edit"), fixture.Token);
        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Equal("Managed edit", (await fixture.Db.Comments.AsNoTracking().SingleAsync(item => item.Id == comment.Id)).Body);
        var deleted = await fixture.Service.DeleteCommentAsync(comment.Id, fixture.Token);

        Assert.True(deleted.IsSuccess, deleted.Error);
        var persisted = await fixture.Db.Comments.AsNoTracking().SingleAsync(item => item.Id == comment.Id);
        Assert.Equal(isAuthor ? fixture.ActorId : fixture.TargetId, persisted.AuthorUserId);
        Assert.Equal(fixture.Clock.UtcNow, persisted.DeletedAt);
        Assert.Equal(status, (await fixture.Db.Projects.AsNoTracking().SingleAsync(item => item.Id == projectId)).Status);
        var audits = await fixture.Db.AuditLogs.AsNoTracking().ToListAsync();
        Assert.Equal(new[] { "CommentDeleted", "CommentEdited" }, audits.Select(audit => audit.Action).Order().ToArray());
        Assert.All(audits, audit =>
        {
            Assert.Equal(fixture.ActorId, audit.ActorUserId);
            Assert.Equal(fixture.TenantId, audit.TenantId);
            Assert.Equal("Comment", audit.EntityType);
            Assert.Equal(comment.Id, audit.EntityId);
        });
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.Empty(fixture.AuthorizationChanges.Calls);
    }

    [Theory]
    [InlineData(CommentTargetType.Project)]
    [InlineData(CommentTargetType.Milestone)]
    public async Task IndependentProjectManagerMembershipSurvivesGroupMembershipRemoval(CommentTargetType targetType)
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync(grouped: true);
        var targetId = await SeedTargetAsync(fixture, targetType);
        var comment = new Comment
        {
            WorkspaceId = fixture.WorkspaceId, AuthorUserId = fixture.TargetId,
            TargetType = targetType, TargetId = targetId, Body = "Group history"
        };
        fixture.Db.Comments.Add(comment);
        fixture.Db.GroupMembers.Remove(await fixture.Db.GroupMembers.SingleAsync(member => member.UserId == fixture.ActorId));
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.GroupMembers.AsNoTracking().ToListAsync());
        Assert.True(await fixture.Authorization.CanManageProject(fixture.ActorId, fixture.ProjectId, fixture.Token));

        var updated = await fixture.Service.UpdateCommentAsync(comment.Id, new UpdateCommentRequest("Managed after Group removal"), fixture.Token);
        var deleted = await fixture.Service.DeleteCommentAsync(comment.Id, fixture.Token);

        Assert.True(updated.IsSuccess, updated.Error);
        Assert.True(deleted.IsSuccess, deleted.Error);
        var persisted = await fixture.Db.Comments.AsNoTracking().SingleAsync(item => item.Id == comment.Id);
        Assert.Equal("Managed after Group removal", persisted.Body);
        Assert.Equal(fixture.Clock.UtcNow, persisted.DeletedAt);
        Assert.Equal(ProjectRole.Owner, (await fixture.Db.ProjectMembers.AsNoTracking().SingleAsync()).Role);
        Assert.Equal(2, await fixture.Db.AuditLogs.AsNoTracking().CountAsync());
    }

    private static async Task<Guid> SeedTargetAsync(ProjectServicePersistenceContext fixture, CommentTargetType targetType)
    {
        if (targetType == CommentTargetType.Project) return fixture.ProjectId;
        await fixture.Db.Projects.SingleAsync(project => project.Id == fixture.ProjectId);
        var milestone = new Milestone { ProjectId = fixture.ProjectId, Name = "Milestone", DueDate = new DateOnly(2026, 11, 5) };
        fixture.Db.Milestones.Add(milestone);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        return milestone.Id;
    }

    private static async Task<(bool Success, string? Error)> MutateCommentAsync(
        ProjectServicePersistenceContext fixture, Guid commentId, string operation)
    {
        if (operation == "edit")
        {
            var updated = await fixture.Service.UpdateCommentAsync(commentId, new UpdateCommentRequest("Unauthorized edit"), fixture.Token);
            return (updated.IsSuccess, updated.Error);
        }
        var deleted = await fixture.Service.DeleteCommentAsync(commentId, fixture.Token);
        return (deleted.IsSuccess, deleted.Error);
    }
}
