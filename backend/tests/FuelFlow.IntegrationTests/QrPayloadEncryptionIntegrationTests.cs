using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Security;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// What the <c>qr_payload</c> column actually holds, proven against a real PostgreSQL.
/// </summary>
/// <remarks>
/// The cipher's own properties are unit-tested; what cannot be proven in memory is that the bytes
/// arriving in the database are the cipher's and not the application's string. These run through the
/// container because that is the only place the migration, the column type and the value converter
/// all exist together.
/// </remarks>
[Collection("Integration Tests")]
public sealed class QrPayloadEncryptionIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public QrPayloadEncryptionIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static FuelVoucher NewVoucher(string qr) => new()
    {
        Id = Guid.CreateVersion7(),
        Brand = "okko",
        FuelTypeId = ToUuid("okko-95"),
        Liters = 10m,
        SupplierExpirationDate = new DateOnly(2027, 1, 1),
        CustomerExpirationDate = new DateOnly(2027, 1, 1),
        VoucherNumber = Guid.NewGuid().ToString("N")[..12],
        QrPayload = qr,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task A_Saved_Payload_Comes_Back_Unchanged()
    {
        const string qr = "BRN:4400720039202603121834256789ZZ4000123456789";
        var voucher = NewVoucher(qr);

        await using (var seed = CreateContext())
        {
            seed.FuelVouchers.Add(voucher);
            await seed.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucher.Id);

        stored.QrPayload.Should().Be(qr, "the value converter is meant to be invisible to callers");
    }

    [Fact]
    public async Task The_Column_Holds_Ciphertext_Not_The_Payload()
    {
        const string qr = "BRN-QR-PAYLOAD-VISIBLE-IN-THE-COLUMN";
        var voucher = NewVoucher(qr);

        await using (var seed = CreateContext())
        {
            seed.FuelVouchers.Add(voucher);
            await seed.SaveChangesAsync();
        }

await using var raw = CreateContext();

// Read the column as raw bytes, bypassing the model entirely. If the converter were missing - or if
// the value were merely base64 - this is where it would show. A real NpgsqlParameter rather than
// EF's {0} form, because EF rewrites those into column references inside a SELECT list.
await using var command = raw.Database.GetDbConnection().CreateCommand();
command.CommandText = "SELECT qr_payload FROM fuel_vouchers WHERE id = @id";
var idParam = command.CreateParameter();
idParam.ParameterName = "id";
idParam.Value = voucher.Id;
command.Parameters.Add(idParam);

await raw.Database.OpenConnectionAsync();
var rawBytes = (byte[])(await command.ExecuteScalarAsync())!;
await raw.Database.CloseConnectionAsync();

rawBytes.Should().NotBeEmpty();
        System.Text.Encoding.UTF8.GetString(rawBytes).Should().NotContain(qr);
        rawBytes.Length.Should().BeGreaterThan(qr.Length,
            "AES-GCM output carries a nonce and a tag, so it is longer than the plaintext");
    }

    [Fact]
    public async Task The_Fingerprint_Is_Written_Without_Anyone_Asking()
    {
        // The value generator's whole reason for existing: a writer that forgets the hash cannot
        // insert a voucher without a fingerprint, because the column is NOT NULL and unique.
        const string qr = "BRN-FINGERPRINT-CHECK";
        var voucher = NewVoucher(qr);

        await using (var seed = CreateContext())
        {
            seed.FuelVouchers.Add(voucher);
            await seed.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucher.Id);

        stored.QrPayloadSha256.Should().NotBeNull();
        stored.QrPayloadSha256.Should().Equal(TestQrCipher.Instance.Sha256(qr));
    }

    [Fact]
    public async Task Two_Vouchers_With_The_Same_Qr_Are_Refused()
    {
        // The guarantee the old index made on the plaintext column, preserved on the hash.
        const string qr = "BRN-DUPLICATE-QR-PAYLOAD";
        var first = NewVoucher(qr);
        var second = NewVoucher(qr);
        second.VoucherNumber = first.VoucherNumber + "-B";

        await using (var seed = CreateContext())
        {
            seed.FuelVouchers.Add(first);
            await seed.SaveChangesAsync();
        }

        await using var second1 = CreateContext();
        second1.FuelVouchers.Add(second);

        var act = async () => await second1.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "the same paper imported twice under two numbers is what the index exists to prevent");
    }
}