using Coglatas.Tests.PostgreSql;

namespace Coglatas.Tests.Groups;

public sealed partial class GroupServiceUpdateValidationTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PostgreSqlRejectedGroupUpdateCannotEscapeThroughLaterCallerSave()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await AssertInvalidNameAsync("", true, database);
                await AssertInvalidNameAsync(" ", true, database);
                await AssertInvalidNameAsync("\t", true, database);
                await AssertInvalidNameAsync("", false, database);
                await AssertInvalidNameAsync(" ", false, database);
                await AssertInvalidNameAsync("\t", false, database);
                await AssertParentDenialAsync("missing", database);
                await AssertParentDenialAsync("foreign", database);
                await AssertParentDenialAsync("self", database);
                await AssertActorDenialAsync(false, true, database);
                await AssertActorDenialAsync(true, false, database);
                await AssertMissingGroupAsync(database);
                await AssertValidUpdateAsync(null, "Subject", "subject", database);
                await AssertValidUpdateAsync(" Renamed ", "Renamed", "renamed", database);
            });
    }
}
