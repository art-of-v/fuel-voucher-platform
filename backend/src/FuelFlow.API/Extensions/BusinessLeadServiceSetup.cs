using FuelFlow.Features.BusinessLeads;
using FuelFlow.SharedKernel.Options;

namespace FuelFlow.API.Extensions;

internal static class BusinessLeadServiceSetup
{
    internal static IServiceCollection AddBusinessLeadServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<BusinessLeadMailOptions>(
            configuration.GetSection(BusinessLeadMailOptions.SectionName));
        services.AddScoped<IBusinessLeadMailSender, BusinessLeadMailSender>();
        return services;
    }
}
