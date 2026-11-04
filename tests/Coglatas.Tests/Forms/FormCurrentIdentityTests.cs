using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Forms;

public sealed partial class FormParentBoundaryCompatibilityTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("tenant")]
    [InlineData("workspace")]
    [InlineData("group")]
    [InlineData("project")]
    [InlineData("creator")]
    [InlineData("status")]
    [InlineData("deleted")]
    public Task ExistingFormAuthorityCannotBeBorrowedByAnInconsistentCallerCopy(string field) =>
        WithAuthorityFixtureAsync(field == "group" ? "group" : field == "project" ? "project" : "workspace", async fixture =>
        {
            // Both resources are genuinely accessible. A nonexistent destination
            // cannot stand in for the persisted-identity comparison under test.
            var workspace = new Workspace
            {
                TenantId = fixture.TenantId, Name = "Accessible alternate", Slug = "alternate", CreatedByUserId = fixture.Actor.Id
            };
            var group = new Group
            {
                TenantId = fixture.TenantId, WorkspaceId = workspace.Id, Name = "Alternate group", Slug = "alternate", CreatedByUserId = fixture.Actor.Id
            };
            var project = new Project
            {
                TenantId = fixture.TenantId, WorkspaceId = workspace.Id, Name = "Alternate project", Slug = "alternate",
                CreatedByUserId = fixture.Actor.Id, OwnerUserId = fixture.Actor.Id, Status = ProjectStatus.Active
            };
            fixture.Db.Workspaces.Add(workspace);
            fixture.Db.WorkspaceMembers.Add(new WorkspaceMember
            {
                TenantId = fixture.TenantId, WorkspaceId = workspace.Id, UserId = fixture.Actor.Id,
                Role = WorkspaceRole.Admin, Status = MembershipStatus.Active
            });
            fixture.Db.Groups.Add(group);
            fixture.Db.GroupMembers.Add(new GroupMember
            {
                TenantId = fixture.TenantId, GroupId = group.Id, UserId = fixture.Actor.Id, Role = GroupRole.Admin
            });
            fixture.Db.Projects.Add(project);
            fixture.Db.ProjectMembers.Add(new ProjectMember
            {
                TenantId = fixture.TenantId, ProjectId = project.Id, UserId = fixture.Actor.Id, Role = ProjectRole.Manager
            });
            await fixture.Db.SaveChangesAsync();
            Assert.True(await fixture.Authorization.CanModifyForm(fixture.Actor.Id, fixture.Form));
            Assert.True(await fixture.Authorization.CanCreateForm(fixture.Actor.Id, workspace.Id, null, null));
            Assert.True(await fixture.Authorization.CanCreateForm(fixture.Actor.Id, null, group.Id, null));
            Assert.True(await fixture.Authorization.CanCreateForm(fixture.Actor.Id, null, null, project.Id));
            var copy = new InternalForm
            {
                Id = fixture.Form.Id, TenantId = fixture.Form.TenantId, WorkspaceId = fixture.Form.WorkspaceId,
                GroupId = fixture.Form.GroupId, ProjectId = fixture.Form.ProjectId,
                CreatedByUserId = fixture.Form.CreatedByUserId, Status = fixture.Form.Status
            };
            switch (field)
            {
                case "missing": copy.Id = Guid.NewGuid(); break;
                case "tenant": copy.TenantId = Guid.NewGuid(); break;
                case "workspace": copy.WorkspaceId = workspace.Id; break;
                case "group": copy.GroupId = group.Id; break;
                case "project": copy.ProjectId = project.Id; break;
                case "creator": copy.CreatedByUserId = fixture.Other.Id; break;
                case "status": copy.Status = FormStatus.Open; break;
                case "deleted": copy.MarkDeleted(new Clock().UtcNow); break;
                default: throw new InvalidOperationException("Unknown identity mismatch.");
            }
            var before = await fixture.StateAsync();

            Assert.False(await fixture.Authorization.CanViewForm(fixture.Actor.Id, copy));
            Assert.False(await fixture.Authorization.CanManageForm(fixture.Actor.Id, copy));
            Assert.False(await fixture.Authorization.CanModifyForm(fixture.Actor.Id, copy));
            Assert.False(await fixture.Authorization.CanAccessScope(fixture.Actor.Id, copy));
            Assert.Equal(before, await fixture.StateAsync());
        });
}
