using System.Net;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

internal static class AqPostgreSqlScenario
{
    public static Task WithHostedEventAsync(Func<string, AdmissionHost, Project, ActivityEvent, Task> scenario) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(),
            async database =>
            {
                var version = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, "SELECT version()");
                Assert.StartsWith("PostgreSQL ", version, StringComparison.Ordinal);
                await using var host = await AdmissionHost.CreateAsync(database, configurationOverrides:
                    new Dictionary<string, string?> { ["AQ:EnablePublicSurveys"] = "true" });
                var (project, activity) = await SeedEventAsync(database, host);
                await scenario(database, host, project, activity);
            });

    public static async Task<(Project Project, ActivityEvent Activity)> SeedEventAsync(string database,
        AdmissionHost host)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        Assert.True(db.Database.IsNpgsql());
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var end = start.AddHours(2);
        var project = new Project
        {
            TenantId = host.Tenant.Id,
            WorkspaceId = host.Workspace.Id,
            OwnerUserId = host.Actor.Id,
            CreatedByUserId = host.Actor.Id,
            Name = "Synthetic feedback project",
            Slug = "synthetic-feedback-project",
            Status = ProjectStatus.Active
        };
        var activity = new ActivityEvent
        {
            TenantId = host.Tenant.Id,
            ProjectId = project.Id,
            CreatedByUserId = host.Actor.Id,
            Title = "Synthetic feedback event",
            StartsAt = start,
            EndsAt = end,
            Status = EventStatus.Published
        };

        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            TenantId = host.Tenant.Id,
            ProjectId = project.Id,
            UserId = host.Actor.Id,
            Role = ProjectRole.Owner,
            JoinedAt = start
        });
        db.ActivityEvents.Add(activity);
        await db.SaveChangesAsync();
        return (project, activity);
    }

    public static HttpClient CreateGuestClient(AdmissionHost host) =>
        new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
        {
            BaseAddress = host.Client.BaseAddress
        };
}
