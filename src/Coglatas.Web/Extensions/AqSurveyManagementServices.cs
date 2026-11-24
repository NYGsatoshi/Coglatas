using Coglatas.Application.Forms.SurveyManagement;
using Coglatas.Infrastructure.Persistence;

namespace Coglatas.Web.Extensions;

public static class AqSurveyManagementServices
{
    public static void AddAqSurveyManagement(this IServiceCollection services)
    {
        services.AddScoped<IAqSurveyManagementStore, AqSurveyManagementRepository>();
        services.AddScoped<AqSurveyManagementService>();
    }
}
