using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Forms;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Forms;

public sealed partial class FormParentBoundaryCompatibilityTests
{
    private static readonly string[] StoredAuthorityChanges =
        ["membership", "form-parent", "project-parent", "group-parent", "workspace", "account", "creator"];

    [Theory]
    [InlineData("membership")]
    [InlineData("form-parent")]
    [InlineData("project-parent")]
    [InlineData("group-parent")]
    [InlineData("workspace")]
    [InlineData("account")]
    [InlineData("creator")]
    public Task CommittedAuthorityChangeAfterInitialFormLoadDeniesTheSameServiceCall(string change) =>
        VerifyStoredAuthorityChangeAsync(change);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task NativeCommittedAuthorityChangesCannotHideBehindTheLoadedFormGraph()
    {
        foreach (var change in StoredAuthorityChanges)
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
                PostgreSqlTestEnvironment.RequireConnectionString(), database => VerifyStoredAuthorityChangeAsync(change, database));
    }

    private static async Task VerifyStoredAuthorityChangeAsync(string change, string? connectionString = null)
    {
        var scope = change == "project-parent" ? "project" : change == "group-parent" ? "group" : "workspace";
        await using var fixture = await Fixture.CreateAsync(scope, connectionString: connectionString);
        Assert.True((await fixture.Service.GetAsync(fixture.Form.Id)).IsSuccess);
        fixture.Db.ChangeTracker.Clear();
        string? expectedState = null;
        var hook = new AfterFormLoad(new FormRepository(fixture.Db), async () =>
        {
            await using var concurrent = new AppDbContext(fixture.Options, new CurrentTenant(fixture.TenantId));
            switch (change)
            {
                case "membership":
                    (await concurrent.WorkspaceMembers.SingleAsync(row => row.UserId == fixture.Actor.Id)).Status = MembershipStatus.Suspended;
                    break;
                case "workspace":
                    (await concurrent.Workspaces.SingleAsync(row => row.Id == fixture.Workspace.Id)).Status = WorkspaceStatus.Archived;
                    break;
                case "account":
                    (await concurrent.Users.SingleAsync(row => row.Id == fixture.Actor.Id)).Status = UserStatus.Suspended;
                    break;
                case "creator":
                    (await concurrent.InternalForms.SingleAsync(row => row.Id == fixture.Form.Id)).CreatedByUserId = fixture.Other.Id;
                    break;
                default:
                    var destination = new Workspace
                    {
                        TenantId = fixture.TenantId, Name = "Inaccessible destination", Slug = "inaccessible",
                        CreatedByUserId = fixture.Other.Id
                    };
                    concurrent.Workspaces.Add(destination);
                    if (change == "form-parent")
                        (await concurrent.InternalForms.SingleAsync(row => row.Id == fixture.Form.Id)).WorkspaceId = destination.Id;
                    else if (change == "project-parent")
                        (await concurrent.Projects.SingleAsync(row => row.Id == fixture.Project.Id)).WorkspaceId = destination.Id;
                    else if (change == "group-parent")
                        (await concurrent.Groups.SingleAsync(row => row.Id == fixture.Group.Id)).WorkspaceId = destination.Id;
                    else throw new InvalidOperationException("Unknown authority transition.");
                    break;
            }
            await concurrent.SaveChangesAsync();
            expectedState = await fixture.StateAsync();
        });
        var clock = new Clock();
        var actor = new CurrentUser(fixture.Actor.Id);
        var service = new FormService(hook, new WorkspaceRepository(fixture.Db), new GroupRepository(fixture.Db),
            new ProjectRepository(fixture.Db), fixture.Authorization, actor, clock,
            new DbAuditLogger(fixture.Db, clock, actor, new CurrentTenant(fixture.TenantId)),
            new NoNotifications(), new EfUnitOfWork(fixture.Db));

        var denied = await service.UpdateAsync(fixture.Form.Id, new UpdateFormRequest(Title: "Unauthorized after committed change"));

        Assert.NotNull(expectedState);
        Assert.False(denied.IsSuccess, $"Committed {change} must defeat the previously loaded authorization graph.");
        Assert.Equal("You are not allowed to update this form.", denied.Error);
        Assert.Equal(expectedState, await fixture.StateAsync());
        Assert.Equal(QueryTrackingBehavior.TrackAll, fixture.Db.ChangeTracker.QueryTrackingBehavior);
    }

    private sealed class AfterFormLoad(IFormRepository inner, Func<Task> afterLoad) : IFormRepository
    {
        private bool _called;
        public async Task<InternalForm?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var result = await inner.GetAsync(id, cancellationToken);
            if (!_called) { _called = true; await afterLoad(); }
            return result;
        }
        public Task<IReadOnlyList<InternalForm>> ListAsync(FormListQuery query, CancellationToken ct = default) => inner.ListAsync(query, ct);
        public Task AddAsync(InternalForm form, CancellationToken ct = default) => inner.AddAsync(form, ct);
        public Task AddQuestionAsync(FormQuestion question, CancellationToken ct = default) => inner.AddQuestionAsync(question, ct);
        public Task<FormQuestion?> GetQuestionAsync(Guid form, Guid question, CancellationToken ct = default) => inner.GetQuestionAsync(form, question, ct);
        public void RemoveQuestion(FormQuestion question) => inner.RemoveQuestion(question);
        public Task<bool> HasResponsesAsync(Guid form, CancellationToken ct = default) => inner.HasResponsesAsync(form, ct);
        public Task<FormResponse?> GetResponseForUserAsync(Guid form, Guid user, CancellationToken ct = default) => inner.GetResponseForUserAsync(form, user, ct);
        public Task<IReadOnlyList<FormResponse>> ListResponsesAsync(Guid form, CancellationToken ct = default) => inner.ListResponsesAsync(form, ct);
        public Task AddResponseAsync(FormResponse response, CancellationToken ct = default) => inner.AddResponseAsync(response, ct);
        public Task<IReadOnlyList<Guid>> ListScopeRecipientUserIdsAsync(InternalForm form, CancellationToken ct = default) => inner.ListScopeRecipientUserIdsAsync(form, ct);
        public Task<IReadOnlyList<FormScopeMember>> ListScopeMembersAsync(InternalForm form, CancellationToken ct = default) => inner.ListScopeMembersAsync(form, ct);
    }
}
