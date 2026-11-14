using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Preview;

/// <summary>Offline original Core debugger recordings; never a browser or server executor.</summary>
internal static class SyntheticSimulationDebugRecording
{
    private static SourceJson Json(JsonElement value) => SourceJson.Parse(value.GetRawText());
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    internal static (SourceJson Recording, ProjectSimulationRun Run) Capture(SimulationRunManifest manifest)
    {
        using var debugger = new ProjectSimulationDebugger(manifest, manifest.Plan.Input.InputDigest);
        var frames = new List<object>();
        void Retain(string action, ProjectLocation? target = null)
        {
            var snapshot = debugger.Snapshot;
            var events = Json(snapshot.Events.Select(item => item.Value).ToArray());
            frames.Add(new { action, target = target?.Data.Value,
                snapshot = new { text = snapshot.Data.CanonicalText, digest = snapshot.Digest.Data.Value },
                events = new { text = events.CanonicalText, digest = snapshot.TracePrefixDigest.Data.Value },
                inspection = snapshot.Inspection is null ? null : new { text = snapshot.Inspection.CanonicalText,
                    digest = ContentDigest.Compute("coglatas.simulation-task-inspection/1", snapshot.Inspection.ToCanonicalBytes()).Data.Value } });
            if (frames.Count > 64) throw new InvalidOperationException("The offline debugger recording exceeded its explicit frame profile.");
        }
        void Accept(SimulationControlResponse response, string action, ProjectLocation? target = null)
        {
            if (!response.Accepted || response.Snapshot is null) throw new InvalidOperationException("The actual debugger command was refused: " + response.Reason);
            Retain(action, target);
        }
        ProjectLocation? Next() => debugger.Snapshot.Pending is { } pending
            ? ProjectLocation.Parse(Json(pending.Value.GetProperty("location"))) : null;
        Retain("coglatas.recorded-preparation");
        if (Next() is { } first)
        {
            var points = new[] {
                new SimulationBreakpoint(Guid.Parse("00001bbd-0000-4000-8000-000000000001"), manifest.Plan.Digest, first, SimulationBreakpointPoint.TaskStart),
                new SimulationBreakpoint(Guid.Parse("00001bbe-0000-4000-8000-000000000001"), manifest.Plan.Digest, first, SimulationBreakpointPoint.TaskEnd),
                new SimulationBreakpoint(Guid.Parse("00001bbf-0000-4000-8000-000000000001"), manifest.Plan.Digest, first, (SimulationBreakpointPoint)999) };
            Accept(debugger.SetBreakpoints(debugger.Snapshot.Stamp, points), "coglatas.recorded-set-breakpoints");
            var tasks = manifest.Plan.Data.Value.GetProperty("tasks").EnumerateArray().ToArray();
            var dependent = manifest.Plan.Data.Value.GetProperty("dependencies").EnumerateArray().FirstOrDefault();
            if (dependent.ValueKind == JsonValueKind.Object)
            {
                var endpoint = dependent.GetProperty("dependent");
                var waiting = tasks.Single(task => Json(task.GetProperty("context")).Equals(Json(endpoint.GetProperty("context"))) &&
                    task.GetProperty("irNodeId").GetString() == endpoint.GetProperty("irNodeId").GetString());
                var location = ProjectLocation.Parse(Json(waiting.GetProperty("location")));
                Accept(debugger.Inspect(debugger.Snapshot.Stamp, location), "coglatas.inspect-fixed-task", location);
            }
            Accept(debugger.Continue(debugger.Snapshot.Stamp), "coglatas.continue");
            Accept(debugger.Inspect(debugger.Snapshot.Stamp, first), "coglatas.inspect-fixed-task", first);
            Accept(debugger.Step(debugger.Snapshot.Stamp), "coglatas.event-step");
            Accept(debugger.Inspect(debugger.Snapshot.Stamp, first), "coglatas.inspect-fixed-task", first);
            if (debugger.Snapshot.Result is null) Accept(debugger.Continue(debugger.Snapshot.Stamp), "coglatas.continue");
            if (debugger.Snapshot.Result is null) Accept(debugger.Step(debugger.Snapshot.Stamp), "coglatas.event-step");
            if (debugger.Snapshot.Result is null && Next() is { } next && debugger.Snapshot.Pending!.Value.GetProperty("kind").GetString() == "coglatas.task-start")
                Accept(debugger.StepOver(debugger.Snapshot.Stamp, next), "coglatas.task-step-over", next);
            while (debugger.Snapshot.Result is null) Accept(debugger.Continue(debugger.Snapshot.Stamp), "coglatas.continue");
            Accept(debugger.Inspect(debugger.Snapshot.Stamp, first), "coglatas.inspect-fixed-task", first);
        }
        var result = debugger.Snapshot.Result ?? throw new InvalidOperationException("The recording has no actual terminal result.");
        var full = ProjectSimulation.Execute(manifest, manifest.Plan.Input.InputDigest);
        if (!result.Data.Equals(full.Data)) throw new InvalidOperationException("Debug commands changed the terminal run bytes.");
        return (Json(new { format = "coglatas.local-simulation-debug-recording/1",
            controls = new { text = debugger.Controls.CanonicalText, digest = debugger.ControlsDigest.Data.Value },
            manifest = new { text = manifest.Data.CanonicalText, digest = manifest.Digest.Data.Value }, frames }), result);
    }
}
