using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.BackgroundJobs;
using FuelFlow.Features.Pricing.BackfillBrandFuelCodes;
using FuelFlow.Features.Pricing.ImportOkkoCatalogue;
using FuelFlow.Features.Pricing.ScrapeOkkoPrices;
using FuelFlow.Features.Pricing.ScrapeUpgWogPrices;
using FuelFlow.Features.Pricing.ScrapeMinfinPrices;
using FuelFlow.Features.Pricing.ImportKloPrices;
using FuelFlow.Features.Admin.GetReconciliation;
using FuelFlow.Features.Auth.AdminLogin;
using FuelFlow.Features.Auth.AdminUser.GetAdminUserOptions;
using FuelFlow.Features.Auth.AdminUser.GetAdminUsers;
using FuelFlow.Features.Auth.GenerateChallenge;
using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Auth.Refresh;
using FuelFlow.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.Features.Auth.SendCode.Services;
using FuelFlow.Features.Auth.SharedServices;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Features.Auth.VerifyChallenge;
using FuelFlow.Features.Contracts.GetAdminContracts;
using FuelFlow.Features.Contracts.GetSignedContractSignature;
using FuelFlow.Features.Contracts.GetSignedContracts;
using FuelFlow.Features.Feedback;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Notifications.DeregisterPushToken;
using FuelFlow.Features.Notifications.GetNotifications;
using FuelFlow.Features.Notifications.MarkNotificationRead;
using FuelFlow.Features.Notifications.Push;
using FuelFlow.Features.Support;
using FuelFlow.Features.Support.CreateSupportMessage;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.DeleteMyOrder;
using FuelFlow.Features.Orders.DeleteOrder;
using FuelFlow.Features.Orders.GetAdminPurchases;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.SimulatePayment;
using FuelFlow.Features.Orders.UpdateMonobankInfo;
using FuelFlow.Features.Brands.CreateBrand;
using FuelFlow.Features.Brands.DeleteBrand;
using FuelFlow.Features.Brands.GetAdminFuelTypeById;
using FuelFlow.Features.Brands.GetAdminFuelTypes;
using FuelFlow.Features.Brands.CreateBrandStation;
using FuelFlow.Features.Brands.DeleteBrandStation;
using FuelFlow.Features.Brands.GetAdminBrandById;
using FuelFlow.Features.Brands.GetAdminBrandStationById;
using FuelFlow.Features.Brands.GetAdminBrandStations;
using FuelFlow.Features.Brands.ImportBrandStations;
using FuelFlow.Features.Brands.UpdateBrandStation;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Brands.GetProviderById;
using FuelFlow.Features.Brands.GetProviderHistory;
using FuelFlow.Features.Brands.GetProviders;
using FuelFlow.Features.Brands.GetAdminBrands;
using FuelFlow.Features.Brands.GetPublicFuelTypes;
using FuelFlow.Features.Brands.GetPublicPackages;
using FuelFlow.Features.Brands.GetPublicBrandStations;
using FuelFlow.Features.Brands.GetPublicBrands;
using FuelFlow.Features.Brands.UpdateBrand;
using FuelFlow.Features.Attestation;
using FuelFlow.Features.Updates;
using FuelFlow.Features.Users.ChangeEmail;
using FuelFlow.Features.Users.UpdateUser;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.DeleteVoucher;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.GetAdminVoucherById;
using FuelFlow.Features.Vouchers.GetAdminVoucherLifecycle;
using FuelFlow.Features.Vouchers.GetAdminVouchers;
using FuelFlow.Features.Vouchers.GetImportBatches;
using FuelFlow.Features.Vouchers.GetUserVouchers;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.MarkVoucherAsUsed;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.Redemptions;
using FuelFlow.Features.Vouchers.Renewal.Checkout;
using FuelFlow.Features.Vouchers.Renewal.Quote;
using FuelFlow.Features.Vouchers.RestoreVoucher;
using FuelFlow.Features.Vouchers.UnarchiveVoucher;
using FuelFlow.Features.Vouchers.UpdateVoucher;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settlement;
using FuelFlow.Middleware;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Notifications.Email;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Resilience;
using FuelFlow.SharedKernel.Security;
using FuelFlow.SharedKernel.Security.Attestation;
using FuelFlow.SharedKernel.Services;
using Scrutor;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FuelFlow.API.Extensions;

internal static class ServiceSetup
{
    internal static IServiceCollection AddFeatureServices(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<SmsOptions>(config.GetSection(SmsOptions.SectionName));
        services.Configure<SmsClubOptions>(config.GetSection(SmsClubOptions.SectionName));
        services.Configure<MonobankOptions>(config.GetSection(MonobankOptions.SectionName));
        services.Configure<DeviceAuthOptions>(config.GetSection(DeviceAuthOptions.SectionName));
        services.Configure<AuthOptions>(config.GetSection(AuthOptions.SectionName));

        // The key that encrypts voucher QR payloads at rest. Bound from configuration and never
        // defaulted: QrCipher's constructor throws when it is missing, because a default key is a
        // key every deployment shares and a QR payload is what a verifier trusts.
        //
        // Registered as a singleton rather than scoped on purpose. EF caches the model per context
        // type, so the value converter captures whatever instance it was handed; a scoped cipher
        // would mean a per-request instance captured into a process-wide model. Singleton also means
        // one key in memory for the process lifetime.
        services.Configure<QrCipherOptions>(config.GetSection(QrCipherOptions.SectionName));
        services.AddSingleton<FuelFlow.Features.Vouchers.Security.IQrCipher,
            FuelFlow.Features.Vouchers.Security.QrCipher>();
        // The QA access code is hashed with the OTP pepper once at boot and its plaintext dropped,
        // by AddOtpSecurity (Features/Auth/Security): it is a verification code, so it uses the
        // same peppered hasher as an OTP row rather than a bare digest.
        services.Configure<AppVersionOptions>(config.GetSection(AppVersionOptions.SectionName));
        services.Configure<UpdatesOptions>(config.GetSection(UpdatesOptions.SectionName));
        services.Configure<ObservabilityOptions>(config.GetSection(ObservabilityOptions.SectionName));
        services.Configure<TelegramOptions>(config.GetSection(TelegramOptions.SectionName));
        services.Configure<ExpoPushOptions>(config.GetSection(ExpoPushOptions.SectionName));
        services.Configure<AttestationOptions>(config.GetSection(AttestationOptions.SectionName));

        var connectionString = config.BuildConnectionString();
        var redisConnection = RedisConnectionParser.Parse(config.GetConnectionString("Redis") ?? "localhost:6379");

        services.AddHealthChecks()
            // The health check opens its OWN pool, so the cap has to be on this string too -
            // otherwise the probe's pool is uncapped and is exactly the pool that can storm the
            // server during an incident.
            .AddNpgSql(
                DatabaseConnectionPool.WithPoolSettings(connectionString, DatabaseConnectionPool.ResolveMaxPoolSize(config)),
                name: "postgres",
                tags: ["ready"])
            .AddRedis(redisConnection, name: "redis", tags: ["ready"]);

        AddVoucherServices(services);
        AddOrderServices(services);
        AddAuthServices(services);
        AddSmsService(services, config);
        AddMonobankService(services, config);
        AddInfrastructureServices(services);
        AddUserServices(services);
        AddReferralServices(services);
        AddBrandservices(services);
        AddProviderServices(services);
        AddContractServices(services);
        AddAdminServices(services);
        AddNotificationServices(services, config);
        AddSupportServices(services, config);
        AddFeedbackServices(services, config);
        AddSettlementServices(services, config);
        AddBackgroundJobServices(services);
        AddOkkoPriceServices(services, config);
        AddUpgWogPriceServices(services, config);
        AddMinfinPriceServices(services, config);
        AddUpdateServices(services, config);

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
        // The writer that gives a company its contracts (planning #445). Registered here
        // because the contracts feature had no writer at all: the table existed with a term and
        // a foreign key to a brand, and nothing in the platform ever created a row in it.
        services.AddScoped<FuelFlow.Features.Contracts.ContractWriter>();
        // The reader that takes the text out of the uploaded Word or PDF document.
        services.AddSingleton<FuelFlow.Features.Contracts.ContractTextExtractor>();
        services.AddScoped<ImportVouchersCommandHandler>();
        // Singleton: one slot process-wide is the whole point.
        services.AddSingleton<ImportConcurrencyGuard>();
        services.AddScoped<IVoucherProviderParser, OkkoVoucherParser>();
        services.AddScoped<IVoucherProviderParser, WogVoucherParser>();
        services.AddTransient<IPdfRenderer, PdfRenderer>();
        services.AddTransient<IQrDecoder, QrDecoder>();
        services.AddTransient<IVoucherDetector, VoucherDetector>();
        services.AddTransient<IQrGenerator, QrGeneratorV2>();
        services.AddScoped<GetImportBatchesQueryHandler>();
        services.AddScoped<GetImportBatchByIdQueryHandler>();
        services.AddScoped<GetImportBatchVouchersQueryHandler>();
        services.AddScoped<BlendedCostRecalculator>();
        // The single point every fuel price passes through, which is what makes the history complete
        // rather than best-effort. Registered next to its first caller because all five of them need it.
        services.AddScoped<FuelFlow.Features.Pricing.PriceHistoryRecorder>();
        services.AddScoped<FuelFlow.Features.Brands.GetFuelPriceHistory.GetFuelPriceHistoryQueryHandler>();
        services.AddScoped<FuelFlow.Features.Brands.GetFuelPriceAt.GetFuelPriceAtQueryHandler>();
        services.AddScoped<GetImportBatchCostsQueryHandler>();
        services.AddScoped<GetImportBatchPnlQueryHandler>();
        services.AddScoped<SetBatchCostCommandHandler>();
        services.AddScoped<FuelFlow.Features.Vouchers.ParseInvoice.ParseInvoiceCommandHandler>();
        services.AddScoped<GetUserVouchersCommandHandler>();
        services.AddScoped<MarkVoucherAsUsedCommandHandler>();
        services.AddScoped<RestoreVoucherCommandHandler>();
        services.AddScoped<VoucherRedemptionService>();
        services.AddScoped<RedemptionJournalPoster>();
        services.AddScoped<GetAdminVouchersQueryHandler>();
        services.AddScoped<GetAdminVoucherByIdQueryHandler>();
        services.AddScoped<UpdateVoucherCommandHandler>();
        services.AddScoped<DeleteVoucherCommandHandler>();
        services.AddScoped<BulkActionVouchersCommandHandler>();
        // The undo for the soft delete: without it a mistaken delete_all has no way back (#217).
        services.AddScoped<UnarchiveVoucherCommandHandler>();

        // Operator→supplier voucher exchange / renewal (#104).
        services.AddScoped<GetVoucherExchangeAttentionQueryHandler>();
        services.AddScoped<GetVoucherExchangeCostContextQueryHandler>();
        // The exchange audit, as batches and as pairs (#223). Read-only: the table was written for
        // every exchange and read by none.
        services.AddScoped<GetVoucherExchangeBatchesQueryHandler>();
        services.AddScoped<GetVoucherExchangeBatchQueryHandler>();

        // The reader D-13 was built for (VOUCHER_LIFECYCLE.md slice 2): one voucher, whole. The ledger
        // tables have nineteen writers and, until this, no reader at all.
        services.AddScoped<GetAdminVoucherLifecycleQueryHandler>();
        services.AddScoped<ConfirmVoucherExchangeCommandHandler>();
    }

    private static void AddOrderServices(IServiceCollection services)
    {
        services.AddScoped<CreateCheckoutCommandHandler>();
        services.AddScoped<BulkCheckoutCommandHandler>();
        // Registered explicitly (not via the *CommandHandler assembly scan) so the renewal checkout
        // is obviously wired next to buy-fuel checkout and cannot be silently dropped if the scan
        // convention changes.
        services.AddScoped<RenewalCheckoutCommandHandler>();
        services.AddScoped<RenewalQuoteCommandHandler>();
services.AddScoped<TermQuoteQueryHandler>();
        services.AddScoped<FuelFlow.Features.Vouchers.Renewal.Operator.GetRenewableCustomerVouchersQueryHandler>();
        services.AddScoped<FuelFlow.Features.Vouchers.Renewal.Operator.ConfirmOperatorRenewalCommandHandler>();
        services.AddScoped<GetUserPurchasesCommandHandler>();
        services.AddScoped<FuelFlow.Features.Orders.GetSavingsReport.GetSavingsReportQueryHandler>();
        services.AddScoped<SimulatePaymentCommandHandler>();
        services.AddScoped<UpdateMonobankInfoCommandHandler>();
        services.AddScoped<GetAdminPurchasesQueryHandler>();
        services.AddScoped<DeleteOrderCommandHandler>();
        services.AddScoped<DeleteMyOrderCommandHandler>();
        services.AddScoped<RefundOrderCommandHandler>();
    }

    private static void AddAuthServices(IServiceCollection services)
    {
        services.AddScoped<SendCodeCommandHandler>();
        services.AddScoped<VerifyCodeCommandHandler>();
        // Shared OTP possession-check for step-up flows (e.g. self-service email change), separate
        // from the login handler so those flows can re-prove identity without minting a session.
        services.AddScoped<OtpVerificationService>();
        services.AddScoped<AdminSendCodeCommandHandler>();
        services.AddScoped<AdminVerifyCodeCommandHandler>();
        // The second factor of a staff sign-in: the challenge the first factor hands out, the
        // enrolment of the secret, and the check of the code. Scoped: it reads and writes the
        // per-request DbContext.
        services.AddScoped<FuelFlow.Features.Auth.TwoFactor.StaffTwoFactorService>();
        services.AddScoped<FuelFlow.Features.Auth.TwoFactor.VerifyTwoFactorCommandHandler>();
        services.AddScoped<FuelFlow.Features.Auth.TwoFactor.RevokeTwoFactorCommandHandler>();
        services.AddScoped<RefreshTokenCommandHandler>();
        services.AddScoped<RegisterDeviceCommandHandler>();
        services.AddScoped<GenerateChallengeCommandHandler>();
        services.AddScoped<VerifyChallengeCommandHandler>();
        services.AddScoped<LogoutDeviceCommandHandler>();
        services.AddScoped<AttestationChallengeService>();
        services.AddScoped<AttestationService>();
        services.AddScoped<LogoutSessionCommandHandler>();
        // Central QA test-access gate. Scoped: it reads the per-request DB context via
        // RuntimeSettingsService. SetQaTestAccessCommandHandler is picked up by the CommandHandler scan.
        services.AddScoped<FuelFlow.Features.Auth.QaTestAccess.QaTestAccessService>();
        // Turns a business fact into a balanced posting. Scoped: it joins the caller's transaction,
        // so several entries for one payment commit together or not at all.
        services.AddScoped<FuelFlow.Features.Ledger.JournalPostingService>();
    }

    private static void AddSmsService(IServiceCollection services, IConfiguration config)
    {
        // Singleton: the daily spend ceiling is only a ceiling if every request shares one counter.
        services.AddSingleton<SmsBudgetGuard>();
        services.Configure<SmsOptions>(config.GetSection(SmsOptions.SectionName));
        services.Configure<SmsClubOptions>(config.GetSection(SmsClubOptions.SectionName));

        var devBypass = config.GetValue<bool>(AuthOptions.SectionName + ":DevBypass");
        if (devBypass)
        {
            services.AddScoped<ISmsService, FakeSmsService>();
            return;
        }

        // SMS Club is the sole real brand. Without credentials the app falls back to
        // FakeSmsService (dev only - OTP codes go to logs, never delivered); the Production
        // startup guard refuses to boot in that state (see SecurityConfigurationValidator).
        if (HasSmsClubConfiguration(config))
        {
            // The OTP path. A deadline on it: the brand is somebody else's server, and a request
            // that waits a hundred seconds for an SMS that was never sent is a login that never
            // happens. The send is a POST, so it is not retried - a second OTP code for one login
            // is worse than no code.
            var smsClub = config.GetSection(SmsClubOptions.SectionName).Get<SmsClubOptions>();
            services.AddOutboundHttpClient<SmsClubSmsService>(
                OutboundHttpClient.TimeoutOrDefault(smsClub?.TimeoutSeconds ?? SmsClubOptions.DefaultTimeoutSeconds),
                OutboundCallKind.Write);
            services.AddScoped<ISmsService>(sp => sp.GetRequiredService<SmsClubSmsService>());
            return;
        }

        services.AddScoped<ISmsService, FakeSmsService>();
    }

    /// <summary>
    /// True when real SMS Club credentials are present. Shared with the
    /// production startup guard (SecurityConfigurationValidator): without a brand the app
    /// silently falls back to FakeSmsService and OTP codes are never
    /// delivered to users.
    /// </summary>
    internal static bool HasSmsClubConfiguration(IConfiguration config)
    {
        var smsClubSection = config.GetSection(SmsClubOptions.SectionName);
        return !string.IsNullOrWhiteSpace(smsClubSection["Token"])
            && !string.IsNullOrWhiteSpace(smsClubSection["SenderName"]);
    }

    private static void AddMonobankService(IServiceCollection services, IConfiguration config)
    {
        var monobankOptions = config.GetSection(MonobankOptions.SectionName).Get<MonobankOptions>();

        // Two merchant profiles exist: live and sandbox. Nothing binds IMonobankClient directly
        // anymore - that would fix the choice globally, which is exactly what having a single
        // token used to mean. Every call site that needs the API resolves its client through
        // the factory, either from the merchant persisted on the order (refund, reconciliation,
        // refund sync) or from the resolver's decision at checkout time.
        services.AddSingleton<IMonobankClientFactory, MonobankClientFactory>();
        services.AddSingleton<IMonobankMerchantResolver, MonobankMerchantResolver>();

        if (monobankOptions?.Enabled == true)
        {
            // A timeout on the acquirer's own client, where there was none - the default is 100
            // seconds, which is how a slow acquirer holds a request thread for a minute and a half.
            //
            // The checkout depends on it: the request that loses the race waits for the winner's
            // invoice, and that wait is bounded by what the acquirer can do to us. A call that hangs
            // for a hundred seconds would make the wait meaningless.
            //
            // No retry: this client carries the invoice creation, and repeating that POST is how
            // one basket becomes two invoices. The circuit breaker still applies, so a downed
            // acquirer is refused in milliseconds instead of after the full timeout.
            services.AddOutboundHttpClient(
                $"monobank-{MonobankMerchant.Live}",
                TimeSpan.FromSeconds(MonobankOptions.ClientTimeoutSeconds),
                OutboundCallKind.Write);
            services.AddOutboundHttpClient(
                $"monobank-{MonobankMerchant.Sandbox}",
                TimeSpan.FromSeconds(MonobankOptions.ClientTimeoutSeconds),
                OutboundCallKind.Write);
        }

        services.AddScoped<ProcessMonobankWebhookCommandHandler>();
    }

    /// <summary>
    /// The OKKO pump-price fetch. Registered unconditionally (unlike Monobank, which swaps in a mock)
    /// because there is no offline substitute worth having: the job is the only caller, it no-ops while
    /// <c>OkkoPrice:Enabled</c> is false, and a stub client would only be a second thing to keep honest.
    /// </summary>
    private static void AddOkkoPriceServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<OkkoPriceOptions>(config.GetSection(OkkoPriceOptions.SectionName));

        var okko = config.GetSection(OkkoPriceOptions.SectionName).Get<OkkoPriceOptions>();
        // A read: the price page is fetched and parsed, and fetching it twice costs nothing.
        services.AddOutboundHttpClient<IOkkoPriceClient, OkkoPriceClient>(
            OutboundHttpClient.TimeoutOrDefault(okko?.TimeoutSeconds ?? OkkoPriceOptions.DefaultTimeoutSeconds),
            OutboundCallKind.Read);
    }

    private static void AddUpgWogPriceServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<UpgWogPriceOptions>(config.GetSection(UpgWogPriceOptions.SectionName));

        var upgWog = config.GetSection(UpgWogPriceOptions.SectionName).Get<UpgWogPriceOptions>();
        services.AddOutboundHttpClient<IUpgWogPriceClient, UpgWogPriceClient>(
            OutboundHttpClient.TimeoutOrDefault(upgWog?.TimeoutSeconds ?? UpgWogPriceOptions.DefaultTimeoutSeconds),
            OutboundCallKind.Read);
    }

    private static void AddMinfinPriceServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<MinfinPriceOptions>(config.GetSection(MinfinPriceOptions.SectionName));

        var minfin = config.GetSection(MinfinPriceOptions.SectionName).Get<MinfinPriceOptions>();
        services.AddOutboundHttpClient<IMinfinPriceClient, MinfinPriceClient>(
            OutboundHttpClient.TimeoutOrDefault(minfin?.TimeoutSeconds ?? MinfinPriceOptions.DefaultTimeoutSeconds),
            OutboundCallKind.Read);
    }

    private static void AddInfrastructureServices(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IAppleAppAttestVerifier, AppleAppAttestVerifier>();
        services.AddSingleton<IPlayIntegrityVerifier, PlayIntegrityVerifier>();
        services.AddSingleton<IAsymmetricSignatureVerifier, AsymmetricSignatureVerifier>();
        services.AddSingleton<DeviceSignatureVerifier>();
        services.AddScoped<IPhoneNumberService, PhoneNumberService>();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
    }

    private static void AddUserServices(IServiceCollection services)
    {
        services.AddScoped<UpdateUserCommandHandler>();
        services.AddScoped<RequestEmailChangeCommandHandler>();
    }

    private static void AddReferralServices(IServiceCollection services)
    {
    }

    private static void AddProviderServices(IServiceCollection services)
    {
        services.AddScoped<GetBrandsQueryHandler>();
        services.AddScoped<GetBrandByIdQueryHandler>();
        services.AddScoped<GetBrandHistoryQueryHandler>();
        services.AddScoped<AuditEventService>();
    }

    private static void AddBrandservices(IServiceCollection services)
    {
        services.AddScoped<GetAdminBrandsQueryHandler>();
        services.AddScoped<GetAdminBrandByIdQueryHandler>();
        services.AddScoped<CreateBrandCommandHandler>();
        services.AddScoped<UpdateBrandCommandHandler>();
        services.AddScoped<DeleteBrandCommandHandler>();
        services.AddScoped<GetAdminBrandStationsQueryHandler>();
        services.AddScoped<GetAdminBrandStationByIdQueryHandler>();
        services.AddScoped<CreateBrandStationCommandHandler>();
        services.AddScoped<UpdateBrandStationCommandHandler>();
        services.AddScoped<DeleteBrandStationCommandHandler>();
        services.AddScoped<ImportBrandStationsCommandHandler>();
        services.AddScoped<GetAdminFuelTypesQueryHandler>();
        services.AddScoped<GetAdminFuelTypeByIdQueryHandler>();
        services.AddScoped<GetPublicBrandsQueryHandler>();
        services.AddScoped<GetPublicFuelTypesQueryHandler>();
        services.AddScoped<GetPublicBrandStationsQueryHandler>();
        services.AddScoped<GetPublicPackagesQueryHandler>();
        // One definition of "in stock", used by the store front (this method) AND by checkout, so the two
        // can never disagree. Registered explicitly rather than left to the assembly scan: the scan covers
        // handlers by name convention, and this is a shared query service, not one.
        services.AddScoped<FuelFlow.Features.Brands.BrandSlugResolver>();
services.AddScoped<FuelFlow.Features.Vouchers.Availability.PackageStockAvailability>();
    }

    private static void AddContractServices(IServiceCollection services)
    {
        services.AddScoped<GetAdminContractsQueryHandler>();
        services.AddScoped<GetSignedContractsQueryHandler>();
        services.AddScoped<GetSignedContractSignatureQueryHandler>();
    }

    private static void AddAdminServices(IServiceCollection services)
    {
        services.AddScoped<GetAdminUsersQueryHandler>();
    services.AddScoped<GetAdminUserOptionsQueryHandler>();
        services.AddScoped<GetReconciliationQueryHandler>();
    }

    private static void AddNotificationServices(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<GetNotificationsQueryHandler>();
        services.AddScoped<MarkNotificationReadCommandHandler>();
        services.AddScoped<FuelFlow.Features.Notifications.RegisterPushToken.RegisterPushTokenCommandHandler>();
        services.AddScoped<DeregisterPushTokenCommandHandler>();
        // Typed client for the Expo Push service. Credential-free by default (see ExpoPushOptions);
        // the send is best-effort and fired from NotificationService when an in-app notification is
        // created.
        //
        // A deadline, because the push is fired on the request thread of whatever created the
        // notification - a push server that never answers would hold that thread. No retry: a
        // repeated push is a second notification on the customer's phone.
        var expoPush = config.GetSection(ExpoPushOptions.SectionName).Get<ExpoPushOptions>();
        services.AddOutboundHttpClient<IExpoPushSender, ExpoPushClient>(
            OutboundHttpClient.TimeoutOrDefault(expoPush?.TimeoutSeconds ?? ExpoPushOptions.DefaultTimeoutSeconds),
            OutboundCallKind.Write);
    }

    private static void AddSupportServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<SupportMailOptions>(config.GetSection(SupportMailOptions.SectionName));
        services.AddScoped<CreateSupportMessageCommandHandler>();
        services.AddScoped<ISupportMailSender, SupportMailSender>();
        // Reuses the SupportMail SMTP connection for transactional mail (admin OTP).
        services.AddScoped<IEmailSender, SmtpEmailSender>();
    }

    private static void AddFeedbackServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<TelegramFeedbackOptions>(
            config.GetSection(TelegramFeedbackOptions.SectionName));
        services.Configure<FeedbackClassifierOptions>(
            config.GetSection(FeedbackClassifierOptions.SectionName));

var feedback = config.GetSection(TelegramFeedbackOptions.SectionName)
            .Get<TelegramFeedbackOptions>();
        // The feedback bot: the platform posts to it and reads its answers. A POST, so no retry -
        // a repeated message is a second answer in the support channel.
        services.AddOutboundHttpClient<ITelegramFeedbackClient, TelegramFeedbackClient>(
            OutboundHttpClient.TimeoutOrDefault(feedback?.TimeoutSeconds ?? TelegramFeedbackOptions.DefaultTimeoutSeconds),
            OutboundCallKind.Write,
            client =>
            {
                var baseUrl = feedback?.BaseUrl;
                if (!string.IsNullOrWhiteSpace(baseUrl))
                    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            });

var classifier = config.GetSection(FeedbackClassifierOptions.SectionName)
            .Get<FeedbackClassifierOptions>();
        // The classifier asks a model to label a support message. A POST, so no retry: the answer
        // is a label, and a second call buys nothing the first did not.
        services.AddOutboundHttpClient<IFeedbackClassifier, GeminiFeedbackClassifier>(
            OutboundHttpClient.TimeoutOrDefault(classifier?.TimeoutSeconds ?? FeedbackClassifierOptions.DefaultTimeoutSeconds),
            OutboundCallKind.Write);

        // Scoped rather than singleton: the router writes through the request-scoped DbContext.
        services.AddScoped<FeedbackUpdateRouter>();
        services.AddScoped<FeedbackTriageService>();
    }

    /// <summary>
    /// The settlement channel. Registered unconditionally and gated at runtime by
    /// <see cref="TelegramSettlementOptions.IsConfigured"/> rather than by not registering at all:
    /// an endpoint that vanishes when a setting is missing is an endpoint that 404s instead of
    /// answering, and Telegram's response to a 404 is to retry forever. It stays registered, answers,
    /// and declines to write.
    /// <para>
    /// No <c>HttpClient</c> here yet. The feedback bot sends; this one receives, so it has nothing to
    /// call until it needs to acknowledge or reply. Adding an unused client with a base address would
    /// be a dependency that does nothing until someone wires it up.
    /// </para>
    /// </summary>
    private static void AddSettlementServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<TelegramSettlementOptions>(
            config.GetSection(TelegramSettlementOptions.SectionName));

        services.AddScoped<SettlementUpdateHandler>();
    }

    private static void AddBackgroundJobServices(IServiceCollection services)
    {
        services.AddScoped<FulfillmentService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<RefundStatusSyncService>();
        services.AddScoped<MonobankReconciliationService>();
    services.AddScoped<MonobankStatementImportService>();
        services.AddScoped<VoucherStockMonitor>();
        services.AddScoped<DeletedUnpaidOrderCleanupService>();
        services.AddScoped<DataRetentionService>();
        services.AddScoped<ExpiredVoucherLossService>();
        services.AddScoped<HangfireHistoryCleanupService>();
        services.AddScoped<RuntimeSettingsService>();
        services.AddScoped<OkkoPriceSyncService>();
        // The same job for the two vendors that publish a page rather than an API.
        services.AddScoped<UpgWogPriceSyncService>();
        // The vendor that publishes no prices: this one learns which grades exist.
        services.AddScoped<KloPriceSyncService>();
        services.AddScoped<ImportKloPricesCommandHandler>();
        // The catalogue half of the same fetch: creates the fuels OKKO sells that we do not carry yet,
        // and nothing else. Registered beside the sync because it is only ever called by it.
        services.AddScoped<ImportOkkoCatalogueCommandHandler>();
        // Runs in the same job, between the catalogue import and the pricing: it is what lets the
        // price step match by code the fuels an operator typed rather than by category.
        services.AddScoped<BackfillBrandFuelCodesCommandHandler>();
    }

    private static void AddUpdateServices(IServiceCollection services, IConfiguration config)
    {
        // Typed client for the self-hosted OTA manifest endpoint. It fetches pre-signed update
        // descriptors from public R2 over plain HTTPS — no S3 SDK and no credential, because update
        // integrity is enforced by the client-verified manifest signature, not the transport.
        //
        // A read, so it is retried: an installed app asking "is there an update?" while the CDN
        // blinks should get an answer, not a 502.
        var updates = config.GetSection(UpdatesOptions.SectionName).Get<UpdatesOptions>();
        services.AddOutboundHttpClient<UpdateManifestSource>(
            OutboundHttpClient.TimeoutOrDefault(updates?.TimeoutSeconds ?? UpdatesOptions.DefaultTimeoutSeconds),
            OutboundCallKind.Read);
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
                        // The wallet's paging headers. A browser client cannot read a
                        // response header that is not exposed, and the mobile client falls
                        // back to "a short page means the end" - but the count is worth
                        // having for the client that can read it.
                        .WithExposedHeaders("X-Total-Count", "X-Has-Next-Page")
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
