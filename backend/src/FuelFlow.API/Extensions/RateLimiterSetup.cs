using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.Extensions;

internal static class RateLimiterSetup
{
    internal const string SendCodePolicy = "send-code";
    internal const string VerifyCodePolicy = "verify-code";
    internal const string DeviceChallengePolicy = "device-challenge";
    internal const string DeviceVerifyPolicy = "device-verify";
    internal const string PurchasePolicy = "purchase";
    internal const string ReferralWritePolicy = "referral-write";
    internal const string RefreshPolicy = "refresh";
    internal const string CompanyInvitePolicy = "company-invite";
    internal const string MonobankWebhookPolicy = "monobank-webhook";
    internal const string SupportMessagePolicy = "support-message";

    /// <summary>Ceiling applied to every request, per client IP, per minute. Generous enough that
    /// no legitimate client or provider callback approaches it; low enough that a single host
    /// cannot sit on the unattributed endpoints (webhook, PDF import, all of admin) all day.</summary>
    internal const int GlobalPerIpPermitLimit = 300;

    /// <summary>Ceiling on OTP requests per client IP per 10 minutes. The named send-code and
    /// verify-code policies key on the phone number, so on their own they let one host pump SMS
    /// to unlimited distinct numbers without ever filling a single per-phone bucket.</summary>
    internal const int OtpPerIpPermitLimit = 12;

    /// <summary>Cap on how much request body the limiter will buffer to find a phone number.
    /// The limiter runs before model binding, so it must not be a place where an attacker can
    /// make the server buffer an arbitrarily large body.</summary>
    private const int MaxPhoneProbeBytes = 4096;

    /// <summary>
    /// Trusts X-Forwarded-For/X-Forwarded-Proto from the in-network reverse proxies so rate
    /// limits are keyed by the real client IP rather than by a proxy address.
    /// <para>
    /// Two things were wrong here. First, the comments described Render's load balancer; the
    /// deployment is now a single Hetzner server where the proxies are containers on a
    /// private Docker network, and both <c>KnownProxies</c> and <c>KnownIPNetworks</c> were
    /// cleared - which switches the trust check off entirely and trusts whatever the immediate
    /// peer claims.
    /// </para>
    /// <para>
    /// Second, and more consequential: <c>ForwardLimit</c> defaults to 1, so only the rightmost
    /// X-Forwarded-For entry is consumed. Requests arriving on the admin domain traverse TWO
    /// proxies - Caddy appends the client IP, then the admin container's nginx appends Caddy's
    /// address (<c>$proxy_add_x_forwarded_for</c> in admin/nginx.conf). Taking one hop back from
    /// that chain yields Caddy's container address for every caller, so all admin-origin traffic
    /// collapsed into a single rate-limit partition: one client could exhaust the shared bucket
    /// for everyone, and the per-IP OTP ceiling degraded to one global bucket. ForwardLimit is
    /// therefore 2, and the chain walk stops at the first untrusted hop.
    /// </para>
    /// <para>
    /// The trusted set is the private address space the container network draws from. Docker
    /// assigns user-defined bridge subnets out of 172.16.0.0/12 without a stable choice, so the
    /// range is trusted rather than a single subnet. This is not a loose grant: the API container
    /// publishes no host port, so the only peers that can open a connection to it are already on
    /// that private network. Override with <c>ForwardedHeaders:TrustedNetworks</c> (CIDR list) if
    /// the topology changes.
    /// </para>
    /// </summary>
    internal static IServiceCollection AddForwardedHeadersSupport(this IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration.GetSection("ForwardedHeaders:TrustedNetworks").Get<string[]>();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // client -> Caddy -> nginx -> API is the longest legitimate chain.
            options.ForwardLimit = 2;

            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var network in ParseTrustedNetworks(configured))
                options.KnownIPNetworks.Add(network);
        });

        return services;
    }

    /// <summary>
    /// Parses the configured CIDR list, falling back to loopback plus the RFC1918 / RFC4193
    /// private ranges that container networks are assigned from. A malformed entry is skipped
    /// rather than throwing, but the fallback is never silently empty: an empty trusted set
    /// means "trust every peer", which is the failure mode this method exists to avoid.
    /// </summary>
    private static IEnumerable<System.Net.IPNetwork> ParseTrustedNetworks(string[]? configured)
    {
        var candidates = configured is { Length: > 0 }
            ? configured
            : DefaultTrustedNetworks;

        var parsed = new List<System.Net.IPNetwork>();

        foreach (var entry in candidates)
        {
            if (System.Net.IPNetwork.TryParse(entry, out var network))
                parsed.Add(network);
        }

        if (parsed.Count == 0)
        {
            foreach (var entry in DefaultTrustedNetworks)
            {
                if (System.Net.IPNetwork.TryParse(entry, out var network))
                    parsed.Add(network);
            }
        }

        return parsed;
    }

    private static readonly string[] DefaultTrustedNetworks =
    [
        "127.0.0.0/8",      // loopback
        "10.0.0.0/8",       // RFC1918
        "172.16.0.0/12",    // RFC1918 - Docker's default bridge pool lives here
        "192.168.0.0/16",   // RFC1918
        "::1/128",          // IPv6 loopback
        "fd00::/8"          // RFC4193 unique-local
    ];

    internal static IServiceCollection AddRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(SendCodePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPhoneOrIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = IsDevBypass(context) ? int.MaxValue : 3,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(VerifyCodePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPhoneOrIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = IsDevBypass(context) ? int.MaxValue : 5,
                        Window = TimeSpan.FromMinutes(5),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(DeviceChallengePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(DeviceVerifyPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(5),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(PurchasePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetUserId(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(ReferralWritePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetUserId(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(RefreshPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Sending a company invitation reveals, per attempt, whether a phone number belongs
            // to a registered FuelFlow customer and whether that customer is already in a
            // company - the handler returns distinguishable WorkerNotFound / CannotInviteSelf /
            // WorkerAlreadyMember / AlreadyPending / Success outcomes. CompanyController carried
            // no named policy, so only the 300/min/IP global ceiling applied: roughly 432,000
            // probes per day per IP, enough to check a bought list of Ukrainian numbers against
            // the customer base, and each Success also plants a visible invitation on a
            // stranger's account. Company owners add staff a handful of times a month, so a low
            // per-account hourly ceiling costs legitimate use nothing and takes the oracle from
            // 432,000/day to 240/day per account.
            options.AddPolicy(CompanyInvitePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetUserId(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // The public contact form on palne.shop/support is anonymous, so IP is the only
            // partition key available. Five messages per hour per IP is far above any real
            // person's need (they would call after two unanswered emails) while capping
            // automated abuse; the global 300/min/IP ceiling still applies on top.
            options.AddPolicy(SupportMessagePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"support:{GetIp(context)}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // The webhook is the only anonymous endpoint that does real asymmetric crypto per
            // request: it reads the whole body, then ECDSA-verifies it against the merchant public
            // key before anything else runs. Unsigned garbage is therefore not free - it costs a
            // verification. Only the 300/min/IP global ceiling applied, and that ceiling is sized
            // for cheap requests, not for 300 signature verifications a minute per source address.
            //
            // Monobank's real callback volume is one or two calls per invoice, so a ceiling well
            // above peak checkout throughput costs legitimate callbacks nothing. Deliberately keyed
            // on IP and not on invoice id: the partition key must come from something an attacker
            // cannot vary freely, and the body is attacker-controlled until the signature checks out.
            options.AddPolicy(MonobankWebhookPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"monobank-webhook:{GetIp(context)}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Named policies only protect endpoints that opted in with [EnableRateLimiting].
            // Everything else - the Monobank webhook, PDF import, every admin route - had no
            // limit at all. The global limiter runs in addition to any named policy.
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: $"global-ip:{GetIp(context)}",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = IsDevBypass(context) ? int.MaxValue : GlobalPerIpPermitLimit,
                            Window = TimeSpan.FromMinutes(1),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0
                        })),

                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsOtpPath(context.Request.Path)
                        ? RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: $"otp-ip:{GetIp(context)}",
                            factory: _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = IsDevBypass(context) ? int.MaxValue : OtpPerIpPermitLimit,
                                Window = TimeSpan.FromMinutes(10),
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                                QueueLimit = 0
                            })
                        : RateLimitPartition.GetNoLimiter<string>("otp-not-applicable")));
        });

        return services;
    }

    /// <summary>
    /// True for the two endpoints that can cause an SMS to be sent. Matched with a trailing-slash
    /// and case tolerant comparison because routing is tolerant of both, and a path check that is
    /// stricter than routing is a bypass.
    /// </summary>
    private static bool IsOtpPath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
            return false;

        var normalized = value.Length > 1 ? value.TrimEnd('/') : value;

        return normalized.Equals("/api/auth/send-code", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/api/auth/verify", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDevBypass(HttpContext context)
    {
        var authOptions = context.RequestServices.GetService<IOptions<AuthOptions>>();
        return authOptions?.Value.DevBypass ?? false;
    }

    private static string GetIp(HttpContext context)
    {
        // UseForwardedHeaders rewrites RemoteIpAddress from the trusted
        // X-Forwarded-For header. The raw header must NOT be read here:
        // clients can spoof it to escape IP-based rate limits.
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Keys the auth rate limit on the normalized phone number so rotating IPs
    /// cannot SMS-bomb a victim. Falls back to the client IP when the request
    /// body does not carry a parseable phoneNumber.
    /// </summary>
    private static string GetPhoneOrIp(HttpContext context)
    {
        var phone = TryGetPhone(context);
        return string.IsNullOrWhiteSpace(phone) ? GetIp(context) : $"phone:{phone}";
    }

    /// <summary>
    /// Reads the phone number out of the request body for partitioning.
    /// <para>
    /// Partition factories are synchronous, and Kestrel rejects synchronous reads of the request
    /// body unless <see cref="IHttpBodyControlFeature.AllowSynchronousIO"/> is set. Without that
    /// opt-in the read below threw, the exception was swallowed, and the partition silently
    /// degraded to per-IP - meaning the per-phone limit this method exists to provide never
    /// applied and a rotating-IP attacker could SMS-bomb one victim without limit.
    /// </para>
    /// </summary>
    private static string? TryGetPhone(HttpContext context)
    {
        var bodyControl = context.Features.Get<IHttpBodyControlFeature>();
        var originalAllowSynchronousIO = bodyControl?.AllowSynchronousIO;

        try
        {
            if (bodyControl != null)
                bodyControl.AllowSynchronousIO = true;

            context.Request.EnableBuffering();

            // Bounded read: this runs before model binding, so it must not be a lever for making
            // the server buffer an unbounded body.
            var buffer = new byte[MaxPhoneProbeBytes];
            var read = 0;
            while (read < buffer.Length)
            {
                var chunk = context.Request.Body.Read(buffer, read, buffer.Length - read);
                if (chunk == 0)
                    break;
                read += chunk;
            }
            context.Request.Body.Position = 0;

            if (read == 0)
                return null;

            var body = Encoding.UTF8.GetString(buffer, 0, read);

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("phoneNumber", out var prop) ||
                prop.ValueKind != JsonValueKind.String)
                return null;

            var raw = prop.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var phoneService = context.RequestServices.GetService<IPhoneNumberService>();
            try
            {
                return phoneService?.Normalize(raw) ?? raw;
            }
            catch
            {
                return raw;
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (bodyControl != null && originalAllowSynchronousIO.HasValue)
                bodyControl.AllowSynchronousIO = originalAllowSynchronousIO.Value;
        }
    }

    private static string GetUserId(HttpContext context)
    {
        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
            return userId;

        return GetIp(context);
    }
}
