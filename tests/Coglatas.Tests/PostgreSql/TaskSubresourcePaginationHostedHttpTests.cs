using System.Net;
using System.Text.Json;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class TaskV1Pr05KanbanHostedHttpTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task Subtasks_HugePagesStayEmptyAndPreserveNormalPagesAndAuthorization() => VerifyTaskPagingAsync("subtasks", 50);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task TaskFiles_HugePagesStayEmptyAndPreserveNormalPagesAndAuthorization() => VerifyTaskPagingAsync("files", 20);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task TaskComments_HugePagesStayEmptyAndPreserveNormalPagesAndAuthorization() => VerifyTaskPagingAsync("comments", 20);

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task LegacyTaskComments_HugePagesStayEmptyAndPreserveNormalPagesAndAuthorization() => VerifyTaskPagingAsync("legacy", 50);

    private static async Task VerifyTaskPagingAsync(string resource, int defaultSize)
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            await using var app = await KanbanHostedTestApp.CreateAsync(database);
            var expected = await app.SeedPagingRowsAsync(resource);
            var actor = await app.LoginAsync(app.Graph.Manager, app.Graph.TenantA);
            var route = PagingRoute(resource, app.Graph.VisibleTask.Id);

            using (var response = await actor.GetAsync(route))
            {
                var page = await ReadPagingJsonAsync(response);
                Assert.Equal(1, page.GetProperty("page").GetInt32());
                Assert.Equal(defaultSize, page.GetProperty("pageSize").GetInt32());
                Assert.Equal(expected, PagingIds(page));
            }

            int[] pageNumbers = [1, 2, 3, 4];
            foreach (var pageNumber in pageNumbers)
            {
                using var response = await actor.GetAsync($"{route}&page={pageNumber}&pageSize=2");
                var page = await ReadPagingJsonAsync(response);
                Assert.Equal(expected.Skip((pageNumber - 1) * 2).Take(2), PagingIds(page));
                Assert.Equal(6, page.GetProperty("totalCount").GetInt32());
                if (resource != "legacy")
                    Assert.Equal(pageNumber < 3, page.GetProperty("hasMore").GetBoolean());
            }

            string[] hugeQueries =
                     [
                         "page=2147483647&pageSize=50",
                         "page=42949674&pageSize=100",
                         "PAGE=042949674&PAGESIZE=100",
                         "page=%2B42949674&pageSize=100",
                         "page=2147483647&pageSize=2147483647",
                         "page=2147483647&pageSize=1"
                     ];
            foreach (var query in hugeQueries)
            {
                using var response = await actor.GetAsync($"{route}&{query}");
                var page = await ReadPagingJsonAsync(response);
                Assert.Empty(PagingIds(page));
                Assert.Equal(6, page.GetProperty("totalCount").GetInt32());
                Assert.Equal(query.Contains("42949674", StringComparison.Ordinal) ? 42949674 : int.MaxValue,
                    page.GetProperty("page").GetInt32());
                Assert.InRange(page.GetProperty("pageSize").GetInt32(), 1, 100);
                if (resource != "legacy") Assert.False(page.GetProperty("hasMore").GetBoolean());
            }

            using (var response = await actor.GetAsync($"{route}&page=-1&pageSize=0"))
            {
                var page = await ReadPagingJsonAsync(response);
                Assert.Equal(1, page.GetProperty("page").GetInt32());
                var normalizedSize = resource == "legacy" ? 50 : 1;
                Assert.Equal(normalizedSize, page.GetProperty("pageSize").GetInt32());
                Assert.Equal(expected.Take(normalizedSize), PagingIds(page));
            }

            string[] invalidWirePages = ["2147483648", "1%27", ""];
            foreach (var wirePage in invalidWirePages)
            {
                using var response = await actor.GetAsync($"{route}&page={wirePage}");
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }

            using (var response = await app.SendAnonymousGetAsync(app.Graph.TenantA, $"{route}&page=2147483647"))
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            if (resource == "subtasks")
            {
                using var response = await actor.GetAsync("/api/tasks/taskItemId/subtasks?page=2147483647");
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            using (var response = await actor.GetAsync(PagingRoute(resource, Guid.NewGuid()) + "&page=2147483647"))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            await app.SetWorkspaceMembershipStatusAsync(app.Graph.TenantA, app.Graph.Workspace, app.Graph.Manager, MembershipStatus.Suspended);
            using (var response = await actor.GetAsync($"{route}&page=2147483647"))
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain("totalCount", body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("paging-row", body, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    private static string PagingRoute(string resource, Guid taskId) => resource == "legacy"
        ? $"/api/comments?targetType=TaskItem&targetId={taskId:D}"
        : $"/api/tasks/{taskId:D}/{resource}?";

    private static async Task<JsonElement> ReadPagingJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static Guid[] PagingIds(JsonElement page) => page.GetProperty("items").EnumerateArray()
        .Select(item => item.GetProperty("id").GetGuid()).ToArray();

    private sealed partial class KanbanHostedTestApp
    {
        public async Task<Guid[]> SeedPagingRowsAsync(string resource)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            SetTenant(scope.ServiceProvider, Graph.TenantA);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Projects.SingleAsync(project => project.Id == Graph.Project.Id);
            var ids = new List<Guid>();
            for (var index = 0; index < 7; index++)
            {
                var parent = index < 6 ? Graph.VisibleTask : Graph.MoveTask;
                var createdAt = new DateTimeOffset(2026, 10, 19, 0, 0, index, TimeSpan.Zero);
                var id = Guid.Parse($"00000000-0000-4000-8000-{index + 1:000000000000}");
                if (resource == "subtasks")
                {
                    var child = Task(Graph.TenantA, Graph.Workspace, Graph.Project, Graph.MainStages.Todo,
                        Graph.Manager, $"paging-row-{index}", index);
                    child.Id = id;
                    child.ParentTaskItemId = parent.Id;
                    db.TaskItems.Add(child);
                    id = child.Id;
                }
                else if (resource == "files")
                {
                    var file = new FileObject
                    {
                        TenantId = Graph.TenantA.Id, WorkspaceId = Graph.Workspace.Id, ProjectId = Graph.Project.Id,
                        UploadedByUserId = Graph.Manager.Id, OriginalFileName = $"paging-row-{index}.txt",
                        StorageKey = $"paging-fixture-{index}", ContentType = "text/plain", SizeBytes = 1
                    };
                    var attachment = new Attachment
                    {
                        Id = id,
                        TenantId = Graph.TenantA.Id, WorkspaceId = Graph.Workspace.Id, FileObject = file,
                        FileObjectId = file.Id, OwnerType = AttachmentOwnerType.TaskItem, OwnerId = parent.Id,
                        OwnerUserId = Graph.Manager.Id, UploadedByUserId = Graph.Manager.Id,
                        FileName = file.OriginalFileName, ContentType = "text/plain", SizeBytes = 1,
                        CreatedAt = createdAt
                    };
                    db.Attachments.Add(attachment);
                    id = attachment.Id;
                }
                else
                {
                    var comment = new TaskComment
                    {
                        Id = id,
                        TenantId = Graph.TenantA.Id, WorkspaceId = Graph.Workspace.Id, ProjectId = Graph.Project.Id,
                        TaskItemId = parent.Id, AuthorUserId = Graph.Manager.Id, BodyPlainText = $"paging-row-{index}",
                        CreatedAt = createdAt
                    };
                    db.TaskComments.Add(comment);
                    id = comment.Id;
                }
                if (index < 6) ids.Add(id);
            }
            await db.SaveChangesAsync();
            return ids.ToArray();
        }
    }
}
