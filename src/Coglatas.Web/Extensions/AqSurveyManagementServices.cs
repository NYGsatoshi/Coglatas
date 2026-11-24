using Coglatas.Application.Forms.SurveyManagement;
using Coglatas.Infrastructure.Persistence;

namespace Coglatas.Web.Extensions;

public static class AqSurveyManagementServices
{
    public static IServiceCollection AddAqSurveyManagement(this IServiceCollection services)
    {
        services.AddScoped<IAqSurveyManagementStore, AqSurveyManagementRepository>();
        services.AddScoped<AqSurveyManagementService>();
        return services;
    }
}
