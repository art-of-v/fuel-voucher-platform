using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The redeemable payload of a voucher is a bearer instrument: whoever holds it can spend the
/// customer's fuel at the pump, and nothing records that they did. It is therefore its own
/// capability, and it is narrower than the money a Manager may move - a refund is an audited row,
/// a redemption is neither.
/// </summary>
/// <remarks>
/// The bulk leak is the one that mattered: the admin LIST used to carry the payload of every row,
/// so one staff call handed out every live voucher's code. The list does not carry it at all now -
/// the operator needs the number, the status and the expiry.
/// </remarks>
[Collection("Integration Tests")]
public sealed class VoucherPayloadCapabilityTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public VoucherPayloadCapabilityTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_Admin_List_Carries_No_Redeemable_Payload()
    {
        await SeedVoucherAsync();
        var admin = await StaffClientAsync("+15550009101", SeedRoles.AdminName);

        var response = await admin.GetAsync("/api/admin/vouchers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var rows = document.RootElement.GetProperty("data");
        rows.GetArrayLength().Should().BeGreaterThan(0, "the fixture seeded a voucher");

        foreach (var row in rows.EnumerateArray())
        {
            row.TryGetProperty("qrPayload", out _).Should().BeFalse(
                "one list call must not hand out every live voucher's redeemable code");
        }
    }

    [Fact]
    public async Task A_Manager_Sees_The_Voucher_But_Not_Its_Code()
    {
        var voucherId = await SeedVoucherAsync();
        var manager = await StaffClientAsync("+15550009102", SeedRoles.ManagerName);

        var response = await manager.GetAsync($"/api/admin/vouchers/{voucherId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(detail);
        var root = document.RootElement;

        root.GetProperty("qrPayload").ValueKind.Should().Be(JsonValueKind.Null,
            "the redeemable code leaves the process only for the roles that hold the capability");
        root.GetProperty("qrImage").ValueKind.Should().Be(JsonValueKind.Null);

        // The rest of the detail is the operator's: what the voucher is, what it cost, when it
        // expires. Hiding the code is not hiding the voucher.
        root.GetProperty("voucherNumber").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("status").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task An_Admin_Sees_The_Code()
    {
        var voucherId = await SeedVoucherAsync();
        var admin = await StaffClientAsync("+15550009103", SeedRoles.AdminName);

        var response = await admin.GetAsync($"/api/admin/vouchers/{voucherId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(detail);
        var root = document.RootElement;

        root.GetProperty("qrPayload").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("qrImage").GetString().Should().StartWith("data:image/png;base64,");
    }

    [Fact]
    public async Task A_Manager_Keeps_The_Decode_Integrity_Numbers_And_Not_The_Image()
    {
        // The response's image IS the redeemable code, so it leaves the process only for the roles
        // that hold the capability. The numbers are the matrix check's result - and an operator
        // checking a printed voucher needs them, together with the statement that they do not prove
        // who issued it.
        var voucherId = await SeedVoucherAsync();
        var manager = await StaffClientAsync("+15550009104", SeedRoles.ManagerName);

        var response = await manager.GetAsync($"/api/admin/vouchers/{voucherId}/decode-integrity");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        root.GetProperty("qrCodeBase64").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("voucherNumber").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("decodeIntegrity").GetString().Should().Be("NotChecked");
        root.GetProperty("supplierProvenance").GetString().Should().Be("Unverified");
    }

    private async Task<Guid> SeedVoucherAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = TestClock.UtcNow;

        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50,
            SupplierExpirationDate = DateOnly.FromDateTime(now).AddMonths(1),
            CustomerExpirationDate = DateOnly.FromDateTime(now).AddMonths(1),
            VoucherNumber = $"OKKO-PAYLOAD-{Guid.NewGuid():N}"[..40],
            QrPayload = $"payload-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.FuelVouchers.Add(voucher);
        await context.SaveChangesAsync();
        return voucher.Id;
    }

    /// <summary>
    /// A user with the given role, created with it - so the token the login mints carries the role
    /// claim the capability policies check.
    /// </summary>
    private async Task SeedUserAsync(string phone, string roleName)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = await context.Roles.FirstAsync(r => r.Name == roleName);
        var now = TestClock.UtcNow;

        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            RoleId = role.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            TokenVersion = 1,
        });
        await context.SaveChangesAsync();
    }

    private async Task<HttpClient> StaffClientAsync(string phone, string roleName)
    {
        await SeedUserAsync(phone, roleName);

        var client = _fixture.CreateClient();

        var sendCode = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber = phone });
        sendCode.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber = phone, code = "000000" });
        verify.EnsureSuccessStatusCode();
        var token = (await verify.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
