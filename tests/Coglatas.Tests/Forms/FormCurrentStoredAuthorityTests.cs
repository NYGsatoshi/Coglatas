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
    [InlineData(QueryTrackingBehavior.TrackAll, false)]
    [InlineData(QueryTrackingBehavior.TrackAll, true)]
    [InlineData(QueryTrackingBehavior.NoTracking, false)]
    [InlineData(QueryTrackingBehavior.NoTracking, true)]
    [InlineData(QueryTrackingBehavior.NoTrackingWithIdentityResolution, false)]
    [InlineData(QueryTrackingBehavior.NoTrackingWithIdentityResolution, true)]
    public async Task AuthorityReadRestoresTrackingAndRetainsPendingCallerChanges(QueryTrackingBehavior previous, bool fail)
    {
        await using var fixture = await Fixture.CreateAsync("workspace");
        var originalName = fixture.Workspace.Name;
        fixture.Workspace.Name = "Caller-owned pending change";
        fixture.Db.Entry(fixture.Workspace).State = EntityState.Modified;
        fixture.Db.ChangeTracker.QueryTrackingBehavior = previous;
        var repository = new FormRepository(fixture.Db);
        var failure = new IOException("Synthetic authorization dependency failure.");
        using var cancellation = new CancellationTokenSource();
        Task<bool> Read() => repository.ReadCurrentAuthorizationAsync(token =>
        {
            Assert.Equal(cancellation.Token, token);
            Assert.Equal(QueryTrackingBehavior.NoTracking, fixture.Db.ChangeTracker.QueryTrackingBehavior);
            return fail ? Task.FromException<bool>(failure) : Task.FromResult(true);
        }, cancellation.Token);

        if (fail) Assert.Same(failure, await Assert.ThrowsAsync<IOException>(Read));
        else Assert.True(await Read());

        Assert.Equal(previous, fixture.Db.ChangeTracker.QueryTrackingBehavior);
        Assert.Same(fixture.Workspace, fixture.Db.Entry(fixture.Workspace).Entity);
        Assert.Equal(EntityState.Modified, fixture.Db.Entry(fixture.Workspace).State);
        Assert.Equal("Caller-owned pending change", fixture.Workspace.Name);
        Assert.Equal(originalName, await fixture.Db.Workspaces.AsNoTracking().Select(row => row.Name).SingleAsync());
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task CancelledAuthorityQueryRestoresTrackingWithoutSaving()
    {
        await using var fixture = await Fixture.CreateAsync("workspace");
        var repository = new FormRepository(fixture.Db);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ReadCurrentAuthorizationAsync(async token =>
        {
            await repository.GetCurrentAuthorizationAsync(fixture.Form.Id, token);
            return true;
        }, cancellation.Token));

        Assert.Equal(QueryTrackingBehavior.TrackAll, fixture.Db.ChangeTracker.QueryTrackingBehavior);
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task NativeAuthorizedFormMutationPreservesCallerTransactionAndPendingWrites() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await Fixture.CreateAsync("workspace", connectionString: database);
                var originalTitle = fixture.Form.Title;
                var originalWorkspaceName = fixture.Workspace.Name;
                await using var caller = await fixture.Db.Database.BeginTransactionAsync();
                fixture.Workspace.Name = "Caller-owned pending name";

                var updated = await fixture.Service.UpdateAsync(fixture.Form.Id, new UpdateFormRequest(Title: "Authorized update"));

                Assert.True(updated.IsSuccess, updated.Error);
                Assert.Same(caller, fixture.Db.Database.CurrentTransaction);
                Assert.Equal("Authorized update", updated.Value!.Title);
                Assert.Equal("Caller-owned pending name", await fixture.Db.Workspaces.AsNoTracking().Select(row => row.Name).SingleAsync());
                Assert.Single(await fixture.Db.AuditLogs.Where(row => row.Action == "FormUpdated").ToListAsync());
                Assert.Same(fixture.Workspace, fixture.Db.Workspaces.Local.Single());
                await caller.RollbackAsync();

                await using var verification = new AppDbContext(fixture.Options, new CurrentTenant(fixture.TenantId));
                Assert.Equal(originalTitle, await verification.InternalForms.Select(row => row.Title).SingleAsync());
                Assert.Equal(originalWorkspaceName, await verification.Workspaces.Select(row => row.Name).SingleAsync());
                Assert.Empty(await verification.AuditLogs.ToListAsync());
            });

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
