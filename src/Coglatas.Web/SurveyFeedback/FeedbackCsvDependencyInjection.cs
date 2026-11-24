using Coglatas.Application.SurveyFeedback.Csv;
using Coglatas.Infrastructure.Persistence.SurveyFeedback;

namespace Coglatas.Web.SurveyFeedback;

public static class FeedbackCsvDependencyInjection
{
    public static void AddFeedbackCsv(this IServiceCollection services) =>
        services.AddScoped<FeedbackCsvService>()
            .AddScoped<IFeedbackCsvRepository, EfFeedbackCsvRepository>();
}
