using System.Collections.Concurrent;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Domain.ProjectIde;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.ProjectIde;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Tests.PostgreSql;

[Trait("Category", "PostgreSQLIntegration")]
public sealed class CompilerRecordingStorePostgreSqlTests
{
    [PostgreSqlFact]
    public Task PersistsOriginalScopedDigestsAndOnlyAppendsAnExactRetryOnce() => WithFixtureAsync(async fixture =>
    {
        var input = CreateInput(fixture.Seed);
        var request = new CompilerHostRequest(new(input));
        using var session = new LocalCompilerRecordingSession(PredicateCompilerHostTests.Profile(input));
        var original = await LocalCompilerRecordedInvocation.ExecuteAsync(session,
            session.Prepare(request, Guid.NewGuid(), CompilerOutputSnapshotMode.AvailableOutputs), request);
        var expected = CompilerRecordingEvidence.FromInvocation(original);
        Assert.Equal(CompilerHostStatus.Completed, expected.HostStatus);
        Assert.Equal(input.InputDigest.Value, expected.Source.InputDigest);
        Assert.Equal(original.Response.Compilation!.Result.ResultDigest.Value, expected.Result!.ResultDigest);

        await using var firstContext = PostgreSqlMigrationTestDatabase.CreatePlatformContext(fixture.Database);
        await using var secondContext = PostgreSqlMigrationTestDatabase.CreatePlatformContext(fixture.Database);
        var first = fixture.Store(firstContext);
        var second = fixture.Store(secondContext);
        var concurrent = await Task.WhenAll(first.RecordAsync(original, default), second.RecordAsync(original, default));
        Assert.Contains(CompilerRecordingWriteOutcome.Recorded, concurrent);
        Assert.Contains(CompilerRecordingWriteOutcome.AlreadyRecorded, concurrent);
        Assert.Equal(expected, await second.FindAsync(expected.Source, expected.SourceDigest, expected.RunId, default));

        using var competingSession = new LocalCompilerRecordingSession(PredicateCompilerHostTests.Profile(input));
        var conflicting = await LocalCompilerRecordedInvocation.ExecuteAsync(competingSession,
            competingSession.Prepare(request, expected.RunId), request);
        Assert.Equal(CompilerRecordingWriteOutcome.Unavailable, await first.RecordAsync(conflicting, default));
        Assert.Null(await first.FindAsync(expected.Source with { BranchId = Guid.NewGuid() }, expected.SourceDigest, expected.RunId, default));
        Assert.Null(await first.FindAsync(expected.Source with { ProjectId = Guid.NewGuid() }, expected.SourceDigest, expected.RunId, default));
        Assert.Null(await first.FindAsync(expected.Source with { TenantId = Guid.NewGuid() }, expected.SourceDigest, expected.RunId, default));

        var payload = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database,
            "SELECT \"Payload\"::text FROM projectide_compiler_recording_evidence WHERE \"RunId\" = @run", ("run", expected.RunId));
        Assert.Contains(expected.ReceiptDigest, payload);
        Assert.DoesNotContain("private-source-canary", payload);
        Assert.DoesNotContain("private-session-canary", payload);
        Assert.DoesNotContain("Bodies", payload);
        var mutation = await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
            "UPDATE projectide_compiler_recording_evidence SET \"RecordedAtUtc\" = now() WHERE \"RunId\" = @run", ("run", expected.RunId)));
        Assert.Equal(PostgresErrorCodes.RaiseException, mutation.SqlState);
        Assert.Equal(expected, await first.FindAsync(expected.Source, expected.SourceDigest, expected.RunId, default));
    });

    [PostgreSqlFact]
    public Task ProtectsDiagnosticLineageAndRevalidatesCurrentAuthorityBeforeCommitAndDisclosure() => WithFixtureAsync(async fixture =>
    {
        var input = CreateInput(fixture.Seed);
        var request = new CompilerHostRequest(new(input));
        using var session = new LocalCompilerRecordingSession(PredicateCompilerHostTests.Profile(input, noCapabilities: true));
        var original = await LocalCompilerRecordedInvocation.ExecuteAsync(session, session.Prepare(request, Guid.NewGuid()), request);
        var child = await LocalCompilerRecordedInvocation.ExecuteAsync(session,
            session.Prepare(request, Guid.NewGuid(), diagnosticOrigin: original.Receipt), request);
        var expected = CompilerRecordingEvidence.FromInvocation(child);
        Assert.Equal(CompilerHostStatus.Unsupported, expected.HostStatus);
        Assert.Null(expected.Result);
        Assert.Null(expected.CaptureDigest);

        await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(fixture.Database);
        var store = fixture.Store(context);
        Assert.Equal(CompilerRecordingWriteOutcome.Unavailable, await store.RecordAsync(child, default));
        Assert.Equal(CompilerRecordingWriteOutcome.Recorded, await store.RecordAsync(original, default));
        fixture.Authorization.AllowRead = false;
        Assert.Equal(CompilerRecordingWriteOutcome.Unavailable, await store.RecordAsync(child, default));
        fixture.Authorization.AllowRead = true;

        async ValueTask SetRevokedAsync(bool revoked) => await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
            "UPDATE sessions SET \"RevokedAt\" = CASE WHEN @revoked THEN now() ELSE NULL END WHERE \"Id\" = @session",
            ("revoked", revoked), ("session", fixture.Seed.SessionId));

        fixture.Authorization.BeforeDecision = async (operation, attempt) =>
        {
            if (operation == CompilerRecordingOperations.Record && attempt == 2)
                await SetRevokedAsync(true);
        };
        fixture.Authorization.Reset();
        Assert.Equal(CompilerRecordingWriteOutcome.Unavailable, await store.RecordAsync(child, default));
        await SetRevokedAsync(false);
        Assert.Null(await store.FindAsync(expected.Source, expected.SourceDigest, expected.RunId, default));
        fixture.Authorization.BeforeDecision = null;
        Assert.Equal(CompilerRecordingWriteOutcome.Recorded, await store.RecordAsync(child, default));

        fixture.Authorization.BeforeDecision = async (operation, attempt) =>
        {
            if (operation == CompilerRecordingOperations.Read && attempt == 2)
                await SetRevokedAsync(true);
        };
        fixture.Authorization.Reset();
        Assert.Null(await store.FindAsync(expected.Source, expected.SourceDigest, expected.RunId, default));
        await SetRevokedAsync(false);
        fixture.Authorization.BeforeDecision = null;
        fixture.Authorization.AllowRead = false;
        Assert.Null(await store.FindAsync(expected.Source, expected.SourceDigest, expected.RunId, default));
        fixture.Authorization.AllowRead = true;
        Assert.Equal(expected, await store.FindAsync(expected.Source, expected.SourceDigest, expected.RunId, default));
        Assert.Equal(original.Receipt.Digest.Value, expected.OriginReceiptDigest);
    });

    private static CompilerInputManifest CreateInput(Seed seed)
    {
        var example = CompilerContractFixtures.Source();
        var context = SourceRevisionContext.Committed(new(new(new TenantId(seed.TenantId), new ProjectId(seed.ProjectId),
            BranchId.New()), RevisionId.New()));
        var source = ProjectSource.Create(context, example.Documents, extensions: SourceJson.Parse("{\"fixture\":\"private-source-canary\"}"));
        return CompilerContractFixtures.Input(source: source);
    }

    private static Task WithFixtureAsync(Func<Fixture, Task> test) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var tenant = new Tenant { Slug = "projectide-recording", Name = "Recording", DisplayName = "Recording" };
            var user = new User { Email = "compiler@example.invalid", NormalizedEmail = "COMPILER@EXAMPLE.INVALID" };
            var session = new Session { UserId = user.Id, SessionKeyHash = "private-session-canary", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
            var workspace = new Workspace { TenantId = tenant.Id, Name = "Recording", Slug = "recording", CreatedByUserId = user.Id };
            var project = new Project { TenantId = tenant.Id, WorkspaceId = workspace.Id, Name = "Recording", Slug = "recording",
                OwnerUserId = user.Id, CreatedByUserId = user.Id };
            var membership = new TenantUser { TenantId = tenant.Id, UserId = user.Id, Status = TenantUserStatus.Active };
            writer.AddRange(tenant, user, session, workspace, project, membership);
            await writer.SaveChangesAsync();
            await test(new(database, new(tenant.Id, user.Id, session.Id, project.Id)));
        });

    private sealed record Seed(Guid TenantId, Guid UserId, Guid SessionId, Guid ProjectId);

    private sealed record Fixture(string Database, Seed Seed)
    {
        public AuthorizationPolicy Authorization { get; } = new(Seed);

        public CompilerRecordingStore Store(AppDbContext context)
        {
            var tenant = new CurrentTenantService();
            tenant.SetTenant(Seed.TenantId, "projectide-recording");
            var clock = new Clock();
            var viewers = new ProjectIdeViewerContextReader(new CurrentUser(Seed), tenant, new SessionRepository(context),
                new TenantRepository(context), clock);
            return new(context, new(viewers, Authorization), clock);
        }
    }

    private sealed class AuthorizationPolicy(Seed seed) : IProjectIdeOperationAuthorization
    {
        private readonly ConcurrentDictionary<SecurityOperationRef, int> _attempts = new();
        public bool AllowRead { get; set; } = true;
        public Func<SecurityOperationRef, int, ValueTask>? BeforeDecision { get; set; }
        public void Reset() => _attempts.Clear();

        public async ValueTask<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
            SecuritySourceIdentitySnapshot source, string sourceDigest, CancellationToken cancellationToken)
        {
            if (BeforeDecision is { } callback)
                await callback(operation, _attempts.AddOrUpdate(operation, 1, (_, attempts) => attempts + 1));
            return viewer.UserId == seed.UserId && source.ProjectId == seed.ProjectId &&
                (operation == CompilerRecordingOperations.Record || (AllowRead && operation == CompilerRecordingOperations.Read));
        }
    }

    private sealed class CurrentUser(Seed seed) : ICurrentUser
    {
        public Guid? UserId => seed.UserId;
        public Guid? SessionId => seed.SessionId;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => true;
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
