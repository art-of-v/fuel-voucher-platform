using System.Text.Json;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

public sealed class MonobankClient : IMonobankClient
{
    /// <summary>Matches the options <c>ReadFromJsonAsync</c> would have used.</summary>
    private static readonly JsonSerializerOptions StatementJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MonobankOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MonobankClient> _logger;
    private readonly MonobankMerchant _merchant;

    /// <summary>
    /// A client bound to one merchant's credentials.
    /// </summary>
    /// <remarks>
    /// The token is passed in rather than read from <see cref="MonobankOptions.Token"/> so that a
    /// live and a sandbox client can exist side by side. Constructed by
    /// <c>MonobankClientFactory</c>; call sites should not build one directly.
    /// </remarks>
    public MonobankClient(
        IOptions<MonobankOptions> options,
        HttpClient httpClient,
        ILogger<MonobankClient> logger,
        MonobankCredentials credentials,
        MonobankMerchant merchant)
    {
        _options = options.Value;
        _merchant = merchant;
        _httpClient = httpClient;
        // Bound once, here: these are the merchant's identity for every call this client makes.
        _httpClient.BaseAddress = new Uri(credentials.BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("X-Token", credentials.Token);
        _logger = logger;
    }

    public async Task<MonobankInvoiceResponse> CreateInvoiceAsync(
        MonobankInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating Monobank invoice on the {Merchant} merchant for merchant payment ID {MerchantPaymentId}, amount {Amount} kopecks",
            _merchant,
            request.MerchantPaymentInfo,
            request.Amount);

        try
        {
            // merchantPaymInfo is built as a dictionary rather than an anonymous object because
            // basketOrder must be ABSENT when there is nothing to declare, not sent as an empty
            // array - Monobank treats the two differently once fiscalisation is linked.
            var merchantPaymInfo = new Dictionary<string, object?>
            {
                ["reference"] = request.MerchantPaymentInfo,
                // What the payment is for, as it appears on the receipt. Previously the same string
                // as `reference` ("FuelFlow Order 7258a522-..."), which printed an internal UUID as
                // the purchase description.
                ["destination"] = request.Destination ?? "╨ƒ╨░╨╗╨╕╨▓╨╛",
            };

            if (request.Items is { Count: > 0 })
            {
                merchantPaymInfo["basketOrder"] = request.Items;
            }

            var payload = new
            {
                amount = request.Amount,
                ccy = 980,
                merchantPaymInfo,
                redirectUrl = request.RedirectUrl ?? _options.RedirectUrl,
                webHookUrl = request.WebhookUrl ?? _options.WebhookUrl,
                validity = 3600,

                // Both of these exist so the customer never has to leave the app to pay, which is
                // what they were doing: Linking.openURL dropped them into Safari, and on a phone
                // that means the payment page renders as a website, not as part of the product.
                //
                // withAppUrl adds `appUrl`, an mbnk.app universal link that opens the Monobank app
                // on the confirmation screen. It also covers devices with no Monobank app (the link
                // falls back to the web), so it is the fallback for the in-app page below rather
                // than a separate code path.
                //
                // displayType:"iframe" switches pageUrl to the embeddable variant
                // (pay.monobank.ua/frame/...), which the app renders in its own payment screen.
                // Verified against the live merchant API: both flags together return
                // { pageUrl: ".../frame/<id>", appUrl: "https://mbnk.app/or/<id>" }.
                withAppUrl = true,
                displayType = "iframe"
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/merchant/invoice/create",
                payload,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Monobank invoice creation failed. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);

                throw new HttpRequestException(
                    $"Monobank API returned {response.StatusCode}: {errorContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<MonobankInvoiceResponse>(cancellationToken);

            if (result == null)
            {
                throw new InvalidOperationException("Monobank returned null response");
            }

            _logger.LogInformation(
                "Monobank invoice created successfully. InvoiceId: {InvoiceId}, PageUrl: {PageUrl}",
                result.InvoiceId,
                result.PageUrl);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Monobank invoice for {MerchantPaymentId}",
                request.MerchantPaymentInfo);
            throw;
        }
    }

    public async Task<MonobankInvoiceStatus> GetInvoiceStatusAsync(
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting Monobank invoice status for {InvoiceId}", invoiceId);

        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/merchant/invoice/status?invoiceId={invoiceId}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Monobank invoice status check failed. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);

                throw new HttpRequestException(
                    $"Monobank API returned {response.StatusCode}: {errorContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<MonobankInvoiceStatus>(cancellationToken);

            if (result == null)
            {
                throw new InvalidOperationException("Monobank returned null status response");
            }

            _logger.LogInformation(
                "Monobank invoice status retrieved: {InvoiceId}, Status: {Status}",
                invoiceId,
                result.Status);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Monobank invoice status for {InvoiceId}", invoiceId);
            throw;
        }
    }

    public async Task<MonobankCancelResponse> CancelInvoiceAsync(
        string invoiceId,
        long amountKopecks,
        string extRef,
        IReadOnlyList<MonobankBasketItem>? items = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Cancelling Monobank invoice {InvoiceId} for {Amount} kopecks (extRef {ExtRef}, {ItemCount} receipt line(s))",
            invoiceId,
            amountKopecks,
            extRef,
            items?.Count ?? 0);

        try
        {
            // `items` is the RETURN receipt: which goods are going back. Monobank documents it as
            // required when fiscalisation is active, and it is sent always for the same reason the
            // create path always sends basketOrder - the link can be switched on without telling us.
            // Omitted rather than sent empty, so behaviour is identical to before until a caller has
            // something to declare.
            var body = new Dictionary<string, object?>
            {
                ["invoiceId"] = invoiceId,
                ["extRef"] = extRef,
                ["amount"] = amountKopecks,
            };

            if (items is { Count: > 0 })
            {
                body["items"] = items;
            }

            var response = await _httpClient.PostAsJsonAsync(
                "/api/merchant/invoice/cancel",
                body,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Monobank invoice cancel failed. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);

                throw new HttpRequestException(
                    $"Monobank API returned {response.StatusCode}: {errorContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<MonobankCancelResponse>(cancellationToken);

            if (result == null)
            {
                throw new InvalidOperationException("Monobank returned null cancel response");
            }

            _logger.LogInformation(
                "Monobank invoice cancel accepted: {InvoiceId}, Status: {Status}",
                invoiceId,
                result.Status);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel Monobank invoice {InvoiceId}", invoiceId);
            throw;
        }
    }

    public async Task<IReadOnlyList<MonobankStatementItem>> GetStatementAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Fetching Monobank statement on the {Merchant} merchant for [{From} - {To})",
            _merchant, fromUtc, toUtc);

        try
        {
            // UTC unix timestamps, in seconds - what the acquirer documents for `from`/`to`. An
            // ISO-8601 instant is rejected with 400 {"errCode":"1001","errText":"invalid 'from'"},
            // which is what every import pass used to send: the statement was never fetched, and the
            // reconciliation act it feeds was permanently empty.
            //
            // toUtc is exclusive on our side and stays exclusive here - sending a bound that looks
            // inclusive is how one row lands in two consecutive periods and the month total comes
            // out double.
            var from = ToUnixSeconds(fromUtc);
            var to = ToUnixSeconds(toUtc);

            var response = await _httpClient.GetAsync(
                $"/api/merchant/statement?from={from}&to={to}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);

                // Deliberately not logged here: the catch below logs this once, with the status and
                // the acquirer's own error body already carried by the exception.
                throw new HttpRequestException(
                    $"Monobank API returned {response.StatusCode}: {errorContent}");
            }

            // The body is an envelope - {"list":[...]} - not the rows themselves, so the shape is
            // checked before it is trusted. A 200 we cannot read is NOT a period with nothing in it:
            // reading it as one would publish an act that quietly omits every payment reported.
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);

            if (document is null || document.RootElement.ValueKind == JsonValueKind.Null)
            {
                // A body that is not JSON at all, or a literal null. Nothing to file, and nothing that
                // contradicts us either - an empty period is the only reading that does not invent
                // payments. A body that IS JSON but is not the documented envelope is refused below.
                return [];
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("list", out var rowsElement)
                || rowsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "Monobank statement response carried no 'list' array; refusing to read it as an empty period");
            }

            // Read through JsonElement so each row keeps its original text. Re-serialising the parsed
            // object would give back the same fields in a different order and with the acquirer's
            // spelling already normalised away - good enough to read, not good enough to be the record
            // of what they actually sent.
            var rawRows = rowsElement.EnumerateArray().ToList();

            var items = new List<MonobankStatementItem>(rawRows.Count);
            foreach (var row in rawRows)
            {
                // JsonElement.Deserialize uses the case-SENSITIVE default options, while
                // ReadFromJsonAsync uses web defaults. Passing them explicitly keeps the camelCase
                // acquirer payload mapping onto the PascalCase properties - without this every field
                // silently deserialises to null.
                var item = row.Deserialize<MonobankStatementItem>(StatementJsonOptions);
                if (item is null)
                {
                    // One unusable row does not cost us the period. Skipping loudly is the point:
                    // silently dropping rows is how a reconciliation ends up quietly short.
                    _logger.LogWarning("Skipping an unparseable Monobank statement row: {Row}", row.GetRawText());
                    continue;
                }

                // The statement carries no id of its own, so the key comes from what it does send.
                var statementId = item.ComposeStatementId();
                if (statementId is null)
                {
                    // Nothing in the row names it, so a second such row would collide with it and the
                    // next import pass would overwrite one with the other.
                    _logger.LogWarning(
                        "Skipping a Monobank statement row that carries no identity: {Row}", row.GetRawText());
                    continue;
                }

                item.StatementId = statementId;
                item.RawJson = row.GetRawText();
                items.Add(item);
            }

            _logger.LogInformation(
                "Monobank statement fetched: {Count} rows for [{From} - {To})", items.Count, fromUtc, toUtc);

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Monobank statement for [{From} - {To})", fromUtc, toUtc);
            throw;
        }
    }

    /// <summary>
    /// UTC unix seconds, the unit the acquirer's <c>from</c>/<c>to</c> take.
    /// </summary>
    /// <remarks>
    /// <see cref="DateTime.SpecifyKind(DateTime, DateTimeKind)"/> rather than trusting
    /// <see cref="DateTime.Kind"/>: both bounds are built from the application's clock
    /// (<c>SystemClock.UtcNow</c>), so they are already UTC, and constructing a
    /// <see cref="DateTimeOffset"/> from a Utc DateTime is a no-op rather than a conversion through
    /// the server's local zone.
    /// </remarks>
    private static long ToUnixSeconds(DateTime utc)
        => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
