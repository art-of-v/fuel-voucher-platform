using FuelFlow.Features.Purchases.GetAdminPurchases;
using FuelFlow.Features.Providers.Repositories;
using FuelFlow.Features.Providers.Services;
using FuelFlow.Features.Stations.GetAdminStations;
using FuelFlow.Features.Vouchers.GetAdminVouchers;
using FuelFlow.SharedKernel.Domain;
using Microsoft.OpenApi;

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