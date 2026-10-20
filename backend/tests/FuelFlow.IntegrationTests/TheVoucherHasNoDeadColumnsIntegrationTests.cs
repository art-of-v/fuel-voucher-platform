using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The voucher carries no subtype, no redemption rules and no stored image - and it never did.
/// </summary>
/// <remarks>
/// <para>
/// <c>fuel_vouchers.fuel_subtype</c>, <c>redemption_rules</c> and <c>image_url</c> were never
/// written by any code path in the product. The customer sees a voucher picture, but it is
/// rendered on the fly from the QR payload; the column was not it. The only read of
/// <c>image_url</c> was a fallback for a voucher with no QR payload, and it could only ever
/// produce null. The other two columns had no reader at all.
/// </para>
/// <para>
/// <b>Why a test and not just a migration.</b> A migration says the columns are gone today. A
/// column that is always null is still an invitation: the next reader sees a subtype, a set of
/// rules, an image, and assumes somebody maintains them. The only thing that stops the next
/// reader from adding a writer is a rule that says these three are not a concept the product
/// has - so the rule is here, and it is checked in the schema and in the model at once.
/// </para>
/// <para>
/// The response field <c>imageUrl</c> stays: it carries the rendered QR picture, which is a
/// different thing from the column that went. The DTO must not grow a subtype or rules back.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class TheVoucherHasNoDeadColumnsIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private static readonly string[] DeadColumns = ["fuel_subtype", "redemption_rules", "image_url"];

    private readonly TestDatabaseFixture _fixture;

    public TheVoucherHasNoDeadColumnsIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_Voucher_Table_Has_No_Subtype_And_No_Rules_And_No_Stored_Image()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name
              FROM information_schema.columns
             WHERE table_schema = current_schema()
               AND table_name = 'fuel_vouchers'
               AND column_name = ANY (@dead)
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "dead";
        parameter.Value = DeadColumns;
        command.Parameters.Add(parameter);

        var found = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            found.Add(reader.GetString(0));
        }

        found.Should().BeEmpty(
            "a column nobody writes is an invitation to the next reader to believe there is something there");
    }

    [Fact]
    public async Task The_Voucher_Model_Has_No_Property_For_Them()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var entityType = context.Model.FindEntityType(typeof(FuelFlow.Features.Vouchers.FuelVoucher))!;

        entityType.FindProperty("FuelSubtype").Should().BeNull("the product has no notion of a voucher subtype");
        entityType.FindProperty("RedemptionRules").Should().BeNull("the product has no per-voucher redemption rules");
        entityType.FindProperty("ImageUrl").Should().BeNull("the voucher picture is rendered from the QR payload, not stored");
    }

    [Fact]
    public async Task The_Customer_Facing_Voucher_Carries_No_Subtype_And_No_Rules()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var dtoType = typeof(FuelFlow.Features.Vouchers.GetUserVouchers.VoucherDto);

        dtoType.GetProperty("FuelSubtype").Should().BeNull("there is no subtype to report");
        dtoType.GetProperty("RedemptionRules").Should().BeNull("there are no rules to report");
        dtoType.GetProperty("ImageUrl").Should().NotBeNull("this one is the rendered QR picture, and it stays");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
