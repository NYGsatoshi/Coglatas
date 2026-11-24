using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Forms.PublicSurveys;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Infrastructure.Persistence;

public sealed class EfAqPublicSurveyStore(
    AppDbContext context,
    ICurrentTenantAccessor currentTenant,
    IAqPublicProjectionCodec codec,
    IClock clock,
    ISurveyFeedbackInvalidation invalidation) : IAqPublicSurveyStore
{
    public Task<AqPublicProgram?> ReadProgramAsync(
        string codeDigest, string? cursor, int pageSize, CancellationToken cancellationToken) =>
        InPublicScopeAsync(codeDigest, isProgram: true, async (code, token) =>
        {
            var eventId = code.EventId!.Value;
            var parent = await FindParentAsync(eventId, token);
            var settings = await context.Set<EventFeedbackSettings>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.EventId == eventId && row.IsProgramPublished, token);
            if (parent is null || settings is null || !await IsCodeCurrentAsync(code.Id, token))
            {
                return null;
            }

            var query = from entry in context.Set<SurveyProgramEntry>().AsNoTracking()
                        join presentation in context.Set<Presentation>() on entry.PresentationId equals presentation.Id
                        join target in context.Set<SurveyTarget>() on entry.SurveyTargetId equals target.Id
                        join campaign in context.Set<SurveyCampaign>() on target.CampaignId equals campaign.Id
                        join definition in context.Set<FormDefinition>() on target.FormDefinitionId equals definition.Id
                        join schema in context.Set<PublishedSurveySchema>() on target.PublishedSchemaId equals schema.Id
                        join targetCode in context.Set<SurveyPublicCode>() on target.Id equals targetCode.SurveyTargetId
                        where entry.EventId == eventId && presentation.EventId == eventId &&
                            presentation.ProjectId == parent.ProjectId && target.EventId == eventId &&
                            target.PresentationId == presentation.Id && campaign.EventId == eventId &&
                            target.FormDefinitionId == campaign.FormDefinitionId && definition.DeletedAt == null &&
                            definition.Purpose == FormPurpose.Survey && schema.SurveyTargetId == target.Id &&
                            schema.FormDefinitionId == definition.Id && campaign.IsPublished &&
                            targetCode.EventId == null && targetCode.RevokedAtUtc == null &&
                            targetCode.ProtectedReprintValue != null
                        select new
                        {
                            PresentationId = presentation.Id,
                            presentation.Title,
                            presentation.Venue,
                            presentation.StartsAtUtc,
                            presentation.EndsAtUtc,
                            ProtectedEntryCode = targetCode.ProtectedReprintValue!,
                            EntryDigest = targetCode.CodeDigest
                        };

            if (cursor is not null)
            {
                var decoded = codec.UnprotectCursor(cursor);
                if (decoded is null || decoded.EventId != eventId || decoded.CodeId != code.Id)
                {
                    throw new AqInvalidProgramCursorException();
                }
                query = query.Where(row => row.StartsAtUtc > decoded.StartsAtUtc ||
                    row.StartsAtUtc == decoded.StartsAtUtc &&
                    row.PresentationId.CompareTo(decoded.PresentationId) > 0);
            }

            var rows = await query.OrderBy(row => row.StartsAtUtc).ThenBy(row => row.PresentationId)
                .Take(pageSize + 1).ToArrayAsync(token);
            var visible = rows.Take(pageSize).ToArray();
            var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
            var fields = settings.ProgramFields;
            var presenterNames = fields.HasFlag(SurveyProgramFields.PresenterNames)
                ? await ReadProgramPresenterNamesAsync(visible.Select(row => row.PresentationId).ToArray(),
                    parent.WorkspaceId, token)
                : null;
            var projected = new List<AqPublicProgramEntry>(visible.Length);
            foreach (var row in visible)
            {
                var entryCode = codec.UnprotectEntry(row.ProtectedEntryCode);
                if (entryCode is null ||
                    !string.Equals(AqPublicSurveyService.Hash(entryCode), row.EntryDigest, StringComparison.Ordinal))
                {
                    continue;
                }
                projected.Add(new AqPublicProgramEntry(
                    entryCode,
                    fields.HasFlag(SurveyProgramFields.Title) ? row.Title : null,
                    fields.HasFlag(SurveyProgramFields.Time)
                        ? TimeZoneInfo.ConvertTime(row.StartsAtUtc, zone) : null,
                    fields.HasFlag(SurveyProgramFields.Time)
                        ? TimeZoneInfo.ConvertTime(row.EndsAtUtc, zone) : null,
                    fields.HasFlag(SurveyProgramFields.Venue) ? row.Venue : null,
                    presenterNames?.GetValueOrDefault(row.PresentationId)));
            }

            var last = visible.LastOrDefault();
            var next = rows.Length > pageSize && last is not null
                ? codec.ProtectCursor(new AqProgramCursor(eventId, code.Id, last.StartsAtUtc, last.PresentationId))
                : null;
            return new AqPublicProgram(
                fields.HasFlag(SurveyProgramFields.Title) ? parent.EventTitle : null,
                fields.HasFlag(SurveyProgramFields.TimeZone) ? settings.TimeZoneId : null,
                projected, next);
        }, cancellationToken);

    private async Task<Dictionary<Guid, string[]>> ReadProgramPresenterNamesAsync(
        Guid[] presentationIds, Guid workspaceId, CancellationToken token)
    {
        var rows = await (from participant in context.Set<PresentationParticipant>().AsNoTracking()
                          join user in context.Users on participant.UserId equals user.Id
                          join member in context.TenantUsers on user.Id equals member.UserId
                          join workspaceMember in context.WorkspaceMembers on user.Id equals workspaceMember.UserId
                          where presentationIds.Contains(participant.PresentationId) &&
                              participant.Role == PresentationParticipantRole.Presenter &&
                              user.Status == UserStatus.Active && user.DeletedAt == null &&
                              member.Status == TenantUserStatus.Active &&
                              workspaceMember.WorkspaceId == workspaceId &&
                              workspaceMember.Status == MembershipStatus.Active
                          select new { participant.PresentationId, user.DisplayName })
            .ToArrayAsync(token);
        return rows.GroupBy(row => row.PresentationId).ToDictionary(group => group.Key,
            group => group.Select(row => row.DisplayName).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray());
    }

    public Task<AqPublicSurveyTarget?> ReadTargetAsync(
        string codeDigest, AqPublicSession? session, CancellationToken cancellationToken) =>
        InPublicScopeAsync(codeDigest, isProgram: false,
            (code, token) => FindTargetAsync(code, session, lockIdentity: false, token), cancellationToken);

    public async Task<AqPublicCommandResult> SubmitAsync(
        string codeDigest,
        string keyDigest,
        AqPublicSession? session,
        Func<AqPublicSurveyTarget, SubmissionIdempotency?, AqPublicWriteDecision> decide,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await InPublicScopeAsync<AqPublicCommandResult>(codeDigest, isProgram: false, async (code, token) =>
            {
                // Hold current public admission rows through the receipt and
                // submission commit. Shared row locks allow concurrent distinct
                // intents while excluding reception/publication/revocation writes.
                await using var transaction = await context.Database.BeginTransactionAsync(token);
                await LockSubmissionAuthorityAsync(code, token);
                var target = await FindTargetAsync(code, session, lockIdentity: true, token);
                if (target is null)
                {
                    return AqPublicCommandResult.Reject("NotFound");
                }
                if (!await TryLockKeyAsync(target.Target.Id, keyDigest, token))
                {
                    return AqPublicCommandResult.Reject("ConcurrentModification");
                }

                var previous = await context.Set<SubmissionIdempotency>()
                    .SingleOrDefaultAsync(row => row.SurveyTargetId == target.Target.Id && row.KeyDigest == keyDigest, token);
                var decision = decide(target, previous);
                var expired = previous is not null && !previous.CanReplayAt(clock.UtcNow) &&
                    decision.Result.Receipt?.RequestId != JsonSerializer.Deserialize<AqPublicReceipt>(previous.ReceiptJson)?.RequestId;
                if (expired)
                {
                    context.AuditLogs.Add(new AuditLog
                    {
                        TenantId = target.Target.TenantId,
                        Action = "AqPublicReceiptExpiredKeyReused",
                        EntityType = nameof(SurveyTarget),
                        EntityId = target.Target.Id,
                        CreatedAt = clock.UtcNow
                    });
                }

                if (decision.Plan is { } plan)
                {
                    PersistSubmission(target, keyDigest, previous, plan);
                    await invalidation.InvalidateAsync(target.Target.TenantId, target.Target.PresentationId, token);
                }
                if (decision.Plan is not null || expired)
                {
                    await context.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                }
                return decision.Result;
            }, cancellationToken);
            return result ?? AqPublicCommandResult.Reject("NotFound");
        }
        catch (PostgresException error) when (IsRetryableConflict(error))
        {
            return AqPublicCommandResult.Reject("ConcurrentModification");
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException postgres && IsRetryableConflict(postgres))
        {
            return AqPublicCommandResult.Reject("ConcurrentModification");
        }
    }

    private static bool IsRetryableConflict(PostgresException error) =>
        error.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;

    private async Task<T?> InPublicScopeAsync<T>(
        string codeDigest, bool isProgram,
        Func<PublicCodeBinding, CancellationToken, Task<T?>> use,
        CancellationToken cancellationToken) where T : class
    {
        // The globally unique high-entropy digest is the only pretenant lookup.
        // Never lift filters on submissions, memberships or tenant resources.
        var code = await context.Set<SurveyPublicCode>().IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CodeDigest == codeDigest && row.RevokedAtUtc == null &&
                (isProgram ? row.EventId != null : row.SurveyTargetId != null))
            .Select(row => new PublicCodeBinding(row.Id, row.TenantId, row.EventId, row.SurveyTargetId))
            .SingleOrDefaultAsync(cancellationToken);
        if (code is null || currentTenant.IsPlatformScope ||
            currentTenant.IsAvailable && currentTenant.TenantId != code.TenantId)
        {
            return null;
        }

        var tenant = await context.Tenants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == code.TenantId && row.Status == TenantStatus.Active && row.DeletedAt == null, cancellationToken);
        if (tenant is null)
        {
            return null;
        }
        var priorTenant = currentTenant.IsAvailable ? currentTenant.TenantId : (Guid?)null;
        var priorSlug = currentTenant.TenantSlug;
        currentTenant.SetTenant(tenant.Id, tenant.Slug);
        try
        {
            return await use(code, cancellationToken);
        }
        finally
        {
            if (priorTenant is { } tenantId && priorSlug is not null)
            {
                currentTenant.SetTenant(tenantId, priorSlug);
            }
            else
            {
                currentTenant.Clear();
            }
        }
    }

    private Task<bool> IsCodeCurrentAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<SurveyPublicCode>().AsNoTracking()
            .AnyAsync(row => row.Id == id && row.RevokedAtUtc == null, cancellationToken);

    private async Task<PublicParent?> FindParentAsync(Guid eventId, CancellationToken token)
    {
        var tenantId = currentTenant.TenantId;
        return await (from activity in context.ActivityEvents.AsNoTracking()
                      join project in context.Projects on activity.ProjectId equals project.Id
                      join workspace in context.Workspaces on project.WorkspaceId equals workspace.Id
                      join tenant in context.Tenants on activity.TenantId equals tenant.Id
                      where activity.Id == eventId && activity.DeletedAt == null && activity.GroupId == null && activity.WorkspaceId == null &&
                          (activity.Status == EventStatus.Published || activity.Status == EventStatus.Completed) &&
                          project.DeletedAt == null && project.Status != ProjectStatus.Suspended &&
                          project.Status != ProjectStatus.Archived && project.Status != ProjectStatus.Deleted &&
                          workspace.Status == WorkspaceStatus.Active && workspace.DeletedAt == null &&
                          (project.GroupId == null || context.Groups.Any(parentGroup => parentGroup.Id == project.GroupId &&
                              parentGroup.WorkspaceId == workspace.Id && parentGroup.Status == GroupStatus.Active && parentGroup.DeletedAt == null)) &&
                          tenant.Id == tenantId && tenant.Status == TenantStatus.Active && tenant.DeletedAt == null
                      select new PublicParent(activity.Title, project.Id, project.WorkspaceId,
                          activity.Status == EventStatus.Published && project.Status != ProjectStatus.Completed))
            .SingleOrDefaultAsync(token);
    }

    private async Task<AqPublicSurveyTarget?> FindTargetAsync(
        PublicCodeBinding code, AqPublicSession? session, bool lockIdentity, CancellationToken token)
    {
        if (!await IsCodeCurrentAsync(code.Id, token))
        {
            return null;
        }

        var picked = await (from target in context.Set<SurveyTarget>().AsNoTracking()
                            join campaign in context.Set<SurveyCampaign>() on target.CampaignId equals campaign.Id
                            join settings in context.Set<EventFeedbackSettings>() on target.EventId equals settings.EventId
                            join schema in context.Set<PublishedSurveySchema>().Include(row => row.Fields)
                                .ThenInclude(link => link.FormFieldDefinition) on target.PublishedSchemaId equals schema.Id
                            join definition in context.Set<FormDefinition>() on target.FormDefinitionId equals definition.Id
                            join presentation in context.Set<Presentation>() on target.PresentationId equals presentation.Id
                            where target.Id == code.SurveyTargetId && target.EventId == campaign.EventId &&
                                target.EventId == presentation.EventId && schema.SurveyTargetId == target.Id &&
                                schema.FormDefinitionId == target.FormDefinitionId &&
                                target.FormDefinitionId == campaign.FormDefinitionId &&
                                campaign.IsPublished && definition.DeletedAt == null && definition.Purpose == FormPurpose.Survey
                            select new PublicTargetRow(target, campaign, settings, schema, definition, presentation.ProjectId))
            .SingleOrDefaultAsync(token);
        if (picked is null)
        {
            return null;
        }
        var parent = await FindParentAsync(picked.Target.EventId, token);
        if (parent is null || picked.ProjectId != parent.ProjectId)
        {
            return null;
        }

        var fields = picked.Schema.Fields.OrderBy(binding => binding.SortOrder)
            .Select(binding => binding.FormFieldDefinition).ToArray();
        if (fields.Any(field => field is null ||
            field.FormVersionId != picked.Schema.FormVersionId &&
            field.TargetQuestionSetVersionId != picked.Schema.TargetQuestionSetVersionId))
        {
            return null;
        }
        var accountEligible = await IsAccountEligibleAsync(session, lockIdentity, token);
        return new AqPublicSurveyTarget(picked.Target, picked.Schema, picked.Settings, picked.Campaign,
            picked.Definition, fields.OfType<FormFieldDefinition>().ToArray(), accountEligible, parent.CanReceive);
    }

    private async Task<bool> IsAccountEligibleAsync(AqPublicSession? account, bool lockIdentity, CancellationToken token)
    {
        if (account is null)
        {
            return false;
        }

        if (lockIdentity)
        {
            var tenantId = currentTenant.TenantId;
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT 1 FROM sessions AS session
                JOIN users AS account ON account."Id" = session."UserId"
                JOIN tenant_users AS member ON member."UserId" = account."Id"
                WHERE session."Id" = {account.SessionId} AND account."Id" = {account.UserId}
                    AND member."TenantId" = {tenantId}
                FOR SHARE OF session, account, member
                """, token);
        }

        var now = clock.UtcNow;
        return await (from session in context.Sessions.AsNoTracking()
                      join user in context.Users on session.UserId equals user.Id
                      join member in context.TenantUsers on user.Id equals member.UserId
                      where session.Id == account.SessionId && user.Id == account.UserId &&
                            session.RevokedAt == null && session.ExpiresAt > now &&
                            user.Status == UserStatus.Active && user.DeletedAt == null &&
                            member.Status == TenantUserStatus.Active
                      select user.Id).AnyAsync(token);
    }

    private async Task LockSubmissionAuthorityAsync(PublicCodeBinding code, CancellationToken token)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM survey_public_codes AS code
            JOIN survey_targets AS target
                ON target."Id" = code."SurveyTargetId" AND target."TenantId" = code."TenantId"
            JOIN survey_campaigns AS campaign
                ON campaign."Id" = target."CampaignId" AND campaign."TenantId" = code."TenantId"
            JOIN presentations AS presentation
                ON presentation."Id" = target."PresentationId" AND presentation."TenantId" = code."TenantId"
            JOIN form_definitions AS form
                ON form."Id" = target."FormDefinitionId" AND form."TenantId" = code."TenantId"
            JOIN event_feedback_settings AS settings
                ON settings."EventId" = target."EventId" AND settings."TenantId" = code."TenantId"
            JOIN activity_events AS activity
                ON activity."Id" = target."EventId" AND activity."TenantId" = code."TenantId"
            JOIN projects AS project
                ON project."Id" = activity."ProjectId" AND project."TenantId" = code."TenantId"
            JOIN workspaces AS workspace
                ON workspace."Id" = project."WorkspaceId" AND workspace."TenantId" = code."TenantId"
            JOIN tenants AS tenant ON tenant."Id" = code."TenantId"
            WHERE code."Id" = {code.Id} AND code."TenantId" = {code.TenantId}
            FOR SHARE OF code, target, campaign, presentation, form, settings, activity, project, workspace, tenant
            """, token);

        // A Project may have a Group parent. The locked Project row keeps
        // that optional reference stable, and the Group cannot close during
        // the admission validated below.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM survey_targets AS target
            JOIN activity_events AS activity
                ON activity."Id" = target."EventId" AND activity."TenantId" = target."TenantId"
            JOIN projects AS project
                ON project."Id" = activity."ProjectId" AND project."TenantId" = target."TenantId"
            JOIN groups AS parent_group
                ON parent_group."Id" = project."GroupId" AND parent_group."TenantId" = target."TenantId"
            WHERE target."Id" = {code.SurveyTargetId} AND target."TenantId" = {code.TenantId}
            FOR SHARE OF parent_group
            """, token);
    }

    private async Task<bool> TryLockKeyAsync(Guid targetId, string keyDigest, CancellationToken token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"aq-public:{targetId:D}:{keyDigest}"));
        var key = BinaryPrimitives.ReadInt64BigEndian(hash);
        return await context.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({key}) AS \"Value\"")
            .SingleAsync(token);
    }

    private sealed record PublicCodeBinding(Guid Id, Guid TenantId, Guid? EventId, Guid? SurveyTargetId);
    private sealed record PublicParent(string EventTitle, Guid ProjectId, Guid WorkspaceId, bool CanReceive);
    private sealed record PublicTargetRow(
        SurveyTarget Target, SurveyCampaign Campaign, EventFeedbackSettings Settings, PublishedSurveySchema Schema,
        FormDefinition Definition, Guid ProjectId);

    private void PersistSubmission(
        AqPublicSurveyTarget target, string keyDigest, SubmissionIdempotency? previous,
        AqPublicWritePlan plan)
    {
        var submission = FormSubmission.SubmitSurvey(
            target.Definition, target.Target, target.Schema, plan.Answers, plan.SubmittedAt);
        var identity = SurveyRespondentIdentity.Create(
            submission, target.Target, target.Settings, plan.RespondentName, plan.AccountUserId is not null,
            plan.AccountUserId, plan.SubmittedAt);
        var receipt = SubmissionIdempotency.Record(
            submission, keyDigest, plan.IntentDigest, JsonSerializer.Serialize(new AqPublicReceipt(plan.RequestId)));

        context.Add(submission);
        context.AuditLogs.Add(new AuditLog
        {
            TenantId = target.Target.TenantId,
            Action = "AqPublicSurveySubmitted",
            EntityType = nameof(FormSubmission),
            EntityId = submission.Id,
            CreatedAt = plan.SubmittedAt
        });
        if (identity is not null)
        {
            context.Add(identity);
        }
        if (previous is not null)
        {
            context.Remove(previous);
        }
        context.Add(receipt);
    }
}
