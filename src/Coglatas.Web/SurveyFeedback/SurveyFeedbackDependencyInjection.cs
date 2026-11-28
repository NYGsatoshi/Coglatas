using Coglatas.Application.SurveyFeedback;
using Coglatas.Infrastructure.Persistence.SurveyFeedback;

namespace Coglatas.Web.SurveyFeedback;

public static class SurveyFeedbackDependencyInjection
{
    public static void AddSurveyFeedback(this IServiceCollection services) =>
        services.AddScoped<SurveyFeedbackAuthorization>()
            .AddScoped<SurveyFeedbackService>()
            .AddScoped<SurveyFeedbackComparisonService>()
            .AddScoped<ISurveyFeedbackComparisonRepository, EfSurveyFeedbackComparisonRepository>()
            .AddScoped<SurveyFeedbackInboxService>()
            .AddScoped<ISurveyFeedbackInboxRepository, EfSurveyFeedbackInboxRepository>()
            .AddScoped<FeedbackInvitationService>()
            .AddScoped<FeedbackEventInvitationPolicyManagement>()
            .AddScoped<FeedbackModerationService>()
            .AddScoped<FeedbackNotesService>()
            .AddScoped<IFeedbackLifecycleRepository, EfFeedbackLifecycleRepository>()
            .AddScoped<ISurveyFeedbackInvalidation, SurveyFeedbackInvalidation>()
            .AddScoped<ISurveyFeedbackRepository, EfSurveyFeedbackRepository>()
            .AddSingleton<ISurveyFeedbackCursor, ProtectedSurveyFeedbackCursor>();
}
