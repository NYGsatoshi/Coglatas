using System.Reflection;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Projects;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
public sealed class AqInvalidationAuthorizationTests
{
    [Theory]
    [InlineData(DeliveryCase.DirectReviewer, true)]
    [InlineData(DeliveryCase.DirectPresenter, true)]
    [InlineData(DeliveryCase.Managed, true)]
    [InlineData(DeliveryCase.ValidInvitation, true)]
    [InlineData(DeliveryCase.InviterNoLongerAssigned, false)]
    [InlineData(DeliveryCase.InviterMembershipSuspended, false)]
    [InlineData(DeliveryCase.NoAuthority, false)]
    [InlineData(DeliveryCase.ResultsStopped, false)]
    [InlineData(DeliveryCase.ProjectUnavailable, false)]
    [InlineData(DeliveryCase.RecipientMembershipSuspended, false)]
    public async Task DeliveryPredicateMatchesCurrentFeedbackReadAuthority(DeliveryCase scenario, bool permitted)
    {
        var tenant = new Tenant { Status = TenantStatus.Active };
        var user = new User { Status = UserStatus.Active };
        var issuer = new User { Status = UserStatus.Active };
        var actor = new FeedbackActor(tenant.Id, user.Id);
        var now = new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);
        var scope = new PresentationFeedbackScope(tenant.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "Synthetic presentation", now, now.AddHours(1), null, scenario == DeliveryCase.ResultsStopped);
        var calls = new List<string>();
        TenantUser? Membership(Guid key, Guid id) => key == tenant.Id && (id == user.Id || id == issuer.Id)
            ? new TenantUser
            {
                TenantId = tenant.Id,
                UserId = id,
                Tenant = tenant,
                User = id == user.Id ? user : issuer,
                Status = (id == user.Id && scenario == DeliveryCase.RecipientMembershipSuspended) ||
                    (id == issuer.Id && scenario == DeliveryCase.InviterMembershipSuspended)
                    ? TenantUserStatus.Suspended : TenantUserStatus.Active
            }
            : null;

        var tenants = Stub<ITenantRepository>((method, args) => method == nameof(ITenantRepository.GetCurrentTenantUserAsync)
            ? Task.FromResult(Membership((Guid)args[0]!, (Guid)args[1]!)) : Unexpected(method), calls);
        var projects = Stub<IProjectAuthorizationService>((method, args) => method == nameof(IProjectAuthorizationService.CanViewProject)
            ? Task.FromResult((Guid)args[1]! == scope.ProjectId && scenario != DeliveryCase.ProjectUnavailable) : Unexpected(method), calls);
        var permissions = Stub<ICapabilityGrantEvaluator>((method, args) => method == nameof(ICapabilityGrantEvaluator.HasActiveGrantAsync)
            ? Task.FromResult(((Guid)args[0]!, (string)args[2]!) switch
            {
                (var id, "feedback.read.managed") when id == user.Id => scenario == DeliveryCase.Managed,
                (var id, "feedback.invite.create") when id == issuer.Id => true,
                _ => false
            }) : Unexpected(method), calls);
        var repository = Stub<ISurveyFeedbackRepository>((method, args) => method switch
        {
            nameof(ISurveyFeedbackRepository.GetScopeAsync) => Task.FromResult<PresentationFeedbackScope?>(
                (Guid)args[0]! == tenant.Id && (Guid)args[1]! == scope.Id ? scope : null),
            nameof(ISurveyFeedbackRepository.GetAssignmentsAsync) => Task.FromResult((Guid)args[2]! == issuer.Id
                ? (scenario != DeliveryCase.InviterNoLongerAssigned, false)
                : (scenario is DeliveryCase.DirectPresenter,
                    scenario is DeliveryCase.DirectReviewer or DeliveryCase.ResultsStopped
                        or DeliveryCase.ProjectUnavailable or DeliveryCase.RecipientMembershipSuspended)),
            nameof(ISurveyFeedbackRepository.GetActiveGrantIssuersAsync) => Task.FromResult<IReadOnlyList<Guid>>(
                scenario is DeliveryCase.ValidInvitation or DeliveryCase.InviterNoLongerAssigned or DeliveryCase.InviterMembershipSuspended
                    ? [issuer.Id] : []),
            _ => Unexpected(method)
        }, calls);
        var auth = new SurveyFeedbackAuthorization(
            Stub<ICurrentTenant>((method, _) => Unexpected(method), calls), Stub<ICurrentUser>((method, _) => Unexpected(method), calls),
            tenants, Stub<IWorkspaceRepository>((method, _) => Unexpected(method), calls), projects, permissions, repository,
            Stub<IClock>((method, _) => method == "get_UtcNow" ? now : Unexpected(method), calls));
        var fullRead = await auth.IsCurrentMemberAsync(actor, CancellationToken.None)
            && await auth.ReadAccessAsync(actor, scope.Id, CancellationToken.None) is { CanRead: true };
        calls.Clear();

        Assert.Equal(permitted, fullRead);
        Assert.Equal(fullRead, await auth.CanReceiveInvalidationAsync(actor, scope.Id, CancellationToken.None));

        if (scenario is DeliveryCase.DirectPresenter or DeliveryCase.DirectReviewer)
        {
            Assert.Equal(1, calls.Count(method => method == nameof(ITenantRepository.GetCurrentTenantUserAsync)));
            Assert.DoesNotContain(nameof(ICapabilityGrantEvaluator.HasActiveGrantAsync), calls);
            Assert.DoesNotContain(nameof(ISurveyFeedbackRepository.GetActiveGrantIssuersAsync), calls);
        }
    }

    private static object Unexpected(string method) => throw new InvalidOperationException($"{method} must not be called.");

    private static T Stub<T>(Func<string, object?[], object?> use, List<string> calls) where T : class
    {
        var proxy = DispatchProxy.Create<T, CaptureDependency>();
        var dependency = (CaptureDependency)(object)proxy;
        dependency.Use = use;
        dependency.Calls = calls;
        return proxy;
    }

    public class CaptureDependency : DispatchProxy
    {
        public Func<string, object?[], object?> Use { get; set; } = null!;
        public List<string> Calls { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod!.Name;
            Calls.Add(method);
            return Use(method, args ?? []);
        }
    }

    public enum DeliveryCase
    {
        DirectReviewer,
        DirectPresenter,
        Managed,
        ValidInvitation,
        InviterNoLongerAssigned,
        InviterMembershipSuspended,
        NoAuthority,
        ResultsStopped,
        ProjectUnavailable,
        RecipientMembershipSuspended
    }
}
