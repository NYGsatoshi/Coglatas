using Coglatas.Application.SurveyFeedback;
using Coglatas.Infrastructure.Persistence.SurveyFeedback;

namespace Coglatas.Web.SurveyFeedback;

public static class SurveyFeedbackDependencyInjection
{
    public static IServiceCollection AddSurveyFeedback(this IServiceCollection services) =>
        services.AddScoped<SurveyFeedbackAuthorization>()
            .AddScoped<SurveyFeedbackService>()
            .AddScoped<FeedbackInvitationService>()
            .AddScoped<FeedbackModerationService>()
            .AddScoped<IFeedbackLifecycleRepository, EfFeedbackLifecycleRepository>()
            .AddScoped<ISurveyFeedbackInvalidation, SurveyFeedbackInvalidation>()
            .AddScoped<ISurveyFeedbackRepository, EfSurveyFeedbackRepository>()
            .AddSingleton<ISurveyFeedbackCursor, ProtectedSurveyFeedbackCursor>();
}
