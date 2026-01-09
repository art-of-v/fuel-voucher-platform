using FuelFlow.Api.Infrastructure;
using FuelFlow.Api.Infrastructure.Services;
using FuelFlow.Api.Middleware;
using FuelFlow.Api.Features.Stations;
using FuelFlow.Api.Features.FuelTypes;
using FuelFlow.Api.Features.FuelPackages;
using FuelFlow.Api.Features.QrCodes;
using FuelFlow.Api.Features.Purchases;
using FuelFlow.Api.Features.Users;
using FuelFlow.Api.Features.PhoneVerification;
using FuelFlow.Api.Features.Vouchers;
using FuelFlow.Api.Features.Notifications;
using FuelFlow.Api.Features.VoucherImport;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.IdleTimeout = TimeSpan.FromHours(24);
});

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
builder.Services.AddScoped<IStationRepository, StationRepository>();
builder.Services.AddScoped<IFuelTypeRepository, FuelTypeRepository>();
builder.Services.AddScoped<IFuelPackageRepository, FuelPackageRepository>();
builder.Services.AddScoped<IQrCodeRepository, QrCodeRepository>();
builder.Services.AddScoped<IPurchaseRepository, PurchaseRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPhoneVerificationRepository, PhoneVerificationRepository>();
builder.Services.AddScoped<IVoucherRepository, VoucherRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IImportJobRepository, ImportJobRepository>();

builder.Services.AddSingleton<ITwilioService, TwilioService>();
builder.Services.AddSingleton<IStripeService, StripeService>();
builder.Services.AddHttpClient<IGeminiService, GeminiService>();
builder.Services.AddSingleton<ImportOrchestrator>();
builder.Services.AddSingleton<IImportOrchestrator>(key => key.GetRequiredService<ImportOrchestrator>());
builder.Services.AddHostedService(key => key.GetRequiredService<ImportOrchestrator>());

var app = builder.Build();

var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
Directory.CreateDirectory(uploadsPath);

app.UseCors();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseSession();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));

app.Run();

public partial class Program { }
