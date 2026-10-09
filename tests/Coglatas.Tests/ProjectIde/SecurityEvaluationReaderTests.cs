using System.Text.Json;
using Coglatas.Application;
using Coglatas.Application.ProjectIde.Evaluations;
using Coglatas.Domain.ProjectIde;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.ProjectIde;

public sealed class SecurityEvaluationReaderTests
{
    [Fact]
    public async Task AuthorizedProjectionRetainsSafeMetadataAndAnIndependentCurrentBinding()
    {
        var binding = SecurityEvaluationTestData.Binding();
        var record = Record(binding);
        var store = new ReadStore(record);
        var provider = new ContextProvider(binding);
        using var cancellation = new CancellationTokenSource();
        var result = Assert.IsType<SecurityEvaluationReadModel>(await new SecurityEvaluationReader(store, provider,
            new()).FindAsync(record.ProjectId, record.EvaluationId, cancellation.Token));
        Assert.Equal(record.EvaluationId, result.EvaluationId);
        Assert.Equal(record.TenantId, result.TenantId);
        Assert.Equal(record.ProjectId, result.ProjectId);
        Assert.Equal(record.BindingDigest, result.BindingDigest);
        Assert.Equal(record.SchemaVersion, result.SchemaVersion);
        Assert.Equal(record.Identity, result.Identity);
        Assert.Equal(record.EnforcementMode, result.EnforcementMode);
        Assert.Equal(record.Status, result.Status);
        Assert.Equal(record.Outcome, result.Outcome);
        Assert.Equal(record.ReasonCode, result.ReasonCode);
        Assert.Equal(record.CreatedAtUtc, result.CreatedAtUtc);
        Assert.Equal(record.TerminalAtUtc, result.TerminalAtUtc);
        Assert.Equal(record.Rules, result.Rules);
        Assert.Equal(SecurityEvaluationFreshness.Current, result.Freshness);
        Assert.False(result.IsAuthoritative);
        Assert.Equal(2, store.Reads);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(record.ProjectId, provider.LastProjectId);
        Assert.Equal(record.Identity.Resource, provider.LastHistoricalResource);
        Assert.Equal(cancellation.Token, provider.LastCancellationToken);
        Assert.Throws<NotSupportedException>(() => ((IList<SecurityRuleResult>)result.Rules).Clear());
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("subject-tenant")]
    [InlineData("project")]
    [InlineData("branch")]
    [InlineData("revision")]
    [InlineData("candidate")]
    [InlineData("input")]
    [InlineData("policy")]
    [InlineData("compiler")]
    [InlineData("host-policy")]
    [InlineData("host-compiler")]
    [InlineData("extension")]
    public async Task KnownChangesCannotMakeHistoricalEvidenceCurrent(string changed)
    {
        var binding = SecurityEvaluationTestData.Binding();
        var request = binding.Request;
        var original = request.Resource.Context;
        var branch = new BranchRef(changed == "tenant" ? TenantId.New() : original.Branch.TenantId,
            changed == "project" ? ProjectId.New() : original.Branch.ProjectId,
            changed == "branch" ? BranchId.New() : original.Branch.BranchId);
        var revision = new RevisionRef(branch, changed == "revision" ? RevisionId.New() : original.CommittedRevision!.RevisionId);
        var context = changed == "candidate"
            ? SourceRevisionContext.Candidate(new(ProposalId.New(), CandidateRevisionId.New(), revision, revision))
            : SourceRevisionContext.Committed(revision);
        if (changed == "extension")
            context = SourceRevisionContext.Parse(SourceJson.Parse(context.Data.CanonicalText.TrimEnd('}') + ",\"private\":\"private-context-canary\"}"));
        var subject = changed == "subject-tenant" ? new SecuritySubjectRef(TenantId.New(), request.Subject.UserId) : request.Subject;
        var currentRequest = new SecurityEvaluationRequest(Guid.NewGuid(), subject, request.Operation,
            new(context, changed == "input" ? ContentDigest.Compute("test", [1]) : request.Resource.InputDigest),
            request.EnforcementMode, request.Source,
            changed == "policy" ? SecurityEvaluationTestData.Policy("{\"rules\":{}}").Snapshot : request.Policy,
            changed == "compiler" ? new("test-host/2", "different") : request.Compiler);
        var current = SecurityBinding.Create(currentRequest, new(binding.Evidence.Source,
            changed == "host-policy" ? SecurityEvaluationTestData.Policy("{\"rules\":{}}") : binding.Evidence.Policy,
            changed == "host-compiler" ? new("test-host/2", "different") : binding.Evidence.Compiler));
        var record = Record(binding);
        var diagnostics = new SecurityEvaluationDiagnostics();
        var result = Assert.IsType<SecurityEvaluationReadModel>(await new SecurityEvaluationReader(new ReadStore(record),
            new ContextProvider(current), diagnostics).FindAsync(record.ProjectId, record.EvaluationId));
        Assert.Equal(SecurityEvaluationFreshness.Stale, result.Freshness);
        Assert.Equal(1, diagnostics.Snapshot().BindingMismatch);
        Assert.DoesNotContain("private-context-canary", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("proposal")]
    [InlineData("candidate")]
    [InlineData("base")]
    [InlineData("head")]
    public async Task CandidateFreshnessIncludesProposalBaseAndCapturedHead(string changed)
    {
        var request = SecurityEvaluationTestData.Request();
        var revision = request.Resource.Context.CommittedRevision!;
        var proposal = new ProposalContext(ProposalId.New(), CandidateRevisionId.New(), revision, revision);
        var historical = SourceRevisionContext.Candidate(proposal);
        var source = ProjectSource.Create(historical, request.Source!.Documents);
        var binding = SecurityBinding.Create(new(Guid.NewGuid(), request.Subject, request.Operation, new(historical, source.Digest),
            request.EnforcementMode, source, request.Policy, request.Compiler),
            new(source, SecurityEvaluationTestData.Policy(), request.Compiler));
        var currentContext = SourceRevisionContext.Candidate(new(
            changed == "proposal" ? ProposalId.New() : proposal.ProposalId,
            changed == "candidate" ? CandidateRevisionId.New() : proposal.CandidateRevisionId,
            changed == "base" ? new(revision.Branch, RevisionId.New()) : proposal.BaseRevision,
            changed == "head" ? new(revision.Branch, RevisionId.New()) : proposal.CapturedTargetHead));
        var currentSource = ProjectSource.Create(currentContext, source.Documents);
        var current = SecurityBinding.Create(new(Guid.NewGuid(), request.Subject, request.Operation,
            new(currentContext, currentSource.Digest), request.EnforcementMode, currentSource, request.Policy, request.Compiler),
            new(currentSource, SecurityEvaluationTestData.Policy(), request.Compiler));
        var record = Record(binding);
        var result = Assert.IsType<SecurityEvaluationReadModel>(await new SecurityEvaluationReader(new ReadStore(record),
            new ContextProvider(current), new()).FindAsync(record.ProjectId, record.EvaluationId));
        Assert.Equal(SecurityEvaluationFreshness.Stale, result.Freshness);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("current-source")]
    [InlineData("current-policy")]
    [InlineData("current-compiler")]
    [InlineData("saved-source")]
    [InlineData("saved-policy")]
    [InlineData("saved-compiler")]
    public async Task MissingEvidenceIsUnverifiedEvenWhenNullsWouldMatch(string missing)
    {
        var binding = SecurityEvaluationTestData.Binding();
        var record = Record(binding);
        var current = missing == "provider" ? null : SecurityBinding.Create(binding.Request,
            new(missing == "current-source" ? null : binding.Evidence.Source,
                missing == "current-policy" ? null : binding.Evidence.Policy,
                missing == "current-compiler" ? null : binding.Evidence.Compiler));
        record = record with { Identity = record.Identity with
        {
            ExpectedSource = missing == "saved-source" ? null : record.Identity.ExpectedSource,
            ExpectedPolicy = missing == "saved-policy" ? null : record.Identity.ExpectedPolicy,
            ExpectedCompiler = missing == "saved-compiler" ? null : record.Identity.ExpectedCompiler
        } };
        var result = Assert.IsType<SecurityEvaluationReadModel>(await new SecurityEvaluationReader(new ReadStore(record),
            new ContextProvider(current), new()).FindAsync(record.ProjectId, record.EvaluationId));
        Assert.Equal(SecurityEvaluationFreshness.Unverified, result.Freshness);
    }

    [Fact]
    public async Task CurrentnessIgnoresHistoricalActorButNeverReplacesCurrentAuthorization()
    {
        var binding = SecurityEvaluationTestData.Binding();
        var request = binding.Request;
        var current = SecurityBinding.Create(new(Guid.NewGuid(), new(request.Subject.TenantId, Guid.NewGuid()),
            request.Operation, request.Resource, request.EnforcementMode, request.Source, request.Policy, request.Compiler), binding.Evidence);
        Assert.NotEqual(binding.Digest, current.Digest);
        var record = Record(binding);
        var result = Assert.IsType<SecurityEvaluationReadModel>(await new SecurityEvaluationReader(new ReadStore(record),
            new ContextProvider(current), new()).FindAsync(record.ProjectId, record.EvaluationId));
        Assert.Equal(SecurityEvaluationFreshness.Current, result.Freshness);
        Assert.Equal(request.Subject.UserId, result.Identity.SubjectUserId);
        Assert.False(result.IsAuthoritative);
    }

    [Fact]
    public async Task UnauthorizedKnownIdDoesNotInvokeContextProviderAndRevocationDuringResolutionReturnsNothing()
    {
        var record = Record(SecurityEvaluationTestData.Binding());
        var store = new ReadStore(null);
        var provider = new ContextProvider(null);
        var reader = new SecurityEvaluationReader(store, provider, new());
        Assert.Null(await reader.FindAsync(record.ProjectId, record.EvaluationId));
        Assert.Equal(0, provider.Calls);
        store.AvailableRecord = record;
        provider.BeforeReturn = () => store.AvailableRecord = null;
        Assert.Null(await reader.FindAsync(record.ProjectId, record.EvaluationId));
        Assert.Equal(1, provider.Calls);
        Assert.Equal(3, store.Reads);
    }

    [Fact]
    public async Task ApplicationCompositionExposesUnavailableCurrentnessWithoutInventingHostEvidence()
    {
        var services = new ServiceCollection().AddApplication();
        var record = Record(SecurityEvaluationTestData.Binding());
        services.AddScoped<ISecurityEvaluationStore>(_ => new ReadStore(record));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var result = Assert.IsType<SecurityEvaluationReadModel>(await scope.ServiceProvider.GetRequiredService<ISecurityEvaluationReader>()
            .FindAsync(record.ProjectId, record.EvaluationId));
        Assert.Equal(SecurityEvaluationFreshness.Unverified, result.Freshness);
        Assert.Same(provider.GetRequiredService<SecurityEvaluationDiagnostics>(),
            scope.ServiceProvider.GetRequiredService<SecurityEvaluationDiagnostics>());
    }

    private static SecurityEvaluationRecord Record(SecurityBinding binding) => new(binding.Request.EvaluationId,
        binding.Request.Subject.TenantId.Value, binding.Request.Resource.Context.Branch.ProjectId.Value,
        binding.Digest.Value, 1, SecurityEvaluationIdentitySnapshot.Capture(binding), SecurityEnforcementMode.Shadow,
        SecurityEvaluationStatus.Completed, SecurityDecisionOutcome.Allow, SecurityReasonCode.BindingsVerified,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1),
        [new("fixture", SecurityEvaluationStatus.Completed, SecurityDecisionOutcome.Allow, SecurityReasonCode.BindingsVerified)]);

    private sealed class ContextProvider(SecurityBinding? binding) : ISecurityCurrentContextProvider
    {
        public int Calls { get; private set; }
        public Action? BeforeReturn { get; set; }
        public Guid LastProjectId { get; private set; }
        public SecuritySourceIdentitySnapshot? LastHistoricalResource { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public ValueTask<SecurityBinding?> ResolveAsync(Guid projectId, SecuritySourceIdentitySnapshot historicalResource,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastProjectId = projectId;
            LastHistoricalResource = historicalResource;
            LastCancellationToken = cancellationToken;
            Calls++;
            BeforeReturn?.Invoke();
            return ValueTask.FromResult(binding);
        }
    }

    private sealed class ReadStore(SecurityEvaluationRecord? record) : ISecurityEvaluationStore
    {
        public SecurityEvaluationRecord? AvailableRecord { get; set; } = record;
        public int Reads { get; private set; }
        public Task<SecurityEvaluationRecord?> FindAsync(Guid projectId, Guid evaluationId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(AvailableRecord);
        }
        public Task<bool> CreatePendingAsync(SecurityBinding binding, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Reads must not write.");
        public Task<SecurityTerminalizationResult> TerminalizeAsync(SecurityBinding binding, SecurityDecision decision,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Reads must not terminalize.");
    }
}
