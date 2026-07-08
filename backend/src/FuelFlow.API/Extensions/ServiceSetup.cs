using FuelFlow.Features.Admin.GetDashboard;
using FuelFlow.Features.Auth.GenerateChallenge;
using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Auth.Refresh;
using FuelFlow.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.Features.Auth.SendCode.Services;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Features.Auth.VerifyChallenge;
using FuelFlow.Features.Notifications.GetNotifications;
using FuelFlow.Features.Notifications.MarkNotificationRead;
using FuelFlow.Features.Referral.CreateReferralCode;
using FuelFlow.Features.Referral.RedeemReferralCode;
using FuelFlow.Features.Users.UpdateUser;
using FuelFlow.Middleware;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.SimulatePayment;
using FuelFlow.Features.Orders.UpdateMonobankInfo;
using FuelFlow.Features.Sync.GetSync;
using FuelFlow.Features.Vouchers.GetInventory;
using FuelFlow.Features.Vouchers.GetUserVouchers;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.MarkVoucherAsUsed;
using FuelFlow.Features.Vouchers.RestoreVoucher;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Services;
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

        AddVoucherServices(services);
        AddOrderServices(services);
        AddAuthServices(services);
        AddFakeSmsService(services, config);
        AddMonobankService(services, config);
        AddInfrastructureServices(services);
        AddUserServices(services);
        AddReferralServices(services);
        AddAdminServices(services);
        AddNotificationServices(services);

        return services;
    }

    private static void AddVoucherServices(IServiceCollection services)
    {
        services.AddScoped<ImportVouchersCommandHandler>();
        services.AddScoped<IVoucherProviderParser, OkkoVoucherParser>();
        services.AddScoped<IVoucherProviderParser, WogVoucherParser>();
        services.AddTransient<IPdfRenderer, PdfRenderer>();
        services.AddTransient<IQrDecoder, QrDecoder>();
        services.AddTransient<IVoucherDetector, VoucherDetector>();
        services.AddTransient<IQrGenerator, QrGeneratorV2>();
        services.AddScoped<GetVouchersQueryHandler>();
        services.AddScoped<GetUserVouchersCommandHandler>();
        services.AddScoped<GetInventoryCommandHandler>();
        services.AddScoped<MarkVoucherAsUsedCommandHandler>();
        services.AddScoped<RestoreVoucherCommandHandler>();
    }

    private static void AddOrderServices(IServiceCollection services)
    {
        services.AddScoped<CreateCheckoutCommandHandler>();
        services.AddScoped<GetUserPurchasesCommandHandler>();
        services.AddScoped<SimulatePaymentCommandHandler>();
        services.AddScoped<UpdateMonobankInfoCommandHandler>();
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
        if (config["SMS_PROVIDER"] == "dev")
        {
            services.AddScoped<ISmsService, FakeSmsService>();
            return;
        }

        var twilioSection = config.GetSection("Twilio");
        var hasTwilio = !string.IsNullOrWhiteSpace(twilioSection["AccountSid"])
                     && !string.IsNullOrWhiteSpace(twilioSection["AuthToken"]);

        if (hasTwilio)
            services.AddScoped<ISmsService, TwilioSmsService>();
        else
            services.AddScoped<ISmsService, FakeSmsService>();
    }

    private static void AddFakeSmsService(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<ISmsService, FakeSmsService>();
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
        services.AddSingleton<ICacheService, InMemoryCacheService>();
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

    private static void AddAdminServices(IServiceCollection services)
    {
        services.AddScoped<GetDashboardQueryHandler>();
    }

    private static void AddNotificationServices(IServiceCollection services)
    {
        services.AddScoped<GetNotificationsQueryHandler>();
        services.AddScoped<MarkNotificationReadCommandHandler>();
    }

    internal static IServiceCollection AddCorsPolicy(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
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
