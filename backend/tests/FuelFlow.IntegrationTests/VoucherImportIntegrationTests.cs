using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using Xunit;

namespace FuelFlow.IntegrationTests;

[Collection("Integration Tests")]
public class VoucherImportIntegrationTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private readonly Mock<IQrDecoder> _qrDecoderMock = new();
    private string? _accessToken;

    public VoucherImportIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        // Default mock setup for QR Decoder
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<SixLabors.ImageSharp.Image>()))
            .Returns(new QrDecodeResult { Text = "9018$2000$;99999600000020368126=4507101299?", EccLevel = "L" });
    }

    private async Task AuthenticateAdminAsync(HttpClient client)
    {
        if (_accessToken != null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            return;
        }

        var phoneNumber = "+380991234568";

        var sendCodeResponse = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCodeResponse.EnsureSuccessStatusCode();

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var verificationCode = context.VerificationCodes
            .Where(vc => vc.PhoneNumber == phoneNumber)
            .OrderByDescending(vc => vc.CreatedAtUtc)
            .First();

        var verifyResponse = await client.PostAsJsonAsync("/api/auth/verify", new
        {
            phoneNumber,
            code = "000000"
        });

        verifyResponse.EnsureSuccessStatusCode();
        var verifyResult = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        _accessToken = verifyResult.GetProperty("accessToken").GetString();

        var adminRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == "Admin", CancellationToken.None);
        if (adminRole == null)
        {
            adminRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = "Admin",
                CreatedAtUtc = DateTime.UtcNow
            };
            context.Roles.Add(adminRole);
            await context.SaveChangesAsync();
        }

        var user = await context.Users.FirstAsync(u => u.PhoneNumber == phoneNumber, CancellationToken.None);
        user.RoleId = adminRole.Id;
        user.UpdatedAtUtc = DateTime.UtcNow;
        context.Update(user);
        await context.SaveChangesAsync();

        // Re-login to get token with Admin role claim
        var sendCodeResponse2 = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCodeResponse2.EnsureSuccessStatusCode();

        var verificationCode2 = context.VerificationCodes
            .Where(vc => vc.PhoneNumber == phoneNumber)
            .OrderByDescending(vc => vc.CreatedAtUtc)
            .First();

        var verifyResponse2 = await client.PostAsJsonAsync("/api/auth/verify", new
        {
            phoneNumber,
            code = "000000"
        });

        verifyResponse2.EnsureSuccessStatusCode();
        var verifyResult2 = await verifyResponse2.Content.ReadFromJsonAsync<JsonElement>();
        _accessToken = verifyResult2.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // No DbContext swap: TestDatabaseFixture publishes the container's connection string as
            // the Database__ConnectionString process env var before any host boots, which redirects
            // the DbContext, the singleton NpgsqlDataSource and Hangfire's storage together.
            // Overriding just the DbContext left the other two on localhost:5433.

            // Replace QrDecoder with our mock instance
            var qrDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IQrDecoder));
            if (qrDescriptor != null)
            {
                services.Remove(qrDescriptor);
            }
            services.AddSingleton<IQrDecoder>(_qrDecoderMock.Object);
        });
    }

    [Fact]
    public async Task ImportVouchers_ShouldSucceed_WhenPdfIsValidAndNew()
    {
        // Arrange
        var client = CreateClient();
        await AuthenticateAdminAsync(client);
        var pdfBytes = GenerateVoucherPdf("OKKO", "DP", 20, "17.06.2025", "99999600000020368126");

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "vouchers.pdf");

        // Act
        var response = await client.PostAsync("/api/voucher-catalog/import", content);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ImportVouchersResponse>();

        result.Should().NotBeNull();
        result!.Imported.Should().Be(1);
        result.Duplicates.Should().Be(0);
        result.Failed.Should().Be(0);

        // Verify DB values
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var voucher = await db.FuelVouchers.FirstOrDefaultAsync(v => v.VoucherNumber == "99999600000020368126");
        voucher.Should().NotBeNull();
        voucher!.Provider.Should().Be("OKKO");
        voucher.Liters.Should().Be(20m);
        voucher.ExpirationDate.Should().Be(new DateOnly(2025, 6, 17));
        voucher.QrPayload.Should().Be("9018$2000$;99999600000020368126=4507101299?");

        var importRecord = await db.VoucherImports.FindAsync(result.ImportId);
        importRecord.Should().NotBeNull();
        importRecord!.Status.Should().Be("Completed");
        importRecord.ImportedCount.Should().Be(1);
    }

    [Fact]
    public async Task ImportVouchers_ShouldTrackDuplicates_WhenUploadingSameVoucherTwice()
    {
        // Arrange
        var client = CreateClient();
        await AuthenticateAdminAsync(client);
        // Use a unique voucher number to avoid collision with previous test runs
        var voucherNum = "88888600000020368126";
        var pdfBytes = GenerateVoucherPdf("OKKO", "A95", 10, "20.12.2025", voucherNum);

        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<SixLabors.ImageSharp.Image>()))
            .Returns(new QrDecodeResult { Text = $"9018$2000$;{voucherNum}=4507101299?", EccLevel = "L" });

        // 1st Upload
        using (var content1 = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(pdfBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
            content1.Add(fileContent, "file", "vouchers1.pdf");
            var response1 = await client.PostAsync("/api/voucher-catalog/import", content1);
            response1.EnsureSuccessStatusCode();
        }

        // 2nd Upload (Duplicate)
        using (var content2 = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(pdfBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
            content2.Add(fileContent, "file", "vouchers2.pdf");

            // Act
            var response2 = await client.PostAsync("/api/voucher-catalog/import", content2);

            // Assert
            response2.EnsureSuccessStatusCode();
            var result2 = await response2.Content.ReadFromJsonAsync<ImportVouchersResponse>();

            result2.Should().NotBeNull();
            result2!.Imported.Should().Be(0);
            result2.Duplicates.Should().Be(1);
            result2.Failed.Should().Be(0);
        }
    }

    [Fact]
    public async Task ImportVouchers_ShouldRecordErrors_WhenVoucherIsInvalid()
    {
        // Arrange
        var client = CreateClient();
        await AuthenticateAdminAsync(client);
        // Generate an invalid voucher missing the voucher number (or non-matching number regex)
        var pdfBytes = GenerateVoucherPdf("OKKO", "LPG", 50, "15.08.2025", "INVALID_NUM");

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "vouchers_invalid.pdf");

        // Act
        var response = await client.PostAsync("/api/voucher-catalog/import", content);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ImportVouchersResponse>();

        result.Should().NotBeNull();
        result!.Imported.Should().Be(0);
        result.Duplicates.Should().Be(0);
        result.Failed.Should().Be(1);

        // #22: the failure reason is surfaced inline in the response, not just counted,
        // so the admin UI can show WHY a row failed.
        result.Errors.Should().NotBeNull();
        result.Errors!.Should().HaveCount(1);
        result.Errors[0].PageNumber.Should().Be(1);
        result.Errors[0].Reason.Should().Contain("failed validation");

        // Verify Import Error is stored
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var error = await db.VoucherImportErrors.FirstOrDefaultAsync(e => e.ImportId == result.ImportId);
        error.Should().NotBeNull();
        error!.ErrorMessage.Should().Contain("failed validation");
        error.PageNumber.Should().Be(1);
    }

    [Fact]
    public async Task ImportVouchers_ShouldImportWogVoucher_AndResolveFuelType()
    {
        // Regression for the production 500: a WOG voucher must import end-to-end (through the
        // real FK-enforcing Postgres) and resolve its fuel type from OCR text via the canonical
        // category space. "A-95" with no Mustang marker → the standard (EURO) 95 grade.
        var client = CreateClient();
        await AuthenticateAdminAsync(client);

        var voucherNum = "10094200095062108351";
        // WOG QR carries the voucher number only (no '$' product code), unlike OKKO.
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<SixLabors.ImageSharp.Image>()))
            .Returns(new QrDecodeResult { Text = voucherNum, EccLevel = "L" });

        var pdfBytes = GenerateVoucherPdf("WOG", "A-95", 20, "09.02.2026", voucherNum);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "wog_vouchers.pdf");

        var response = await client.PostAsync("/api/voucher-catalog/import", content);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ImportVouchersResponse>();

        result.Should().NotBeNull();
        result!.Imported.Should().Be(1);
        result.Duplicates.Should().Be(0);
        result.Failed.Should().Be(0);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var voucher = await db.FuelVouchers.FirstOrDefaultAsync(v => v.VoucherNumber == voucherNum);
        voucher.Should().NotBeNull();
        voucher!.Provider.Should().Be("WOG");
        voucher.FuelTypeId.Should().Be("wog-95-euro");
        voucher.Liters.Should().Be(20m);
        voucher.ExpirationDate.Should().Be(new DateOnly(2026, 2, 9));
    }

    [Fact]
    public async Task ImportVouchers_ShouldRejectWogGracefully_WhenFuelTypeUnresolvable()
    {
        // Regression for the true production flip: the old parser fell back to a hardcoded
        // "wog-dp" id for an unresolvable fuel, which silently imported the row as diesel and —
        // when "wog-dp" was absent from a drifted catalog — tripped the FK and 500'd the whole
        // import. The fix must instead return a graceful per-row error (HTTP 200, Failed=1,
        // Imported=0) with a clear reason and NOT persist any voucher.
        var client = CreateClient();
        await AuthenticateAdminAsync(client);

        var voucherNum = "10094200095062109999";
        _qrDecoderMock.Setup(x => x.Decode(It.IsAny<SixLabors.ImageSharp.Image>()))
            .Returns(new QrDecodeResult { Text = voucherNum, EccLevel = "L" });

        var pdfBytes = GenerateVoucherPdf("WOG", "ZZZ", 20, "09.02.2026", voucherNum);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "wog_unresolvable.pdf");

        var response = await client.PostAsync("/api/voucher-catalog/import", content);

        // The whole request must still succeed (200) — a bad row is a per-row error, not a 500.
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ImportVouchersResponse>();

        result.Should().NotBeNull();
        result!.Imported.Should().Be(0);
        result.Duplicates.Should().Be(0);
        result.Failed.Should().Be(1);

        result.Errors.Should().NotBeNull();
        result.Errors!.Should().ContainSingle();
        result.Errors[0].Reason.Should().Contain("failed validation");
        result.Errors[0].Reason.Should().Contain("Fuel type could not be determined");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.FuelVouchers.AnyAsync(v => v.VoucherNumber == voucherNum)).Should().BeFalse();
    }

    private static byte[] GenerateVoucherPdf(string provider, string fuel, decimal liters, string date, string number)
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        // Position words closely to form a single cluster mimicking a voucher at (50, 100) region
        page.AddText(provider, 10, new PdfPoint(50, 100), font);
        page.AddText(fuel, 10, new PdfPoint(60, 80), font);
        page.AddText($"{liters} l", 10, new PdfPoint(70, 60), font);
        page.AddText($"Valid until {date}", 10, new PdfPoint(70, 40), font);
        page.AddText(number, 10, new PdfPoint(20, 20), font);

        return builder.Build();
    }
}
