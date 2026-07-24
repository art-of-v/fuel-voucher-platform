using FuelFlow.API.Features.Providers.Repositories;
using FuelFlow.API.Features.Providers.Services;

namespace FuelFlow.API.Features.Providers
{
    internal static class ServiceSetup
    {
        internal static IServiceCollection AddProviderServices(this IServiceCollection services)
        {
            services.AddScoped<IProviderRepository, ProviderRepository>();
            services.AddScoped<IProviderService, ProviderService>();

            return services;
        }
    }
}