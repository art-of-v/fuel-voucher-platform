using FuelFlow.SharedKernel.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Serilog;

namespace FuelFlow.API.Extensions;

/// <summary>
/// Production-only "refuse to boot" guards for configuration that is unsafe by omission: a stray
/// flag or a missing secret that would silently disable a security or payment-integrity control.
/// Extracted from Program.cs so the individual rules are unit-testable; invoked once at startup.
/// </summary>
internal static class SecurityConfigurationValidator
{
    internal static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        // Development is exempt from everything here: a developer's box runs on generated keys and
        // a fake SMS provider, and a guard that stopped it from starting would be a guard that
        // would be turned off.
        if (environment.IsDevelopment())
            return;

        // The placeholder check is NOT production-only. Staging is a second, fully isolated stack
        // on the same host, behind an HTTP Basic credential - and it is deployed from a template
        // that ships the same placeholders as production's. A placeholder that is refused in
        // Production and accepted in Staging is a placeholder that gets copied into Production by
        // the next person who reads the wrong file.
        ValidatePlaceholderSecrets(configuration);

        if (!environment.IsProduction())
            return;

        var monobankEnabled = configuration.GetValue<bool?>("Monobank:Enabled") ?? false;
        var monobankPublicKey = configuration["Monobank:PublicKey"] ?? "";

        if (monobankEnabled &&
            (string.IsNullOrWhiteSpace(monobankPublicKey)
             || monobankPublicKey.Contains("PRODUCTION_PUBLIC_KEY_HERE", StringComparison.OrdinalIgnoreCase)
             || monobankPublicKey.Contains("YOUR_MONOBANK_PUBLIC_KEY", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Refusing to start: Monobank is enabled but Monobank:PublicKey is not configured. "
                + "Webhook signature verification cannot be enforced without the real Monobank public key.");
        }

        // "Is it set" is not "is it usable". This one cost a production incident: Monobank:PublicKey
        // held 100 characters of a 240-character key, because an env file was hand-edited and the
        // value was truncated somewhere along the way. Every existing check passed - it was set, and
        // it was not a placeholder - and then every webhook failed inside signature verification, so
        // no payment was ever confirmed by its own callback. Reconciliation silently became the only
        // thing completing orders. A configured-looking key that cannot be read is caught here,
        // where the fix is "fix the env var", instead of in production, where the symptom is a
        // paying customer with no voucher.
        if (monobankEnabled && !string.IsNullOrWhiteSpace(monobankPublicKey)
            && !PublicKeyFormat.IsReadable(monobankPublicKey))
        {
            throw new InvalidOperationException(
                "Refusing to start: Monobank:PublicKey is set but is not a readable public key "
                + $"(it is {monobankPublicKey.Trim().Length} characters; a Monobank key is 240). This is "
                + "almost always a value truncated while editing the env file. Fetch the key with "
                + "`curl -H 'X-Token: $MONOBANK_TOKEN' https://api.monobank.ua/api/merchant/pubkey`, take the "
                + "\"key\" field, and put the whole value in MONOBANK_PUBLIC_KEY. Until it is fixed, every "
                + "Monobank webhook is rejected and reconciliation is the only thing completing payments.");
        }

        // A Monobank test token creates invoices that settle in Monobank's sandbox and collect no
        // real money, while everything around it looks like a live payment system: orders are
        // created, vouchers are reserved, the reconciliation job reports them paid. So the failure
        // mode is not an error — it is a launch that looks successful and takes nothing. That is
        // only detectable if the state is written down, which is what this is for.
        //
        // Acknowledge-to-disable, like the two guards below: the sandbox phase is deliberate
        // today, and refusing to boot would stop work rather than make it safer. Remove the ack
        // together with the test token when #35 flips to real money.
        var monobankToken = configuration["Monobank:Token"] ?? "";
        if (monobankEnabled && monobankToken.StartsWith("test_", StringComparison.OrdinalIgnoreCase))
        {
            var acknowledged = configuration.GetValue<bool>("Monobank:AcknowledgeTestTokenInProduction");
            if (!acknowledged)
            {
                throw new InvalidOperationException(
                    "Refusing to start: Monobank is enabled with a TEST merchant token (Monobank:Token "
                    + "starts with 'test_'), so payments settle in Monobank's sandbox and no real money is "
                    + "ever collected - orders will be created and marked paid for nothing. Either set the "
                    + "live Monobank__Token, or set Monobank__AcknowledgeTestTokenInProduction=true to run "
                    + "on the sandbox as a deliberate, recorded decision.");
            }

            Log.Warning(
                "PAYMENTS: Production is running on a Monobank TEST merchant token, acknowledged explicitly. "
                + "No real money is being collected: invoices settle in Monobank's sandbox. This must be "
                + "replaced with the live token before taking real payments (#35).");
        }

        // MonobankReconciliationService is the only backstop for a payment webhook that never
        // arrives: it polls invoice status for still-unpaid orders and drives a paid-but-unnotified
        // one through the same fulfilment path a webhook would. ReconciliationEnabled defaults to
        // true, but a stray config flag can turn it off - and then a single lost webhook is a
        // customer charged with no voucher and nothing to catch it. Like the DeviceAuth guard below
        // this is acknowledge-to-disable, not a hard refusal: there can be a real reason to pause
        // reconciliation, but it must be a deliberate, recorded choice rather than a silent default.
        var reconciliationEnabled = configuration.GetValue<bool?>("Monobank:ReconciliationEnabled") ?? true;
        if (monobankEnabled && !reconciliationEnabled)
        {
            var acknowledged = configuration.GetValue<bool>("Monobank:AcknowledgeReconciliationDisabled");
            if (!acknowledged)
            {
                throw new InvalidOperationException(
                    "Refusing to start: Monobank is enabled but Monobank:ReconciliationEnabled is false "
                    + "in Production, so lost or late payment webhooks are never reconciled - a paid order "
                    + "can be left unfulfilled with no retry. Either set Monobank__ReconciliationEnabled=true, "
                    + "or set Monobank__AcknowledgeReconciliationDisabled=true to run without the "
                    + "reconciliation safety net as a deliberate, recorded decision.");
            }

            Log.Warning(
                "PAYMENTS: Monobank reconciliation is disabled in Production by explicit acknowledgement. "
                + "Lost or late payment webhooks will not be recovered; a paid order can remain unfulfilled.");
        }

        // Auth:DevBypass is a single switch that turns off most of the auth surface at once:
        // ISmsService becomes FakeSmsService (OTP codes only ever reach the log stream, so anyone
        // who can read logs can log in as anyone), every OTP and global rate limit becomes
        // int.MaxValue, and the Hangfire dashboard drops its authorization filter. One stray
        // environment variable in the deploy config is therefore a full authentication bypass plus
        // an unauthenticated job-management console. It must never be set in Production.
        var devBypass = configuration.GetValue<bool>("Auth:DevBypass");
        if (devBypass)
        {
            throw new InvalidOperationException(
                "Refusing to start: Auth:DevBypass is enabled in Production. This disables real SMS "
                + "delivery (OTP codes go to logs only), removes every rate limit, and unauthenticates "
                + "the Hangfire dashboard. Unset Auth__DevBypass.");
        }

        // OTP codes must actually reach users' phones. With DevBypass off and no
        // SMS brand credentials the app silently falls back to FakeSmsService: codes
        // are only written to logs, nobody can log in, and the failure is easy to
        // miss. Refuse to start instead, like the Monobank guard above.
        var hasSmsClub = ServiceSetup.HasSmsClubConfiguration(configuration);
        if (!hasSmsClub)
        {
            throw new InvalidOperationException(
                "Refusing to start: Auth:DevBypass is off but SMS Club is not configured. " +
                "Set SmsClub__Token and SmsClub__SenderName — " +
                "OTP codes would never be delivered (silent FakeSmsService fallback).");
        }

        // DeviceAuth:Enabled defaults to false, and when it is false DeviceSignatureMiddleware
        // returns before it checks anything at all - device binding on /api/purchases silently
        // does not exist. Shipping that by omission is the failure mode worth blocking.
        //
        // This is NOT a hard refusal, because enabling signature enforcement is coupled to the
        // released mobile build: turning it on before a signing client is in users' hands locks
        // them out of checkout. So the operator has to state the choice in config rather than
        // arrive at it by default.
        var deviceAuthEnabled = configuration.GetValue<bool>("DeviceAuth:Enabled");
        if (!deviceAuthEnabled)
        {
            var acknowledged = configuration.GetValue<bool>("DeviceAuth:AcknowledgeDisabledInProduction");
            if (!acknowledged)
            {
                throw new InvalidOperationException(
                    "Refusing to start: DeviceAuth:Enabled is false in Production, so device signature "
                    + "verification on the checkout endpoints is inactive. Either set DeviceAuth__Enabled=true "
                    + "(only once a signing mobile build is released - enabling it earlier breaks checkout for "
                    + "existing installs), or set DeviceAuth__AcknowledgeDisabledInProduction=true to run "
                    + "without device binding as a deliberate, recorded decision.");
            }

            Log.Warning(
                "SECURITY: DeviceAuth is disabled in Production by explicit acknowledgement. "
                + "Checkout requests are not device-bound; a stolen access token is sufficient to purchase.");
        }

        // VoucherExpiration:Enabled is what makes fulfilment refuse expired stock, in both the
        // matcher and the atomic claim. When it is off, fulfilment happily hands the most-expired
        // stock it has to whoever buys first - which is exactly the stock a customer would least
        // want. It is read straight from configuration with no admin surface, so the only way to
        // reach it is an env var and a restart: a silent default nobody re-reads.
        //
        // Acknowledge-to-disable rather than a hard refusal, and deliberately keyed off
        // Monobank:Enabled rather than "is this Production": production is currently a Monobank
        // sandbox that still exercises expired stock on purpose, and a hard rule would block boot
        // today and halt that testing. What this changes is that the state has to be *stated* -
        // the same trade the reconciliation and DeviceAuth guards already make. When #35 introduces
        // a real-money signal, this should become a hard refusal with no ack accepted.
        var voucherExpirationEnabled = configuration.GetValue<bool?>("VoucherExpiration:Enabled") ?? true;
        if (monobankEnabled && !voucherExpirationEnabled)
        {
            var acknowledged = configuration.GetValue<bool>("VoucherExpiration:AcknowledgeDisabledInProduction");
            if (!acknowledged)
            {
                throw new InvalidOperationException(
                    "Refusing to start: Monobank is enabled but VoucherExpiration:Enabled is false "
                    + "in Production, so fulfilment hands already-expired vouchers to buyers and the "
                    + "checkout card is the only thing that flags it. Either set "
                    + "VoucherExpiration__Enabled=true, or set "
                    + "VoucherExpiration__AcknowledgeDisabledInProduction=true to sell expired stock as a "
                    + "deliberate, recorded decision.");
            }

            Log.Warning(
                "FULFILMENT: the voucher-expiration gate is disabled in Production by explicit "
                + "acknowledgement. Expired vouchers can be sold; customers receive fuel that is already "
                + "out of date. Clear this the moment real money is taken (see #35).");
        }

        // The database transport. Everything else in this file guards a control that is OFF by
        // default; this one guards a control that is ON by default, which is what makes it easy
        // to lose: "SSL Mode=Require" encrypts the session against anyone, and "Trust Server
        // Certificate=true" accepts any certificate at all - so a host that can present a
        // self-signed certificate terminates the session and reads the refresh-token hashes, the
        // personal data and the voucher ciphertext. "SSL Mode=Disable" is the same thing without
        // the encryption.
        //
        // The container-to-container connection used to be the reason for it (the local Postgres
        // had no certificate), and the answer was to keep the default for everybody so that a
        // managed database could never inherit it by accident. The certificates exist now, so
        // Production states a verified transport, and a string that says otherwise is a refusal
        // rather than a warning: the connection carries money and personal data, and the operator
        // has to say which transport they meant.
        //
        // "VerifyFull" is the only mode that checks WHO is on the other end, so it is the only
        // one that passes. A string with no SSL Mode at all also fails: the code would then
        // append "SSL Mode=Require;Trust Server Certificate=true" to it, which is the old default
        // wearing a different name.
        var connectionString = configuration["Database:ConnectionString"] ?? "";
        var migrationConnectionString = configuration["Database:MigrationConnectionString"] ?? "";

        // BOTH connections carry the money and the personal data - the migration reads the
        // schema and may touch the rows a backfill rewrites - so both have to verify the
        // server. The migration connection used to not exist; now it does, and it is the
        // one an operator is most likely to leave at the old default.
        foreach (var (settingName, value) in new[]
                 {
                     ("Database:ConnectionString", connectionString),
                     ("Database:MigrationConnectionString", migrationConnectionString),
                 })
        {
            if (value.Length > 0
                && !value.Contains("SSL Mode=VerifyFull", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing to start: the database connection string does not verify the server's "
                    + "certificate. In Production the connection must be "
                    + "'SSL Mode=VerifyFull' with the CA ('Root Certificate=<path to ca.crt>'): "
                    + "'Require' and 'Trust Server Certificate=true' encrypt the session against anyone, "
                    + "and 'Disable' does not encrypt it at all - and this connection carries the money and "
                    + $"the personal data. (The string that was refused is {settingName}.)");
            }
        }

        // The application's traffic and the schema migration must not share a role.
        //
        // One role used to serve both, and it was the cluster's SUPERUSER - the one role the
        // postgres image creates. So a defect that reached the database did not "leak a
        // table", it owned the cluster: it could read every role's password hash out of
        // pg_authid, create a new superuser, drop the database, or write straight into the
        // audit trail the money reconciliation depends on. The trail was protected by the
        // application's own discipline rather than by the database's privileges.
        //
        // The roles are separated now: the traffic runs as a role that may do the
        // application's work and nothing else, and the migration runs as another one that
        // may change the schema and nothing else. This is the check that the separation is
        // real rather than a naming convention - a configuration that points both strings at
        // one role puts the whole thing back, and it does so silently.
        if (connectionString.Length > 0)
        {
            // The migration connection string is only required when the application MIGRATES
            // at boot. A deployment that manages the schema by hand (RunMigrationsOnBoot=false)
            // has no migration to run, and refusing it to start over a connection string it
            // will never open would be a refusal of the wrong thing.
            var runMigrationsOnBoot = configuration.GetValue<bool?>("RunMigrationsOnBoot") ?? true;

            if (runMigrationsOnBoot && migrationConnectionString.Length == 0)
            {
                throw new InvalidOperationException(
                    "Refusing to start: Database:MigrationConnectionString is not set. The "
                    + "application's traffic and the schema migration must not share a role - the "
                    + "traffic runs as a role that can only do the application's own work, and the "
                    + "migration runs as another one that can change the schema. Point "
                    + "Database__MigrationConnectionString at the migration role (deploy/"
                    + "ensure-database-roles.sh creates both).");
            }

            if (migrationConnectionString.Length == 0)
            {
                // The schema is managed by hand; there is no second role to check.
                return;
            }

            var applicationRole = RoleOf(connectionString);
            var migrationRole = RoleOf(migrationConnectionString);

            if (applicationRole is null || migrationRole is null)
            {
                throw new InvalidOperationException(
                    "Refusing to start: one of the two database connection strings does not name "
                    + "a role, so it cannot be told whether the application's traffic and the "
                    + "schema migration share one. Both strings must carry a 'Username='.");
            }

            if (string.Equals(applicationRole, migrationRole, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing to start: the application's traffic and the schema migration both "
                    + $"connect as the role '{applicationRole}'. One role for both is what made a "
                    + "defect that reached the database able to own the cluster; the traffic "
                    + "belongs to a role that may do the application's work and nothing else, "
                    + "and the schema belongs to another one.");
            }
        }
    }

    /// <summary>
    /// The role a connection string connects as, or <c>null</c> when it does not name one.
    /// </summary>
    /// <remarks>
    /// Read through Npgsql's own parser rather than with a regex, so a string this code
    /// cannot read is a refusal rather than a guess: the parser is the one that will read
    /// the string when the connection opens.
    /// </remarks>
    private static string? RoleOf(string connectionString)
    {
        try
        {
            var username = new NpgsqlConnectionStringBuilder(connectionString).Username;
            return string.IsNullOrWhiteSpace(username) ? null : username;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// A secret that is still the placeholder from the env template is a secret in a public
    /// repository, and it is refused here - outside Development, in Production and in Staging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The finding this answers is planning #620 (S-11). The template
    /// <c>deploy/.env.production.example</c> ships four placeholder secrets, and the application
    /// accepted every one of them as a real value:
    /// <c>POSTGRES_PASSWORD=CHANGE_ME_openssl_rand_base64_36</c>,
    /// <c>REDIS_PASSWORD=CHANGE_ME_openssl_rand_hex_32</c>,
    /// <c>JWT_SECRET=CHANGE_ME_at_least_32_characters_long_random_string</c> and
    /// <c>QR_CIPHER_KEY=CHANGE_ME_openssl_rand_base64_32</c>. The JWT one is the serious one: it
    /// signs the access tokens, it is 53 characters long (so it passed the length check), and the
    /// guard that was supposed to catch it compared the value against a different literal. The
    /// repository is public until 2026-11-01, so a token signed with it can be forged by anybody.
    /// </para>
    /// <para>
    /// The check is the PATTERN, not one literal - a literal list is a list that goes stale, and the
    /// stale one is the reason this exists. The deploy template is the other half: preflight.sh
    /// compares the deployed env file against the template's own values verbatim, so a template that
    /// is edited to hide the placeholder does not defeat the check.
    /// </para>
    /// <para>
    /// Two of the four secrets are not settings of their own: the database password and the Redis
    /// password reach the application inside the connection strings that carry them, so they are
    /// read from there. That is the only place the application can see them - and it is enough,
    /// because a placeholder password that reaches the connection string is a placeholder password
    /// that is live.
    /// </para>
    /// </remarks>
    private static void ValidatePlaceholderSecrets(IConfiguration configuration)
    {
        RefusePlaceholder(
            configuration["Jwt:Secret"],
            "Jwt:Secret",
            "it signs every access token. A token signed with it can be forged by anybody, which "
            + "is full staff console access - refunds, payment placement, customer PII and the "
            + "redeemable voucher payloads - with the whole session model bypassed, because the "
            + "forger never holds a refresh token.");

        RefusePlaceholder(
            configuration["QrCipher:Key"],
            "QrCipher:Key",
            "it encrypts the redeemable QR payload of every voucher at rest, and it is the only "
            + "thing that can read it.");

        RefusePlaceholder(
            DatabasePassword(configuration["Database:ConnectionString"]),
            "POSTGRES_PASSWORD (inside Database:ConnectionString)",
            "it is the credential the application opens the database with, and the database holds "
            + "the money, the personal data and the voucher ciphertext.");

        RefusePlaceholder(
            RedisPassword(configuration["ConnectionStrings:Redis"]),
            "REDIS_PASSWORD (inside ConnectionStrings:Redis)",
            "it is the credential the application opens Redis with, and Redis holds the cached "
            + "sessions.");
    }

    /// <summary>
    /// Refuses one placeholder value, naming the setting so the operator knows which line of the
    /// env file to fix.
    /// </summary>
    private static void RefusePlaceholder(string? value, string setting, string consequence)
    {
        if (!PlaceholderSecret.LooksLikePlaceholder(value))
            return;

        throw new InvalidOperationException(
            $"Refusing to start: {setting} still holds a placeholder value. {consequence} "
            + "The env templates ship placeholder values on purpose - they are instructions, not "
            + "secrets - and they are published in a public repository, so a value that was never "
            + "replaced is a value everybody has. Generate a real one (openssl rand -base64 36 for "
            + "the database and the JWT key, openssl rand -hex 32 for Redis, openssl rand -base64 32 "
            + "for the QR cipher key) and put it in the env file on the box.");
    }

    /// <summary>
    /// The password inside an Npgsql-style connection string, which is how the database password
    /// reaches the application: <c>Host=...;Password=...;SSL Mode=...</c>.
    /// </summary>
    private static string? DatabasePassword(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        // The URI form (postgresql://user:password@host/db) carries it in the user info.
        if (connectionString.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            var parts = uri.UserInfo.Split(':', 2);
            return parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null;
        }

        return ValueOf(connectionString, "Password", ';');
    }

    /// <summary>
    /// The password inside a Redis connection string: <c>redis:6379,password=...</c>, or the URI
    /// form <c>redis://:password@host:6379</c>.
    /// </summary>
    private static string? RedisPassword(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        if (connectionString.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            var parts = uri.UserInfo.Split(':', 2);
            return parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null;
        }

        return ValueOf(connectionString, "password", ',');
    }

    /// <summary>
    /// The value of one key in a <c>key=value</c> connection string, where the pairs are separated
    /// by the given character. A quoted value is unquoted, the way Npgsql reads it.
    /// </summary>
    private static string? ValueOf(string connectionString, string key, char separator)
    {
        foreach (var pair in connectionString.Split(separator))
        {
            var cut = pair.IndexOf('=');
            if (cut <= 0) continue;

            if (!pair[..cut].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = pair[(cut + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
            {
                value = value[1..^1];
            }

            return value;
        }

        return null;
    }
}
