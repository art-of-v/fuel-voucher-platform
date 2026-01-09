using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FuelFlow.Api.Infrastructure.Services;

public interface IGeminiService
{
    Task<List<GeminiVoucherResult>> AnalyzePdfAsync(Stream fileStream, string mimeType);
}

public class GeminiVoucherResult
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("fuelType")]
    public string FuelType { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("expirationDate")]
    public DateTime? ExpirationDate { get; set; }

    [JsonPropertyName("externalId")]
    public string ExternalId { get; set; } = string.Empty;

    [JsonIgnore]
    public string RawResponse { get; set; } = string.Empty;
}

public class GeminiService : IGeminiService
{
    private readonly string? _apiKey;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(IConfiguration configuration, HttpClient httpClient, ILogger<GeminiService> logger)
    {
        _apiKey = configuration["GEMINI_API_KEY"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<GeminiVoucherResult>> AnalyzePdfAsync(Stream fileStream, string mimeType)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("GEMINI_API_KEY is missing. Returning empty result.");
            return new List<GeminiVoucherResult>();
        }

        // Convert stream to base64
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream);
        var base64Data = Convert.ToBase64String(memoryStream.ToArray());

        var modelsToTry = new[] { "gemini-1.5-flash", "gemini-1.5-pro" };
        
        foreach (var model in modelsToTry)
        {
            try 
            {
                return await AnalyzeWithModelAsync(model, base64Data, mimeType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze with model {Model}. Retrying...", model);
            }
        }

        _logger.LogError("All Gemini models failed.");
        return new List<GeminiVoucherResult>();
    }

    private async Task<List<GeminiVoucherResult>> AnalyzeWithModelAsync(string model, string base64Data, string mimeType)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";

        var prompt = @"
Analyze this PDF containing fuel vouchers from OKKO gas stations.
Extract ALL vouchers from ALL pages.

For each voucher, extract:
- ""provider"": Brand name (usually ""OKKO"")
- ""fuelType"": Fuel name in original Cyrillic (e.g. ""ДП ЄВРО"", ""А-95"", ""PULLS 95"")
- ""amount"": Number of liters (integer)
- ""expirationDate"": Valid until date (YYYY-MM-DD format)
- ""externalId"": The long numeric code PRINTED under the QR code (e.g. ""9999960000018383454"")
 
CRITICAL RULES FOR externalId:
1. Extract EVERY SINGLE DIGIT exactly as printed - do NOT skip or add digits
2. Count the leading 9s carefully (usually 6 nines: ""999999"")
3. The full ID is typically 19 digits long
4. Double-check each digit - OCR errors are NOT acceptable
5. If unsure about a digit, examine the image more carefully
6. Preserve Cyrillic characters exactly (ДП ЄВРО, not ""DP EVRO"")
7. Return ONLY valid JSON array - no markdown, no explanations
8. Include ALL vouchers from ALL pages in the PDF
 
Example output:
[
  { 
    ""provider"": ""OKKO"", 
    ""fuelType"": ""ДП ЄВРО"", 
    ""amount"": 10, 
    ""expirationDate"": ""2026-01-06"", 
    ""externalId"": ""9999960000018383454""
  }
]
";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt },
                        new 
                        { 
                            inline_data = new 
                            { 
                                mime_type = mimeType, 
                                data = base64Data 
                            } 
                        }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.1
            }
        };

        var response = await _httpClient.PostAsJsonAsync(url, requestBody);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Gemini API error: {response.StatusCode} - {error}");
        }

        var jsonResponse = await response.Content.ReadFromJsonAsync<GeminiApiResponse>();
        var text = jsonResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        if (string.IsNullOrEmpty(text))
        {
            return new List<GeminiVoucherResult>();
        }

        // Extract JSON from response
        var jsonStart = text.IndexOf('[');
        var jsonEnd = text.LastIndexOf(']');
        
        if (jsonStart < 0 || jsonEnd < 0)
        {
             // Try finding single object
             jsonStart = text.IndexOf('{');
             jsonEnd = text.LastIndexOf('}');
             if (jsonStart >= 0 && jsonEnd >= 0)
             {
                 var singleJson = text.Substring(jsonStart, jsonEnd - jsonStart + 1);
                 var singleResult = JsonSerializer.Deserialize<GeminiVoucherResult>(singleJson);
                 return singleResult != null ? new List<GeminiVoucherResult> { singleResult } : new List<GeminiVoucherResult>();
             }
             return new List<GeminiVoucherResult>();
        }

        var cleanJson = text.Substring(jsonStart, jsonEnd - jsonStart + 1);
        try 
        {
            var results = JsonSerializer.Deserialize<List<GeminiVoucherResult>>(cleanJson);
            return results ?? new List<GeminiVoucherResult>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Gemini JSON response");
            return new List<GeminiVoucherResult>();
        }
    }

    private class GeminiApiResponse
    {
        [JsonPropertyName("candidates")]
        public List<Candidate>? Candidates { get; set; }
    }

    private class Candidate
    {
        [JsonPropertyName("content")]
        public Content? Content { get; set; }
    }

    private class Content
    {
        [JsonPropertyName("parts")]
        public List<Part>? Parts { get; set; }
    }

    private class Part
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
