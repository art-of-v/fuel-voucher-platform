using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Admin.GetDashboard;
using FuelFlow.Features.Admin.GetReconciliation;
using FuelFlow.Features.Auth.AdminUser.GetAdminUsers;
using FuelFlow.Features.Auth.GenerateChallenge;
using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Auth.Refresh;
using FuelFlow.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.Features.Auth.SendCode.Services;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Features.Auth.VerifyChallenge;
using FuelFlow.Features.Contracts.GetAdminContracts;
using FuelFlow.Features.Contracts.GetSignedContracts;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Notifications.GetNotifications;
using FuelFlow.Features.Notifications.MarkNotificationRead;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.DeleteOrder;
using FuelFlow.Features.Orders.GetAdminOrderById;
using FuelFlow.Features.Orders.GetAdminOrders;
using FuelFlow.Features.Orders.GetAdminPurchases;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.SimulatePayment;
using FuelFlow.Features.Orders.UpdateMonobankInfo;
using FuelFlow.Features.Orders.UpdateOrderStatus;
using FuelFlow.Features.Referral.CreateReferralCode;
using FuelFlow.Features.Referral.RedeemReferralCode;
using FuelFlow.Features.Stations.CreateFuelType;
using FuelFlow.Features.Stations.CreatePackage;
using FuelFlow.Features.Stations.CreateStation;
using FuelFlow.Features.Stations.DeleteFuelType;
using FuelFlow.Features.Stations.DeletePackage;
using FuelFlow.Features.Stations.DeleteStation;
using FuelFlow.Features.Stations.GetAdminFuelTypeById;
using FuelFlow.Features.Stations.GetAdminFuelTypes;
using FuelFlow.Features.Stations.GetAdminPackages;
using FuelFlow.Features.Stations.GetAdminPackagesByStation;
using FuelFlow.Features.Stations.GetAdminStationById;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Providers.GetProviderById;
using FuelFlow.Features.Providers.GetProviderHistory;
using FuelFlow.Features.Providers.GetProviders;
using FuelFlow.Features.Stations.GetAdminStations;
using FuelFlow.Features.Stations.GetPackageSuggestions;
using FuelFlow.Features.Stations.GetPublicPackages;
using FuelFlow.Features.Stations.GetPublicPackagesByStation;
using FuelFlow.Features.Stations.GetPublicStationNodes;
using FuelFlow.Features.Stations.GetPublicStationNodesByStation;
using FuelFlow.Features.Stations.GetPublicStations;
using FuelFlow.Features.Stations.UpdateFuelType;
using FuelFlow.Features.Stations.UpdatePackage;
using FuelFlow.Features.Stations.UpdateStation;
using FuelFlow.Features.Sync.GetSync;
using FuelFlow.Features.Users.UpdateUser;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.DeleteVoucher;
using FuelFlow.Features.Vouchers.GetAdminVoucherById;
using FuelFlow.Features.Vouchers.GetAdminVouchers;
using FuelFlow.Features.Vouchers.GetFuelVouchers;
using FuelFlow.Features.Vouchers.GetImportBatches;
using FuelFlow.Features.Vouchers.GetInventory;
using FuelFlow.Features.Vouchers.GetQrCodes;
using FuelFlow.Features.Vouchers.GetUserVouchers;
using FuelFlow.Features.Vouchers.GetVoucherVerification;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.MarkVoucherAsUsed;
using FuelFlow.Features.Vouchers.RestoreVoucher;
using FuelFlow.Features.Vouchers.UpdateVoucher;
using FuelFlow.Features.Settings;
using FuelFlow.Middleware;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.SharedKernel.Services;
using Scrutor;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FuelFlow.API.Extensions;

internal static class ServiceSetup
{
    internal static IServiceCollection AddFeatureServices(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<TwilioOptions>(config.GetSection(TwilioOptions.SectionName));
        services.Configure<MonobankOptions>(config.GetSection(MonobankOptions.SectionName));
        services.Configure<DeviceAuthOptions>(config.GetSection(DeviceAuthOptions.SectionName));
        services.Configure<AuthOptions>(config.GetSection(AuthOptions.SectionName));
        services.Configure<AppVersionOptions>(config.GetSection(AppVersionOptions.SectionName));

        AddVoucherServices(services);
        AddOrderServices(services);
        AddAuthServices(services);
        AddSmsService(services, config);
        AddMonobankService(services, config);
        AddInfrastructureServices(services);
        AddUserServices(services);
        AddReferralServices(services);
        AddStationServices(services);
        AddProviderServices(services);
        AddContractServices(services);
        AddAdminServices(services);
        AddNotificationServices(services);
        AddBackgroundJobServices(services);

        services.Scan(scan => scan
            .FromAssembliesOf(typeof(ServiceSetup))
            .AddClasses(classes => classes.Where(c =>
                c.Name.EndsWith("CommandHandler") ||
                c.Name.EndsWith("QueryHandler") ||
                c.Name.EndsWith("EventHandler")), publicOnly: false)
            .UsingRegistrationStrategy(RegistrationStrategy.Skip)
            .AsSelf()
            .WithScopedLifetime());

        return services;
    }

    private static void AddVoucherServices(IServiceCollection services)
    {
        services.AddScoped<ImportVouchersCommandHandler>();
        // Singleton: one slot process-wide is the whole point.
        services.AddSingleton<ImportConcurrencyGuard>();
        services.AddScoped<IVoucherProviderParser, OkkoVoucherParser>();
        services.AddScoped<IVoucherProviderParser, WogVoucherParser>();
        services.AddTransient<IPdfRenderer, PdfRenderer>();
        services.AddTransient<IQrDecoder, QrDecoder>();
        services.AddTransient<IVoucherDetector, VoucherDetector>();
        services.AddTransient<IQrGenerator, QrGeneratorV2>();
        services.AddScoped<GetVouchersQueryHandler>();
        services.AddScoped<GetVoucherVerificationQueryHandler>();
        services.AddScoped<GetImportBatchesQueryHandler>();
        services.AddScoped<GetImportBatchByIdQueryHandler>();
        services.AddScoped<GetImportBatchVouchersQueryHandler>();
        services.AddScoped<GetUserVouchersCommandHandler>();
        services.AddScoped<GetInventoryCommandHandler>();
        services.AddScoped<MarkVoucherAsUsedCommandHandler>();
        services.AddScoped<RestoreVoucherCommandHandler>();
        services.AddScoped<GetAdminVouchersQueryHandler>();
        services.AddScoped<GetAdminVoucherByIdQueryHandler>();
        services.AddScoped<UpdateVoucherCommandHandler>();
        services.AddScoped<DeleteVoucherCommandHandler>();
        services.AddScoped<BulkActionVouchersCommandHandler>();
        services.AddScoped<GetFuelVouchersQueryHandler>();
        services.AddScoped<GetQrCodesQueryHandler>();
    }

    private static void AddOrderServices(IServiceCollection services)
    {
        services.AddScoped<CreateCheckoutCommandHandler>();
        services.AddScoped<BulkCheckoutCommandHandler>();
        services.AddScoped<GetUserPurchasesCommandHandler>();
        services.AddScoped<SimulatePaymentCommandHandler>();
        services.AddScoped<UpdateMonobankInfoCommandHandler>();
        services.AddScoped<GetAdminOrdersQueryHandler>();
        services.AddScoped<GetAdminOrderByIdQueryHandler>();
        services.AddScoped<GetAdminPurchasesQueryHandler>();
        services.AddScoped<UpdateOrderStatusCommandHandler>();
        services.AddScoped<DeleteOrderCommandHandler>();
        services.AddScoped<RefundOrderCommandHandler>();
    }

    private static void AddAuthServices(IServiceCollection services)
    {
        services.AddScoped<SendCodeCommandHandler>();
        services.AddScoped<VerifyCodeCommandHandler>();
        services.AddScoped<RefreshTokenCommandHandler>();
        services.AddScoped<RegisterDeviceCommandHandler>();
        services.AddScoped<GenerateChallengeCommandHandler>();
        services.AddScoped<VerifyChallengeCommandHandler>();
        services.AddScoped<LogoutDeviceCommandHandler>();
    }

    private static void AddSmsService(IServiceCollection services, IConfiguration config)
    {
        // Singleton: the daily spend ceiling is only a ceiling if every request shares one counter.
        services.AddSingleton<SmsBudgetGuard>();

        var devBypass = config.GetValue<bool>(AuthOptions.SectionName + ":DevBypass");
        if (devBypass)
        {
            services.AddScoped<ISmsService, FakeSmsService>();
            return;
        }

        if (HasTwilioConfiguration(config))
            services.AddScoped<ISmsService, TwilioSmsService>();
        else
            services.AddScoped<ISmsService, FakeSmsService>();
    }

    /// <summary>
    /// True when real Twilio credentials are present. Shared with the
    /// production startup guard in Program.cs: without Twilio the app
    /// silently falls back to FakeSmsService and OTP codes are never
    /// delivered to users.
    /// </summary>
    internal static bool HasTwilioConfiguration(IConfiguration config)
    {
        var twilioSection = config.GetSection("Twilio");
        return !string.IsNullOrWhiteSpace(twilioSection["AccountSid"])
            && !string.IsNullOrWhiteSpace(twilioSection["AuthToken"])
            && twilioSection["AccountSid"] != "your_production_account_sid_here";
    }

    private static void AddMonobankService(IServiceCollection services, IConfiguration config)
    {
        var monobankOptions = config.GetSection(MonobankOptions.SectionName).Get<MonobankOptions>();
        if (monobankOptions?.Enabled == true)
        {
            services.AddHttpClient<IMonobankClient, MonobankClient>();
        }
        else
        {
            services.AddScoped<IMonobankClient, MockMonobankClient>();
        }

        services.AddScoped<ProcessMonobankWebhookCommandHandler>();
        services.AddScoped<GetSyncCommandHandler>();
    }

    private static void AddInfrastructureServices(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IAsymmetricSignatureVerifier, AsymmetricSignatureVerifier>();
        services.AddScoped<IPhoneNumberService, PhoneNumberService>();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
    }

    private static void AddUserServices(IServiceCollection services)
    {
        services.AddScoped<UpdateUserCommandHandler>();
    }

    private static void AddReferralServices(IServiceCollection services)
    {
        services.AddScoped<CreateReferralCodeCommandHandler>();
        services.AddScoped<RedeemReferralCodeCommandHandler>();
    }

    private static void AddProviderServices(IServiceCollection services)
    {
        services.AddScoped<GetProvidersQueryHandler>();
        services.AddScoped<GetProviderByIdQueryHandler>();
        services.AddScoped<GetProviderHistoryQueryHandler>();
        services.AddScoped<ProviderEventService>();
    }

    private static void AddStationServices(IServiceCollection services)
    {
        services.AddScoped<GetAdminStationsQueryHandler>();
        services.AddScoped<GetAdminStationByIdQueryHandler>();
        services.AddScoped<CreateStationCommandHandler>();
        services.AddScoped<UpdateStationCommandHandler>();
        services.AddScoped<DeleteStationCommandHandler>();
        services.AddScoped<GetAdminFuelTypesQueryHandler>();
        services.AddScoped<GetAdminFuelTypeByIdQueryHandler>();
        services.AddScoped<CreateFuelTypeCommandHandler>();
        services.AddScoped<UpdateFuelTypeCommandHandler>();
        services.AddScoped<DeleteFuelTypeCommandHandler>();
        services.AddScoped<GetAdminPackagesQueryHandler>();
        services.AddScoped<GetAdminPackagesByStationQueryHandler>();
        services.AddScoped<GetPackageSuggestionsQueryHandler>();
        services.AddScoped<CreatePackageCommandHandler>();
        services.AddScoped<UpdatePackageCommandHandler>();
        services.AddScoped<DeletePackageCommandHandler>();
        services.AddScoped<GetPublicStationsQueryHandler>();
        services.AddScoped<GetPublicStationNodesQueryHandler>();
        services.AddScoped<GetPublicStationNodesByStationQueryHandler>();
        services.AddScoped<GetPublicPackagesQueryHandler>();
        services.AddScoped<GetPublicPackagesByStationQueryHandler>();
    }

    private static void AddContractServices(IServiceCollection services)
    {
        services.AddScoped<GetAdminContractsQueryHandler>();
        services.AddScoped<GetSignedContractsQueryHandler>();
    }

    private static void AddAdminServices(IServiceCollection services)
    {
        services.AddScoped<GetDashboardQueryHandler>();
        services.AddScoped<GetAdminUsersQueryHandler>();
        services.AddScoped<GetReconciliationQueryHandler>();
    }

    private static void AddNotificationServices(IServiceCollection services)
    {
        services.AddScoped<GetNotificationsQueryHandler>();
        services.AddScoped<MarkNotificationReadCommandHandler>();
    }

    private static void AddBackgroundJobServices(IServiceCollection services)
    {
        services.AddScoped<FulfillmentService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<RefundStatusSyncService>();
        services.AddScoped<RuntimeSettingsService>();
    }

    internal static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5001"];

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        return services;
    }

    internal static IServiceCollection AddSwaggerDocs(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new() { Title = "FuelFlow API", Version = "v1" });
            options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter your JWT token.\n\nExample: \"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...\""
            });
        });

        return services;
    }

    internal static WebApplication UseSwaggerDocs(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return app;

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "FuelFlow API v1");
            c.RoutePrefix = string.Empty;
        });

        return app;
    }
}
