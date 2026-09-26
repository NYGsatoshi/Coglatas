using System.Text.Json;
using Coglatas.Application.Projects;

namespace Coglatas.Tests.Projects;

public sealed class TaskUpdateDetailsRequestContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    [Trait("Scope", "TaskV1PR07B")]
    public void OmittedDeadlineIsNotSpecified()
    {
        var request = Deserialize("""{"expectedVersion":7}""");

        Assert.False(request.DeadlineAt.IsSpecified);
        Assert.Null(request.DeadlineAt.Value);
    }

    [Fact]
    public void OmittedStructuredBriefMembersAreNotSpecified()
    {
        var request = Deserialize("""{"expectedVersion":7}""");

        Assert.False(request.Goal.IsSpecified);
        Assert.False(request.Deliverable.IsSpecified);
        Assert.False(request.Constraints.IsSpecified);
    }

    [Fact]
    public void LegacyCreateJsonWithoutStructuredBriefRemainsCompatible()
    {
        const string json =
            """{"milestoneId":null,"title":"Legacy","description":"Free-form","priority":1,"startDate":null,"dueDate":null}""";

        var request = JsonSerializer.Deserialize<CreateTaskItemRequest>(json, WebJson);

        Assert.NotNull(request);
        Assert.Equal("Free-form", request.Description);
        Assert.Null(request.Goal);
        Assert.Null(request.Deliverable);
        Assert.Null(request.Constraints);
    }

    [Theory]
    [InlineData("goal")]
    [InlineData("deliverable")]
    [InlineData("constraints")]
    public void ExplicitNullStructuredBriefMemberIsSpecifiedForClear(string propertyName)
    {
        var request = Deserialize($$"""{"expectedVersion":7,"{{propertyName}}":null}""");
        var field = propertyName switch
        {
            "goal" => request.Goal,
            "deliverable" => request.Deliverable,
            _ => request.Constraints
        };

        Assert.True(field.IsSpecified);
        Assert.Null(field.Value);
    }

    [Theory]
    [InlineData("goal")]
    [InlineData("deliverable")]
    [InlineData("constraints")]
    public void StructuredBriefStringMemberIsSpecified(string propertyName)
    {
        var request = Deserialize($$"""{"expectedVersion":7,"{{propertyName}}":"value"}""");
        var field = propertyName switch
        {
            "goal" => request.Goal,
            "deliverable" => request.Deliverable,
            _ => request.Constraints
        };

        Assert.True(field.IsSpecified);
        Assert.Equal("value", field.Value);
    }

    [Fact]
    public void StructuredBriefProvenanceSerializesWithStableCamelCaseValues()
    {
        var response = new TaskBriefResponse(
            new TaskBriefFieldResponse("Goal", TaskBriefValueSource.TaskSpecific),
            new TaskBriefFieldResponse(null, TaskBriefValueSource.NotSet),
            new TaskBriefFieldResponse(null, TaskBriefValueSource.NotSet));

        var json = JsonSerializer.Serialize(response, WebJson);

        Assert.Contains("\"source\":\"taskSpecific\"", json, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"notSet\"", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scope", "TaskV1PR07B")]
    public void ExplicitNullDeadlineIsSpecified()
    {
        var request = Deserialize("""{"expectedVersion":7,"deadlineAt":null}""");

        Assert.True(request.DeadlineAt.IsSpecified);
        Assert.Null(request.DeadlineAt.Value);
    }

    [Fact]
    [Trait("Scope", "TaskV1PR07B")]
    public void IsoDeadlineValueIsSpecifiedWithItsOffset()
    {
        var request = Deserialize(
            """{"expectedVersion":7,"deadlineAt":"2026-08-03T00:15:00+09:00"}""");

        Assert.True(request.DeadlineAt.IsSpecified);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 3, 0, 15, 0, TimeSpan.FromHours(9)),
            request.DeadlineAt.Value);
    }

    [Theory]
    [Trait("Scope", "TaskV1PR07B")]
    [InlineData("isMajorDeadlineChange")]
    [InlineData("deadlineChangeClassification")]
    public void ClientCannotSupplyDeadlineSignificance(string propertyName)
    {
        var json = $$"""{"expectedVersion":7,"{{propertyName}}":true}""";

        Assert.Throws<JsonException>(() => Deserialize(json));
    }

    [Fact]
    [Trait("Scope", "TaskV1PR07B")]
    public void PlannedScheduleContractDoesNotOwnHardDeadline()
    {
        Assert.Null(typeof(TaskScheduleUpdateRequest).GetProperty("DeadlineAt"));

        const string json =
            """{"plannedStartDate":null,"plannedEndDate":null,"milestoneDate":null,"expectedVersion":7,"deadlineAt":"2026-08-03T00:15:00Z"}""";
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TaskScheduleUpdateRequest>(json, WebJson));
    }

    private static TaskUpdateDetailsRequest Deserialize(string json) =>
        JsonSerializer.Deserialize<TaskUpdateDetailsRequest>(json, WebJson)
        ?? throw new InvalidOperationException("Task update JSON unexpectedly deserialized to null.");
}
