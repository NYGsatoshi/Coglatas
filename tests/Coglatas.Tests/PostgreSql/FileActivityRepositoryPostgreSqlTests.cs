using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
[Trait("Category", "PostgreSQLIntegration")]
[Trait("Scope", "Issue1057")]
public sealed class FileActivityRepositoryPostgreSqlTests
{
    [PostgreSqlFact]
    public async Task VersionHistoryAcceptsNullAndExactFiltersWhilePreservingScopeOrderingAndBounds()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(connectionString, async database =>
        {
            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var tenant = new Tenant { Name = "File history", DisplayName = "File history", Slug = "file-history" };
            var user = new User
            {
                DisplayName = "History uploader",
                Email = "history@example.test",
                NormalizedEmail = "HISTORY@EXAMPLE.TEST",
                PasswordHash = "hash",
                Status = UserStatus.Active
            };
            var workspace = new Workspace
            {
                TenantId = tenant.Id,
                Name = "File history workspace",
                Slug = "file-history",
                CreatedByUserId = user.Id
            };
            var file = CreateFile(tenant.Id, workspace.Id, user.Id, "history.txt");
            var otherFile = CreateFile(tenant.Id, workspace.Id, user.Id, "other.txt");
            db.AddRange(tenant, user, workspace, file, otherFile);
            await db.SaveChangesAsync();

            var repository = new FileRepository(db);
            // The migrated trigger writes version 1; the list passes a null UUID filter.
            var initial = Assert.Single(await repository.ListFileVersionsAsync(tenant.Id, file.Id, 100));
            Assert.Equal(file.Id, initial.Id);
            Assert.Equal(file.Id, initial.FileObjectId);
            Assert.Equal(1, initial.VersionNumber);
            Assert.Equal(file.OriginalFileName, initial.OriginalFileName);
            Assert.Equal(file.ContentType, initial.ContentType);
            Assert.Equal(file.SizeBytes, initial.SizeBytes);
            Assert.Equal(user.Id, initial.CreatedByUserId);
            Assert.Equal(user.DisplayName, initial.CreatedByDisplayName);
            Assert.Equal(initial, await repository.GetFileVersionAsync(tenant.Id, file.Id, initial.Id));

            var secondId = Guid.NewGuid();
            var thirdId = Guid.NewGuid();
            await AddVersionAsync(database, file.Id, secondId, 2, initial.CreatedAt.AddMinutes(2));
            await AddVersionAsync(database, file.Id, thirdId, 3, initial.CreatedAt.AddMinutes(1));
            var versions = await repository.ListFileVersionsAsync(tenant.Id, file.Id, 100);
            Assert.Equal(new[] { thirdId, secondId, file.Id }, versions.Select(version => version.Id));
            Assert.All(versions, version => Assert.Equal(file.Id, version.FileObjectId));
            Assert.Equal(2, (await repository.GetFileVersionAsync(tenant.Id, file.Id, secondId))?.VersionNumber);
            Assert.Equal(thirdId, Assert.Single(await repository.ListFileVersionsAsync(tenant.Id, file.Id, 1)).Id);
            Assert.Equal(thirdId, Assert.Single(await repository.ListFileVersionsAsync(tenant.Id, file.Id, 0)).Id);
            Assert.Equal(2, (await repository.ListFileVersionsAsync(tenant.Id, file.Id, 2)).Count);

            var otherTenantId = Guid.NewGuid();
            Assert.Empty(await repository.ListFileVersionsAsync(otherTenantId, file.Id, 100));
            Assert.Null(await repository.GetFileVersionAsync(otherTenantId, file.Id, initial.Id));
            Assert.Null(await repository.GetFileVersionAsync(tenant.Id, file.Id, Guid.NewGuid()));
            Assert.Null(await repository.GetFileVersionAsync(tenant.Id, otherFile.Id, secondId));
            Assert.Equal(otherFile.Id, Assert.Single(await repository.ListFileVersionsAsync(tenant.Id, otherFile.Id, 100)).Id);
        });
    }

    private static FileObject CreateFile(Guid tenantId, Guid workspaceId, Guid userId, string name) => new()
    {
        TenantId = tenantId,
        WorkspaceId = workspaceId,
        UploadedByUserId = userId,
        OriginalFileName = name,
        StorageKey = $"file-history/{Guid.NewGuid():N}",
        ContentType = "text/plain",
        SizeBytes = 10,
        Classification = DataClassification.Private,
        Status = FileObjectStatus.Active
    };

    private static Task AddVersionAsync(
        string database,
        Guid fileObjectId,
        Guid versionId,
        int versionNumber,
        DateTimeOffset createdAt) =>
        PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
            INSERT INTO file_versions (
                "Id", "TenantId", "FileObjectId", "VersionNumber", "OriginalFileName",
                "StorageKey", "ContentType", "SizeBytes", "HashSha256", "CreatedByUserId", "CreatedAt")
            SELECT @versionId, "TenantId", "FileObjectId", @versionNumber, "OriginalFileName",
                "StorageKey", "ContentType", "SizeBytes", "HashSha256", "CreatedByUserId", @createdAt
            FROM file_versions WHERE "Id" = @fileObjectId;
            """, ("versionId", versionId), ("versionNumber", versionNumber),
            ("createdAt", createdAt), ("fileObjectId", fileObjectId));
}
