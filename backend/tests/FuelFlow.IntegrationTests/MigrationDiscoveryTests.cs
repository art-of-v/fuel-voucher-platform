using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// EF discovers migrations by the <c>[DbContext]</c> and <c>[Migration]</c> attributes that the
/// generated <c>.Designer.cs</c> carries. A migration written by hand has no such file, so it
/// compiles, sits in the folder, and is silently invisible: <c>Migrate()</c> never runs it and
/// <c>dotnet ef migrations add</c> never mentions it.
///
/// That is not hypothetical. Two hand-written migrations shipped that way and stayed invisible on
/// every environment - one of them the migration the model snapshot already claimed was applied, so
/// the constraint it adds would never exist on a fresh database, with nothing failing to say so.
/// </summary>
[Collection("Integration Tests")]
public sealed class MigrationDiscoveryTests
{
    private static IReadOnlyList<string> DiscoverMigrationIds()
    {
        using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql("Host=localhost;Database=none;Username=none")
                .Options,
            TestQrCipher.Instance
        );

        return context.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();
    }

    [Fact]
    public void EveryMigration_Is_Discoverable_By_EF()
    {
        var ids = DiscoverMigrationIds();

        // A migration on disk that EF cannot see is worse than no migration at all: it reads as applied
        // work and never is. The assertions name the ones that were invisible, so a regression here
        // points at a specific file rather than at "something is wrong with migrations".
        ids.Should().Contain("20261007073119_AddFulfillmentOrderForeignKey");
        ids.Should().Contain("20261007091958_RenameOrderCleanupSettingKeys");
    }

    /// <summary>
    /// The eight migrations production has applied, and the one that stands in for them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On 2026-09-11 a commit deleted eight migration files with the reasoning that "EF scans the
    /// assembly, already-applied history rows are unaffected". That is true for the databases that
    /// already have them and irrelevant for every database that does not.
    /// </para>
    /// <para>
    /// It happened to be harmless, because <c>20260919113317_AddMissingDriftedTables</c> re-creates
    /// everything those eight made with <c>IF NOT EXISTS</c> — which is what that migration is for,
    /// and why its name says so. <b>Restoring the eight would not be harmless.</b> A database
    /// migrated from zero while they were gone — staging is one — holds the current schema and a
    /// history that does not mention them, so EF would try to apply them over objects that already
    /// exist, and the first one does <c>CREATE TABLE refunds</c>, which by then is a view:
    /// <c>42P07: relation "refunds" already exists</c>. That is how staging stopped booting on
    /// 2026-10-19, and because staging deploys before production it blocked every deploy.
    /// </para>
    /// <para>
    /// So they stay deleted and the compensation is what holds. The general check is the CI gate,
    /// <c>deploy/check-migration-history.sh</c>, which compares a deployed database's history
    /// against the migration folder and refuses a repository that has lost a migration nothing else
    /// compensates for. It needs a database, so it cannot run here; what is asserted locally is
    /// that the compensation is in place and that the eight are accounted for.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_Deleted_Migrations_Are_Compensated_For_And_Not_Restored()
    {
        var ids = DiscoverMigrationIds();

        var deleted = new[]
        {
            "20260803120600_AddRefunds",
            "20260804123305_AddRuntimeSettingsAndPartialFulfillmentSince",
            "20260816121521_AddVerificationCodeAttempts",
            "20260816163113_AddRefreshTokenFamily",
            "20260816163849_WidenOrderIdempotencyKey",
            "20260817190228_AddCompanyMembership",
            "20260817190316_AddWorkerVoucherAssignment",
            "20260817191435_AddOrderLegalEntity"
        };

        ids.Should().NotContain(deleted,
            "these eight are compensated for by AddMissingDriftedTables; restoring them makes every "
            + "database that was migrated from zero while they were gone fail with 42P07");

        ids.Should().Contain("20260919113317_AddMissingDriftedTables",
            "the compensation has to be in the migration folder, and it is the thing that creates "
            + "what the eight would have created");

        // The list the gate reads has to name exactly those eight, or the gate is either red or
        // blind. It is a file rather than a constant so that it can be read and reviewed.
        // FindProjectRoot stops at the backend solution, and deploy/ is beside it, not under it.
        var listPath = Path.GetFullPath(
            Path.Combine(FindProjectRoot(), "..", "deploy", "compensated-migration-ids.txt"));
        File.Exists(listPath).Should().BeTrue(
            $"the gate's compensated list has to exist at {listPath}, and it is what makes the eight accounted for");

        var listed = File.ReadAllLines(listPath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

        listed.Should().BeEquivalentTo(deleted,
            "the list names the eight that are missing because something else creates what they created");
    }

    [Fact]
    public void Hand_Written_Migrations_Carry_The_Attributes_Discovery_Needs()
    {
        // Checked directly on the types, because the assembly-level discovery above would still pass
        // for a migration that some other mechanism happened to find.
        var assembly = typeof(ApplicationDbContext).Assembly;

        foreach (
            var id in new[]
            {
                "20261007073119_AddFulfillmentOrderForeignKey",
                "20261007091958_RenameOrderCleanupSettingKeys"
            }
        )
        {
            var type = assembly
                .GetTypes()
                .SingleOrDefault(t =>
                    t.GetCustomAttributes(typeof(MigrationAttribute), false)
                        .Any(a => ((MigrationAttribute)a).Id == id)
                );

            type.Should().NotBeNull($"migration {id} must carry [Migration] to be discoverable");
            type!.GetCustomAttributes(typeof(DbContextAttribute), false).Should()
                .ContainSingle($"migration {id} must carry [DbContext] to be discovered");
        }
    }

    /// <summary>
    /// Closes the class of bug above instead of pinning its two known instances. EF discovers
    /// migrations by assembly attribute, not by folder, so a migration can sit anywhere in the
    /// project and be invisible — and two did, in <c>Features/Auth/Migrations/</c>, which is why
    /// that folder now holds nothing.
    /// </summary>
    [Fact]
    public void Migrations_Live_In_One_Folder()
    {
        var projectRoot = FindProjectRoot();
        var migrationFiles = Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(f => !f.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        migrationFiles.Should().NotBeEmpty();

        // The migration and its Designer must be siblings: a migration whose Designer sits in a
        // different folder compiles and is still invisible, because the attributes ride on the
        // generated partial.
        var canonical = Path.Combine(projectRoot, "src", "FuelFlow.API", "Migrations");
        migrationFiles
            .Where(f => !Path.GetDirectoryName(f)!.Equals(canonical, StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("migrations belong in src/FuelFlow.API/Migrations so the folder list is honest");

        foreach (var file in migrationFiles.Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal)))
        {
            // The [Migration] id is the file name without its extension, extension included in the
            // match - not just the timestamp prefix.
            var id = Path.GetFileNameWithoutExtension(file);
            var designer = Path.ChangeExtension(file, ".Designer.cs");

            // Either the generated partial exists, or the hand-written class carries the two
            // attributes discovery depends on. A migration with neither is invisible.
            var designerExists = File.Exists(designer);
            var carriesAttributes = typeof(ApplicationDbContext).Assembly
                .GetTypes()
                .Any(t => t.GetCustomAttributes(typeof(MigrationAttribute), false)
                    .Any(a => ((MigrationAttribute)a).Id == id));

            (designerExists || carriesAttributes).Should().BeTrue(
                $"migration {id} has no .Designer.cs and no [Migration] attribute, so EF cannot see it");
        }
    }

    /// <summary>
    /// The history is <b>append-only</b>: it still starts at the migration that created the
    /// schema, and it is not collapsed into a single baseline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one migration fact that no other test here can notice losing. Every other
    /// assertion in this class checks that a migration EF can see exists; all of them pass just as
    /// happily against a history that has been rewritten, because every migration in a squashed set
    /// is still perfectly discoverable — that is the whole problem.
    /// </para>
    /// <para>
    /// A squash is not a neutral tidy-up here. Production is dry-run by restoring its real
    /// <c>__EFMigrationsHistory</c> and schema and applying only the pending migrations, so a
    /// baseline id production has never seen makes EF attempt to <c>CREATE TABLE</c> over a schema
    /// that already has those tables — killing the one gate that tests a migration against
    /// production's rows instead of seeded ones. It also deletes the id that
    /// <c>DataKindBackfillTests</c> migrates *to</c> in order to assert what a past backfill
    /// produced. See AGENTS.md, and "The history is append-only — never squashed" in
    /// docs/CODEBASE.md (private docs repo).
    /// </para>
    /// </remarks>
    [Fact]
    public void The_Migration_History_Is_Append_Only()
    {
        var ids = DiscoverMigrationIds();

        // The oldest migration is still the one that created the schema. A squashed history does
        // not contain it at all, and pruning a few early entries to tidy the folder would leave a
        // later migration claiming to create tables it never touched.
        ids.Should().Contain(
            "20260617125815_InitialCreate",
            "the migration history is append-only — every applied migration stays under its " +
            "original id, and it is never collapsed into a single baseline");

        // The complement of the assertion above: the count is deliberately a floor rather than a
        // census, because an exact number would need editing on every schema change. What it rules
        // out is the other shape a squash can take — deleting the history and scaffolding a
        // single migration that happens to reuse the name.
        ids.Should().HaveCountGreaterThan(
            1,
            "a history of one migration is a squashed history, whatever it is named");
    }

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FuelFlow.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("could not locate the backend project root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}