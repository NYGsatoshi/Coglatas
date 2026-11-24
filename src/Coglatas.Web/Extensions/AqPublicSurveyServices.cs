using Coglatas.Application.Forms.PublicSurveys;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Security;

namespace Coglatas.Web.Extensions;

public static class AqPublicSurveyServices
{
    public static IServiceCollection AddAqPublicSurveys(this IServiceCollection services)
    {
        services.AddScoped<AqPublicSurveyService>();
        services.AddScoped<IAqPublicSurveyStore, EfAqPublicSurveyStore>();
        services.AddSingleton<IAqPublicProjectionCodec, AqPublicProjectionCodec>();
        return services;
    }
}
