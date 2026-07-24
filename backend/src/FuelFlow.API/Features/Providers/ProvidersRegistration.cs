using System.ComponentModel.DataAnnotations.Schema;

namespace FuelFlow.API.Features.Providers
{
    internal static class ServiceRegistration
    {
        internal static IServiceCollection AddProviderServices(this IServiceCollection services)
        {
            services.AddScoped<IProviderRepository, ProviderRepository>();
            services.AddScoped<IProviderService, ProviderService>();

            return services;
        }
    }
}