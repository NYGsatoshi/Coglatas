using System.Diagnostics;
using Coglatas.Infrastructure.Persistence;

namespace Coglatas.Tests.PostgreSql;

public sealed class PerformanceDbCaptureListenerTests
{
    [Fact]
    public void AnotherActiveListenerDoesNotRecordIntoInactiveObserver()
    {
        // ActivitySource dispatches stop notifications to subscribed listeners
        // even when the individual listener did not request sampling. An
        // inactive capture must not inherit another capture's measurement.
        using var inactive = new PerformanceDbCapture();
        using var active = new PerformanceDbCapture();
        using var source = new ActivitySource("Npgsql");
        using (var measurement = active.Begin())
        {
            using (var activity = source.StartActivity())
            {
                Assert.NotNull(activity);
                activity.SetTag("db.query.text", "SELECT 1");
            }

            var command = Assert.Single(measurement.Snapshot());
            Assert.Equal(PerformanceDbCapture.SqlShape.Inspect("SELECT 1").Fingerprint, command.Fingerprint);
            Assert.False(command.Failed);
        }

        // Starting the previously inactive observer must not expose another
        // capture's command, and reversing the roles must remain isolated.
        using var isolated = inactive.Begin();
        Assert.Empty(isolated.Snapshot());
        using (var activity = source.StartActivity())
        {
            Assert.NotNull(activity);
            activity.SetTag("db.query.text", "SELECT 2 FROM task_items");
        }

        var isolatedCommand = Assert.Single(isolated.Snapshot());
        Assert.Equal(PerformanceDbCapture.SqlShape.Inspect("SELECT 2 FROM task_items").Fingerprint,
            isolatedCommand.Fingerprint);
        Assert.Equal("task_items", isolatedCommand.RootTable);
        Assert.False(isolatedCommand.Failed);
    }
}
