using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using FuelFlow.Features.Auth.Refresh;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.Features.Auth.SendCode.Services;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Services;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.SimulatePayment;
using FuelFlow.Features.Orders.UpdateMonobankInfo;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.GetUserVouchers;
using FuelFlow.Features.Vouchers.GetInventory;
using FuelFlow.Features.Vouchers.MarkVoucherAsUsed;
using FuelFlow.Features.Vouchers.RestoreVoucher;
using FuelFlow.Features.Sync.GetSync;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.GenerateChallenge;
using FuelFlow.Features.Auth.VerifyChallenge;
using FuelFlow.Middleware;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;

try
{
    var builder = WebApplication.CreateBuilder(args);

    if (builder.Environment.IsEnvironment("Testing"))
    {
    }
    else
    {
        try
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateBootstrapLogger();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already frozen", StringComparison.OrdinalIgnoreCase))
        {
        }

        builder.Services.AddSerilog((services, configuration) => configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console());
    }

    builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
    builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
    builder.Services.PostConfigure<JwtOptions>(options =>
    {
        if (string.IsNullOrWhiteSpace(options.Secret))
            options.Secret = "test-secret-key-that-is-at-least-32-characters-long";

        if (string.IsNullOrWhiteSpace(options.Issuer))
            options.Issuer = "FuelFlow";

        if (string.IsNullOrWhiteSpace(options.Audience))
            options.Audience = "FuelFlow";
    });
    builder.Services.Configure<TwilioOptions>(builder.Configuration.GetSection(TwilioOptions.SectionName));
    builder.Services.Configure<MonobankOptions>(builder.Configuration.GetSection(MonobankOptions.SectionName));
    builder.Services.Configure<DeviceAuthOptions>(builder.Configuration.GetSection(DeviceAuthOptions.SectionName));

    builder.Services.AddScoped<IImportVouchersDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
    builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
    {
        var dbOptions = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        options.UseNpgsql(dbOptions.ConnectionString,
            b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));
    });

    builder.Services.AddScoped<ImportVouchersCommandHandler>();
    builder.Services.AddScoped<IVoucherProviderParser, OkkoVoucherParser>();
    builder.Services.AddScoped<IVoucherProviderParser, WogVoucherParser>();
    builder.Services.AddTransient<IPdfRenderer, PdfRenderer>();
    builder.Services.AddTransient<IQrDecoder, QrDecoder>();
    builder.Services.AddTransient<IVoucherDetector, VoucherDetector>();
    builder.Services.AddTransient<IQrGenerator, QrGenerator>();
    builder.Services.AddScoped<GetVouchersQueryHandler>();

    builder.Services.AddScoped<SendCodeCommandHandler>();
    builder.Services.AddScoped<VerifyCodeCommandHandler>();
    builder.Services.AddScoped<RefreshTokenCommandHandler>();

    builder.Services.AddScoped<RegisterDeviceCommandHandler>();
    builder.Services.AddScoped<GenerateChallengeCommandHandler>();
    builder.Services.AddScoped<VerifyChallengeCommandHandler>();
    builder.Services.AddScoped<FuelFlow.Features.Auth.Logout.LogoutDeviceCommandHandler>();

    builder.Services.AddSingleton<ICacheService, InMemoryCacheService>();

    builder.Services.AddScoped<IPhoneNumberService, PhoneNumberService>();

    var twilioSection = builder.Configuration.GetSection("Twilio");
    var hasTwilio = !string.IsNullOrWhiteSpace(twilioSection["AccountSid"])
                 && !string.IsNullOrWhiteSpace(twilioSection["AuthToken"]);

    if (hasTwilio)
    {
        builder.Services.AddScoped<ISmsService, TwilioSmsService>();
    }
    else
    {
        builder.Services.AddScoped<ISmsService, FakeSmsService>();
    }

    builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

    builder.Services.AddScoped<CreateCheckoutCommandHandler>();
    builder.Services.AddScoped<GetUserPurchasesCommandHandler>();
    builder.Services.AddScoped<SimulatePaymentCommandHandler>();
    builder.Services.AddScoped<UpdateMonobankInfoCommandHandler>();

    builder.Services.AddScoped<GetUserVouchersCommandHandler>();
    builder.Services.AddScoped<GetInventoryCommandHandler>();
    builder.Services.AddScoped<MarkVoucherAsUsedCommandHandler>();
    builder.Services.AddScoped<RestoreVoucherCommandHandler>();

    builder.Services.AddScoped<GetSyncCommandHandler>();

    builder.Services.AddScoped<ProcessMonobankWebhookCommandHandler>();

    var monobankOptions = builder.Configuration.GetSection(MonobankOptions.SectionName).Get<MonobankOptions>();
    var isTesting = builder.Environment.IsEnvironment("Testing");
    if (monobankOptions?.Enabled == true)
    {
        builder.Services.AddHttpClient<IMonobankClient, MonobankClient>();
        if (!isTesting) Log.Information("Monobank integration enabled: Using real MonobankClient");
    }
    else
    {
        builder.Services.AddScoped<IMonobankClient, MockMonobankClient>();
        if (!isTesting) Log.Warning("Monobank integration disabled: Using MockMonobankClient (development mode)");
    }

    var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
    if (string.IsNullOrWhiteSpace(jwtOptions.Secret))
    {
        jwtOptions.Secret = "test-secret-key-that-is-at-least-32-characters-long";
    }

    if (string.IsNullOrWhiteSpace(jwtOptions.Issuer))
    {
        jwtOptions.Issuer = "FuelFlow";
    }

    if (string.IsNullOrWhiteSpace(jwtOptions.Audience))
    {
        jwtOptions.Audience = "FuelFlow";
    }

    var isTestingEnvironment = builder.Environment.IsEnvironment("Testing");

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !isTestingEnvironment,
            ValidateAudience = !isTestingEnvironment,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ClockSkew = TimeSpan.Zero
        };
    });

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSwaggerGen(options =>
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
            Description = "Enter your JWT token (Swagger will add 'Bearer ' prefix automatically).\n\nExample: \"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...\""
        });
    });

    var app = builder.Build();

    if (app.Environment.IsDevelopment() || true)
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "FuelFlow API v1");
            c.RoutePrefix = string.Empty;
        });
    }

    app.UseCors();
    app.UseAuthentication();
    app.UseMiddleware<DeviceSignatureMiddleware>();
    app.UseAuthorization();
    app.MapControllers();

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
        .AllowAnonymous();

    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>();
        try
        {
            var dbContext = services.GetRequiredService<ApplicationDbContext>();
            var runMigrationsOnBoot = builder.Configuration.GetValue<bool>("RunMigrationsOnBoot");
            if (runMigrationsOnBoot)
            {
                logger.LogInformation("Applying pending migrations...");
                dbContext.Database.Migrate();
                logger.LogInformation("Database migrated successfully.");
            }
            else
            {
                logger.LogInformation("Skipping Database.Migrate() (RunMigrationsOnBoot=false). Apply migrations manually via 'dotnet ef database update' if needed.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while migrating the database.");
            throw;
        }
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    try
    {
        Log.CloseAndFlush();
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("already frozen", StringComparison.OrdinalIgnoreCase))
    {
    }
}

public partial class Program { }
