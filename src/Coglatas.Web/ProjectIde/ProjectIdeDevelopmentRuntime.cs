using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.ProjectIde;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Coglatas.Web.ProjectIde;

/// <summary>The exact server-owned Source binding for a retained Compiler recording.</summary>
public sealed record ProjectIdeRecordingSource(SecuritySourceIdentitySnapshot Identity, ContentDigest Digest);

/// <summary>Resolves a recording's Source within the current viewer's scope.</summary>
public interface IProjectIdeRecordingSourceResolver
{
    ValueTask<ProjectIdeRecordingSource?> ResolveAsync(ProjectIdeViewerContext viewer, Guid projectId, Guid runId,
        CancellationToken cancellationToken);
}

public sealed record ProjectIdeCompilerRecordingResponse(
    Guid RunId, string ReceiptDigest, string HostStatus, string CaptureMode, string? CaptureState,
    ProjectIdeCompilerResultResponse? Result);

public sealed record ProjectIdeCompilerResultResponse(
    string ResultDigest, string Execution, string Coverage, string Verdict, bool EvidenceComplete);

public static class ProjectIdeDevelopmentRuntime
{
    public const string EnabledConfigurationKey = "ProjectIde:DevelopmentRuntime:Enabled";
    public const string RecordingPath = "/api/projectide/runtime/projects/{projectId:guid}/compiler-recordings/{runId:guid}";

    public static bool AddProjectIdeDevelopmentRuntime(this WebApplicationBuilder builder)
    {
        if (!IsRequested(builder.Environment, builder.Configuration) ||
            !builder.Services.Any(service => service.ServiceType == typeof(IProjectIdeRecordingSourceResolver) && !service.IsKeyedService) ||
            !builder.Services.Any(service => service.ServiceType == typeof(IProjectIdeOperationAuthorization) && !service.IsKeyedService))
            return false;

        builder.Services.AddScoped<ProjectIdeViewerContextReader>();
        builder.Services.AddScoped<ProjectIdeOperationAccess>();
        builder.Services.AddScoped<ICompilerRecordingStore, CompilerRecordingStore>();
        return true;
    }

    public static bool MapProjectIdeDevelopmentRuntime(this WebApplication app, bool registered)
    {
        if (!registered || !IsRequested(app.Environment, app.Configuration))
            return false;

        app.MapGet(RecordingPath, ReadRecordingAsync)
            .RequireAuthorization()
            .WithName("ProjectIdeDevelopmentCompilerRecording")
            .Produces<ApiErrorEnvelope>(StatusCodes.Status401Unauthorized);
        return true;
    }

    private static bool IsRequested(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue<bool>(EnabledConfigurationKey);

    private static async Task<Results<Ok<ProjectIdeCompilerRecordingResponse>, NotFound>> ReadRecordingAsync(
        Guid projectId, Guid runId, HttpResponse response, ProjectIdeViewerContextReader viewers,
        IProjectIdeRecordingSourceResolver sources, ICompilerRecordingStore recordings, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "private, no-store";
        if (response.HttpContext.Features.Get<IStatusCodePagesFeature>() is { } statusPages)
            statusPages.Enabled = false;

        var viewer = viewers.Capture();
        if (projectId == Guid.Empty || runId == Guid.Empty || viewer is null ||
            !await viewers.IsCurrentAsync(viewer, cancellationToken))
            return TypedResults.NotFound();

        var source = await sources.ResolveAsync(viewer, projectId, runId, cancellationToken);
        if (source is null || source.Identity.TenantId != viewer.TenantId || source.Identity.ProjectId != projectId ||
            !await viewers.IsCurrentAsync(viewer, cancellationToken))
            return TypedResults.NotFound();

        var recording = await recordings.FindAsync(source.Identity, source.Digest.Value, runId, cancellationToken);
        if (recording is null)
            return TypedResults.NotFound();

        var result = recording.Result;
        return TypedResults.Ok(new ProjectIdeCompilerRecordingResponse(
            recording.RunId, recording.ReceiptDigest, recording.HostStatus.ToString(), recording.CaptureMode.ToString(),
            recording.OriginalCaptureState?.ToString(), result is null ? null : new(
                result.ResultDigest, result.Execution.ToString(), result.Coverage.ToString(), result.Verdict.ToString(),
                result.EvidenceComplete)));
    }
}
