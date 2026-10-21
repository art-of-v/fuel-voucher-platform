using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The below-cost gate asks about the fuel that is IN STOCK, and about nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <c>deploy/preflight-db/017-fuels-without-a-supplier-cost.sql</c> names the fuels whose
/// supplier cost or margin is missing, so that nobody sells a litre below cost by accident.
/// It used to ask about every row in <c>fuel_types</c> - the whole catalogue - and the
/// catalogue is not the warehouse.
/// </para>
/// <para>
/// The rule it now follows is the one the application already follows: <c>allow_below_cost</c> is
/// consulted where a sale happens, and a sale needs stock. <c>CreateCheckoutCommandHandler</c>
/// checks the stock and throws <c>PackageOutOfStockException</c> BEFORE the below-cost verdict is
/// asked, and <c>UnknownCostSaleBlockedException</c> says in its own words that a fuel
/// <em>that is in stock</em> and has no recorded cost cannot be sold. A fuel with no stock
/// cannot be sold at all, so nothing about its cost can cost us money - it has a price in the
/// columns and no vouchers behind it, which is a catalogue entry and not a product.
/// </para>
/// <para>
/// The difference was not academic. Measured on production 2026-10-21: the catalogue holds 23
/// fuels and the warehouse holds ZERO vouchers, so all 23 were a false positive - 46 reported
/// rows naming fuel that cannot be sold. With <c>PREFLIGHT_DB_STRICT=1</c> that blocked every
/// deploy, and it did so on a table with nothing to say: five consecutive production deploys
/// failed on this file alone, from 07:43 to 09:20, while production served 200s throughout.
/// </para>
/// <para>
/// So this test runs the file's own SQL against a database with fuel in it, and asserts both
/// halves of the rule: the fuel WITH stock is named, and the fuel WITHOUT stock is not.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class TheBelowCostGateAsksAboutStock : IClassFixture<TestDatabaseFixture>
{
    private const string PreconditionFile = "017-fuels-without-a-supplier-cost.sql";

    private readonly TestDatabaseFixture _fixture;

    public TheBelowCostGateAsksAboutStock(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_fuel_without_stock_is_not_named()
    {
        var (_, _, withoutStock) = await SeedAsync(cost: null, margin: null, withStock: false);

        var reported = await RunPreconditionAsync();

        reported.Should().NotContain(withoutStock.ToString(),
            "a fuel with no stock cannot be sold, so the below-cost guard cannot cost us money on it");
    }

    [Fact]
    public async Task A_fuel_with_stock_and_no_cost_is_named()
    {
        var (_, withStock, _) = await SeedAsync(cost: null, margin: null, withStock: true);

        var reported = await RunPreconditionAsync();

        reported.Should().Contain(withStock.ToString(),
            "a fuel that is in stock and has no recorded cost cannot be sold, and that is a real problem");
    }

    [Fact]
    public async Task A_fuel_with_stock_and_a_cost_and_a_margin_is_not_named()
    {
        var (_, withStock, _) = await SeedAsync(cost: 50m, margin: 3m, withStock: true);

        var reported = await RunPreconditionAsync();

        reported.Should().NotContain(withStock.ToString(),
            "the guard can do its job on a fuel whose cost and margin are both known");
    }

    [Fact]
    public async Task A_fuel_with_stock_and_no_margin_is_named()
    {
        var (_, withStock, _) = await SeedAsync(cost: 50m, margin: null, withStock: true);

        var reported = await RunPreconditionAsync();

        reported.Should().Contain(withStock.ToString(),
            "a cost with no margin leaves the price with nothing to add to it, so the guard is skipped");
    }

    /// <summary>
    /// The file's own SQL, run against this class's database, and the text it reported.
    /// </summary>
    /// <remarks>
    /// The file, not a paraphrase of it: a gate that is only re-implemented in the test is a second
    /// gate, and the two drift - which is how a check comes to say something the check does not do.
    /// The psql client directives are stripped exactly as <see cref="PreflightSqlExecutesTests"/>
    /// strips them, because they are a client feature and not SQL.
    /// </remarks>
    private async Task<string> RunPreconditionAsync()
    {
        var path = Path.Combine(RepoRoot(), "deploy", "preflight-db", PreconditionFile);
        var sql = StripPsqlDirectives(File.ReadAllText(path));

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("SET TRANSACTION READ ONLY;\n" + sql, connection, transaction);

        var reported = new System.Text.StringBuilder();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            // Every result set, not just the first. The file is more than one statement, and
            // Npgsql exposes the first set and leaves the rest to `NextResultAsync` - so a reader
            // that stopped at the end of the first set would see the file's own housekeeping and
            // none of its findings.
            do
            {
                while (await reader.ReadAsync())
                {
                    // The rows are joined with a PIPE, and only the rows that carry one are
                    // counted - the same rule the deploy runner applies (`grep -c '|'` over
                    // psql's `-F'|'` output). It is what separates a reported violation from the
                    // file's housekeeping: the `\gset` line that asks whether the table exists
                    // produces a row of its own, and the runner does not count it as a finding
                    // either.
                    var line = string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue));
                    if (line.Contains('|'))
                    {
                        reported.AppendLine(line);
                    }
                }
            }
            while (await reader.NextResultAsync());
        }

        // The reader is closed before the rollback: a command still in progress on the connection
        // makes the rollback fail with "a command is already in progress", which is a fact about
        // the test's own bookkeeping and not about the file under test.
        await transaction.RollbackAsync();
        return reported.ToString();
    }

    /// <summary>
    /// The psql directives removed, so the file runs as plain SQL against Npgsql.
    /// </summary>
    private static string StripPsqlDirectives(string fileText)
    {
        var sql = new System.Text.StringBuilder();
        var skippingElseBranch = false;

        foreach (var raw in fileText.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith('\\'))
            {
                skippingElseBranch = trimmed.StartsWith("\\else", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (skippingElseBranch)
            {
                continue;
            }

            var gset = trimmed.IndexOf("\\gset", StringComparison.Ordinal);
            if (gset >= 0)
            {
                line = line[..gset].TrimEnd();
                if (!line.EndsWith(';'))
                {
                    line += ";";
                }
            }

            var statement = line.TrimStart();
            if (statement.StartsWith("BEGIN;", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("COMMIT;", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("SET TRANSACTION READ ONLY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sql.AppendLine(line);
        }

        return sql.ToString();
    }

    /// <summary>
    /// Two fuels on one brand: one with stock behind it, one without, plus the state each is in.
    /// </summary>
    private async Task<(Guid BrandId, Guid WithStock, Guid WithoutStock)> SeedAsync(
        decimal? cost,
        decimal? margin,
        bool withStock)
    {
        var brandId = Guid.NewGuid();
        var slug = $"slug-{Guid.NewGuid():N}"[..20];
        var stockedId = Guid.NewGuid();
        var bareId = Guid.NewGuid();

        await using var context = CreateContext();
        context.Brands.Add(new Brand
        {
            Id = brandId,
            Slug = slug,
            Name = "TEST",
            LogoText = "TEST",
            Color = "#000",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = stockedId,
            BrandId = brandId,
            Name = "А-95",
            SupplierPricePerLiter = cost,
            MarginUahPerLiter = margin,
            PumpPricePerLiter = 92.90m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = bareId,
            BrandId = brandId,
            Name = "ДП",
            SupplierPricePerLiter = cost,
            MarginUahPerLiter = margin,
            PumpPricePerLiter = 101.90m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        if (withStock)
        {
            context.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.NewGuid(),
                BrandId = brandId,
                Brand = slug,
                FuelTypeId = stockedId,
                Liters = 10m,
                Status = VoucherStatus.Available,
                DataKind = DataKinds.Real,
                SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
                VoucherNumber = $"V-{Guid.NewGuid():N}",
                QrPayload = $"9018$500$;{Guid.NewGuid():N}",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
        return (brandId, stockedId, bareId);
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FuelFlow.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("could not locate the backend root from " + AppContext.BaseDirectory);
        return dir!.Parent!.FullName;
    }
}
