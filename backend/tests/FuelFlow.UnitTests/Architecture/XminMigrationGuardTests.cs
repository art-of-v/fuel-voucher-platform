using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// Architecture guard for the <c>xmin</c> concurrency token.
/// </summary>
/// <remarks>
/// <para>
/// <c>xmin</c> is a Postgres <b>system column</b>: it exists on every row of every table, and it is
/// the transaction id that last touched the row. There is no column to create, so no migration may
/// add one - <c>ALTER TABLE ... ADD COLUMN "xmin"</c> is refused by Postgres with <c>column name
/// "xmin" conflicts with a system column name</c>. Auto-deploy runs <c>Migrate()</c> on container
/// start, so a migration that scaffolds it is a crash on every boot, not on one environment.
/// </para>
/// <para>
/// The EF model still has to <i>declare</i> the token, or the concurrency check never fires - that is
/// <c>UseXminAsConcurrencyToken()</c> in each entity's configuration. The token lives in the model
/// and in the snapshot; it does not live in the DDL. All three migrations that add the token are
/// therefore empty bodies whose only job is to move the snapshot forward.
/// </para>
/// <para>
/// <b>Why this test exists rather than a review convention.</b> The third of those three was
/// originally written as an explicit <c>migrationBuilder.AddColumn&lt;uint&gt;(name: "xmin", ...)</c>.
/// It happened to emit no DDL and so would have worked, but it was
/// <i>textually indistinguishable</i> from the scaffold the other two document as a crash - so the
/// next reader could not tell a working migration from a boot-crashing one without going and
/// generating the script. That is a trap that costs an hour to notice and a deploy to step in. It is
/// now an empty body, and this test fails if the scaffold comes back.
/// </para>
/// </remarks>
public sealed class XminMigrationGuardTests
{
    // Deliberately narrow: `name: "xmin"` (or a single-quoted variant) is the only spelling that has
    // ever been dangerous. Matching the whole AddColumn/DropColumn call rather than the bare string
    // keeps a table that legitimately has a column *called* something else from tripping it, while
    // still catching the scaffold whether it is written with type:, rowVersion: or neither.
    private static readonly Regex XminColumnDdl = new(
        @"\b(Add|Drop)Column(?:<[^>]*>)?\s*\([^;]*?\bname\s*:\s*""xmin""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void No_Migration_Adds_Or_Drops_The_Xmin_System_Column()
    {
        var migrationsRoot = LocateMigrationsFolder();
        var offenders = Directory
            .EnumerateFiles(migrationsRoot, "*.cs", SearchOption.TopDirectoryOnly)
            // The Designer files are EF-generated snapshots of the MODEL, not DDL, and the token is
            // correctly present in every one of them.
            .Where(p => !p.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase))
            .Select(p => (path: p, text: File.ReadAllText(p)))
            // Comments are stripped for the same reason NoTrackingMutationGuardTests strips them: the
            // prose explaining why this is forbidden mentions the exact call being looked for, and
            // scanning raw text would make every explanation read as an offence.
            .Select(f => (f.path, text: StripComments(f.text)))
            .Where(f => XminColumnDdl.IsMatch(f.text))
            .Select(f => Path.GetFileName(f.path))
            .OrderBy(n => n)
            .ToList();

        offenders.Should().BeEmpty(
            "`xmin` is a Postgres system column, already present on every row: there is nothing to add " +
            "and `ALTER TABLE ... ADD COLUMN \"xmin\" xid` is refused with " +
            "'column name \"xmin\" conflicts with a system column name'. Because auto-deploy runs " +
            "Migrate() on container start, a scaffold here crashes every boot. Declare the token in " +
            "the EF model (UseXminAsConcurrencyToken) and let the migration move the snapshot only - " +
            "see AddXminConcurrencyToken, ExtendXminToFulfillmentTables and PaymentConcurrencyToken.");
    }

    /// <summary>
    /// The complement of the guard above: the token has to be declared on the tables that need it,
    /// or EF never checks the row version and a lost update is silently accepted. A migration cannot
    /// add it, so the model is the only place it can live - which makes "the migration is empty"
    /// suspicious enough to be worth asserting from the other side.
    /// </summary>
    [Theory]
    [InlineData("orders")]
    [InlineData("fuel_vouchers")]
    [InlineData("payments")]
    [InlineData("operator_voucher_renewals")]
    public void The_Token_Reach_This_Table_In_The_Model(string table)
    {
        var snapshot = File.ReadAllText(
            Path.Combine(LocateMigrationsFolder(), "ApplicationDbContextModelSnapshot.cs"));

        // Split on the entity-block opener and keep each block. Every block that maps `table` also
        // carries its properties, so "does any block declaring this ToTable also declare xmin" is the
        // whole question - and it is a question about structure, not about a pattern to be clever
        // with. A block that maps the table twice (once for properties, once for relationships) is
        // fine as long as one of them has the token.
        var blocks = snapshot.Split("modelBuilder.Entity(");

        blocks.Any(b => b.Contains($"ToTable(\"{table}\"", StringComparison.Ordinal)
                         && b.Contains("Property<uint>(\"xmin\")", StringComparison.Ordinal))
            .Should().BeTrue(
                $"`{table}` must keep its xmin concurrency token declared in the EF model - that is the " +
                "only place the token can live, because no migration may add the column. A table that " +
                "loses it stops detecting concurrent writes and accepts the loser's change silently.");
    }

    private static string StripComments(string text)
    {
        var withoutBlock = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//[^\n]*", " ");
    }

    /// <summary>
    /// Resolves backend/src/FuelFlow.API/Migrations from this file's compile-time location, with a
    /// fallback that climbs from the running assembly - the same shape as
    /// <see cref="NoTrackingMutationGuardTests"/>, because both read source rather than a database.
    /// </summary>
    private static string LocateMigrationsFolder([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        var candidate = Path.Combine(backend, "src", "FuelFlow.API", "Migrations");
        if (Directory.Exists(candidate)) return candidate;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var probe in new[]
                     {
                         Path.Combine(dir.FullName, "src", "FuelFlow.API", "Migrations"),
                         Path.Combine(dir.FullName, "backend", "src", "FuelFlow.API", "Migrations")
                     })
            {
                if (Directory.Exists(probe)) return probe;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate backend/src/FuelFlow.API/Migrations from either the test file path or " +
            "the assembly directory.");
    }
}