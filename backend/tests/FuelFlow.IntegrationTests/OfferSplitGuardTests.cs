using FluentAssertions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>SplitFuelPackagesIntoOffers</c> scoped its guard and its backfill to live rows. Read from the
/// migration file, because a guard that is right in a test and wrong in a migration is exactly what
/// happened here.
/// </summary>
/// <remarks>
/// <para>
/// Two properties are asserted, and they were bought with production outages. The guard already
/// excluded archived rows, while the backfill did not — so 14 archived package rows came across, 11
/// of them sharing (offer, litres) with a live volume, and <c>ux_fuel_offer_volumes_offer_liters</c>
/// took the API down for the second time in one day. Two adjacent statements, one filter.
/// </para>
/// <para>
/// These were static assertions and stayed static. Seven behavioural tests alongside them manufactured
/// drift and ran the guard's own DO-block, which needs a live <c>fuel_packages</c> — a table that no
/// longer exists. See the note at the bottom of this file for what covers the guard's behaviour now.
/// </para>
/// </remarks>
public sealed class OfferSplitGuardTests
{
    [Fact]
    public void TheMigrationIsScopedToLiveRows()
    {
        // The structural assertion behind the outage. The guard refuses on a group whose LIVE volumes
        // disagree and ignores archived ones — because an archived volume carries the price the fuel
        // had before the last reprice (BrandsController.UpdateNominals), so it is history rather than
        // a competing price.
        //
        // Read with the comments stripped, and "comments" means both flavours: the migration embeds
        // its SQL in a C# raw string, so its prose is marked `//`, while the preflight file is real SQL
        // and uses `--`. Stripping only one of them is what made this assertion pass with the filter
        // deleted — because the migration's own comment explains the filter and quotes it. A check
        // that passes on the change it exists to catch is worse than no check, and the bash sibling in
        // preflight-db.test.sh made the same mistake first, for the same reason.
        var sql = CodeOnly(File.ReadAllText(FindMigrationFile()));

        sql.Should().Contain("WHERE NOT is_deleted",
            "grouping over archived rows is what aborted the 2026-10-15 production deploy");

        // The preflight that used to answer the same question is gone: `010` asked whether a volume's
        // total disagreed with its offer, and `PackagePriceOverrideOnNominals` (2026-10-17) removed the
        // stored total, so there is nothing left to disagree — the price is computed at read time by
        // `FuelPricing.PackagePrice`, and the shelf and the charge call the same function. Nothing took
        // its place, because a drift between one number and another is no longer representable. The
        // archived-row scope below therefore belongs to no file, and asserting it against one that is
        // gone would only assert that a path exists. See deploy/preflight-db/README.md.
    }

    [Fact]
    public void TheBackfillIsScopedToLiveRows_Too()
    {
        // The guard was already scoped to live rows when this migration shipped, and the backfill was
        // not. So both are asserted against the file, with the comments stripped.
        var sql = CodeOnly(File.ReadAllText(FindMigrationFile()));

        // Every statement that reads fuel_packages must exclude archived rows: the guard (for price
        // disagreement) and the two INSERTs (for the backfill itself).
        var statements = sql.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.Contains("FROM fuel_packages", StringComparison.Ordinal))
            .ToList();

        statements.Should().HaveCount(3, "one guard and two backfill INSERTs read fuel_packages");

        statements.Should().OnlyContain(
            s => s.Contains("WHERE NOT is_deleted", StringComparison.Ordinal)
                 || s.Contains("WHERE NOT p.is_deleted", StringComparison.Ordinal),
            "every read of fuel_packages in this migration must exclude archived rows");
    }

    // The seven behavioural tests that ran this guard against a live `fuel_packages` are gone with
    // the table. They manufactured drift, ran the guard's own DO-block, and asserted what it
    // refused — only possible while the table that block reads exists. Keeping them would mean
    // rolling the schema back to before DropFuelPackages for each one, turning seven focused
    // assertions into a test of the migration chain as much as of the guard.
    //
    // What they protected is not uncovered. `SplitFuelPackagesIntoOffers` has shipped to every
    // environment and cannot run again, so what is left to protect is the check that runs BEFORE
    // every future deploy — now a different question (a volume against its offer, not two rows of
    // one group) and covered end to end, against the exact file the deploy executes, by
    // `OfferVolumePriceAgreementTests`.

    // -- helpers ------------------------------------------------------------

    private static string RepoRoot()
    {
        // The anchor is FuelFlow.slnx, which lives in backend/ — so it identifies the BACKEND root,
        // not the repository root, and deploy/ is one level above it. Walking up to the solution and
        // stopping there finds no deploy directory, which is how this test first failed with "file
        // not found" rather than with a wrong answer.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FuelFlow.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("could not locate the backend root from " + AppContext.BaseDirectory);

        var repoRoot = dir!.Parent;
        repoRoot.Should().NotBeNull("the backend root has a parent, which is the repository root");
        return repoRoot.FullName;
    }

    private static string CodeOnly(string text) => string.Join(
        Environment.NewLine,
        text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("--"))
            .Select(l => l.Trim()));

    private static string FindRepoFile(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue($"the preflight executes {relativePath}, so it must exist");
        return path;
    }

    private static string FindMigrationFile()
    {
        var migrations = Path.Combine(
            RepoRoot(), "backend", "src", "FuelFlow.API", "Migrations");

        var file = Directory.EnumerateFiles(migrations, "*SplitFuelPackagesIntoOffers.cs").FirstOrDefault();
        file.Should().NotBeNull("the migration this test audits must still be on disk");
        return file!;
    }
}
