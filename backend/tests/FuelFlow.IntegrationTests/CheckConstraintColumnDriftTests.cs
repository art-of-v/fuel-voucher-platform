using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text.RegularExpressions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Every CHECK the model declares must name columns that exist, and must read the same as the
/// constraint actually installed.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of one specific near-miss, and the shape of it is worth stating. When
/// <c>20261016071102_MoneyIsDecimalOnly</c> renamed <c>refunds.amount</c> to
/// <c>refunds.amount_kopecks</c>, PostgreSQL followed the reference inside the CHECK by OID — so the
/// live constraint silently became <c>amount_kopecks &gt; 0</c>, which is correct and which nothing
/// asked for.
/// </para>
/// <para>
/// Meanwhile <see cref="Orders.Configurations.RefundConfiguration"/> kept saying <c>"amount &gt; 0"</c>
/// as raw SQL text. That is a lie that costs nothing until it is read. EF only emits a
/// check-constraint change when the model's text differs from the snapshot's, and the snapshot had
/// recorded the same wrong string — so <c>migrations add</c> saw no difference and wrote nothing. The
/// model and the snapshot agreed with each other and both disagreed with the database.
/// </para>
/// <para>
/// The failure mode is therefore silence, twice over: no migration, no failing test, and a schema
/// document that tells the reader to reason from the model. So this asserts the thing that was never
/// asserted — not that the refund constraint is right (one hand-written assertion would do that, and
/// <c>DatabaseIntegrityTests</c> has it) but that <b>no</b> CHECK anywhere names a column that is not
/// there. That is a property of the whole schema rather than of one constraint, and it is the part
/// that would have caught this.
/// </para>
/// <para>
/// It also compares the model's text against the installed one, because a constraint that exists on
/// the right column with the wrong predicate is equally invisible: the model still looks plausible and
/// the database still looks guarded.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class CheckConstraintColumnDriftTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public CheckConstraintColumnDriftTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task EveryCheckConstraintInTheDatabase_NamesOnlyColumnsThatExist()
    {
        await using var context = CreateContext();

        // pg_constraint.conkey is the list of columns the constraint depends on, so a dropped or
        // renamed column shows up here as an attnum with no matching pg_attribute row. The predicate
        // text cannot answer this: PostgreSQL already rewrote mount to mount_kopecks by OID, and
        // pg_get_constraintdef reports the rewritten form, so reading the text would find nothing.
        var dangling = await context.Database.SqlQueryRaw<string>(
            """
            SELECT c.conname AS "Value"
            FROM pg_constraint c
            JOIN pg_class r ON r.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = r.relnamespace
            WHERE c.contype = 'c'
              AND n.nspname = 'public'
              AND c.conkey IS NOT NULL
              AND EXISTS (
                  SELECT 1
                  FROM unnest(c.conkey) AS a(attnum)
                  WHERE NOT EXISTS (
                      SELECT 1 FROM pg_attribute at
                      WHERE at.attrelid = c.conrelid AND at.attnum = a.attnum AND NOT at.attisdropped
                  )
              )
            """).ToListAsync();

        dangling.Should().BeEmpty(
            "a CHECK that still references a dropped column is a constraint nobody is enforcing");

        // Belt and braces on the same axis, read a second way. A CHECK can be column-free (a constant,
        // or an expression over other CHECKs) and the join above skips those; this asserts the model's
        // own declarations name real columns, which is the half that is a raw string in C#.
        var modelChecks = ModelCheckConstraints();

        modelChecks.Should().NotBeEmpty("the model does declare CHECKs, and this must not be vacuous");

        var missing = new List<string>();
        foreach (var (table, constraint, expression) in modelChecks)
        {
            foreach (var column in ColumnReferences(expression))
            {
                var exists = await context.Database
                    .SqlQueryRaw<int>(
                        $"""
                         SELECT count(*) FROM information_schema.columns
                         WHERE table_schema = 'public' AND table_name = '{table}' AND column_name = '{column}'
                         """)
                    .ToListAsync();

                if (exists.Single() == 0)
                {
                    missing.Add($"{table}.{column} (from {constraint}: \"{expression}\")");
                }
            }
        }

        missing.Should().BeEmpty(
            "the model is what the schema doc tells a reader to reason from, so it cannot name a column "
            + "the database does not have");
    }

    [Fact]
    public async Task EveryModelCheckConstraint_MatchesTheOnePostgresHas()
    {
        await using var context = CreateContext();

        var installed = (await context.Database.SqlQueryRaw<string>(
            """
            SELECT conname AS "Value"
            FROM pg_constraint
            WHERE contype = 'c'
              AND connamespace = 'public'::regnamespace
            """).ToListAsync()).ToHashSet(StringComparer.Ordinal);

        var missing = ModelCheckConstraints()
            .Where(c => !installed.Contains(c.constraint))
            .Select(c => c.constraint)
            .ToList();

        missing.Should().BeEmpty(
            "a CHECK in the model that no migration ever installed is a guarantee that exists only in "
            + "the source");
    }

    /// <summary>
    /// Every <c>HasCheckConstraint</c> in the model, read out of the compiled EF model rather than by
    /// grepping the source.
    /// </summary>
    /// <remarks>
    /// From the model, not the files, because the files include historical migrations and comments
    /// that legitimately quote old column names — and because a constraint is only "declared" once it
    /// has been parsed into metadata. A source grep would report the migration that created it, the
    /// Designer that recorded it and the comment that explains it, and call that three declarations.
    /// </remarks>
    private static List<(string table, string constraint, string expression)> ModelCheckConstraints()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused")
            .Options;

        using var context = new ApplicationDbContext(options, TestQrCipher.Instance);

        // IDesignTimeModel, not context.Model. The read-optimized model drops check constraints
        // entirely — asking it throws — which is the one thing this test cannot do.
        var model = context.GetService<IDesignTimeModel>().Model;

        return model
            .GetEntityTypes()
            .SelectMany(entity => entity.GetCheckConstraints()
                .Select(c => (table: entity.GetTableName() ?? entity.ClrType.Name,
                              constraint: c.Name,
                              expression: c.Sql ?? string.Empty)))
            .ToList();
    }

    /// <summary>
    /// Identifiers the CHECK expression refers to, as opposed to keywords and SQL function names.
    /// </summary>
    /// <remarks>
    /// Deliberately a quoted-identifier scan plus a bare-identifier scan filtered against the SQL
    /// keyword and builtin-function lists, rather than anything clever. A check expression is a short
    /// piece of SQL written by a human; the only thing that has to be true is that this does not
    /// silently return the empty set, because then the test above would pass for the wrong reason —
    /// which is exactly the failure mode it was written to prevent.
    /// </remarks>
    private static IEnumerable<string> ColumnReferences(string expression){
        // Strip single-quoted literals FIRST. `status IN ('Active', 'Revoked')` is the single most
        // common shape a CHECK has in this schema, and a bare-identifier scan over it yields the
        // five words inside the quotes — every one of which is then reported as a column that does
        // not exist. That is the "fails for the wrong reason" failure this method exists to avoid,
        // and it is why the literal strip is not an optimisation: without it the assertion is noise.
        var withoutLiterals = Regex.Replace(expression, @"'(?:[^']|'')*'", "''");

        var quoted = Regex.Matches(withoutLiterals, @"""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .Where(n => !n.Contains('.'));

        var bare = Regex.Matches(withoutLiterals, @"\b([a-z_][a-z0-9_]*)\b", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value)
            .Where(n => !Keywords.Contains(n) && !BuiltinFunctions.Contains(n));

        return quoted.Concat(bare).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // The comparison and boolean vocabulary a CHECK is written in. `from` is here because of
        // `IS DISTINCT FROM`, which appears twice in this schema and reads as an identifier to any
        // scan that has not been told otherwise — the same category of mistake as the string literals.
        "and", "or", "not", "is", "from", "null", "true", "false", "case", "when", "then", "else", "end",
        "between", "like", "ilike", "in", "exists", "as", "cast", "collate", "default", "primary",
        "unique", "check", "constraint", "asc", "desc", "any", "all", "some", "distinct", "array",
        "select", "where", "by", "having", "group", "order", "limit", "offset", "values", "returning",
        "union", "except", "intersect", "at", "time", "zone", "local", "timestamp", "interval",
    };

    private static readonly HashSet<string> BuiltinFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "length", "lower", "upper", "coalesce", "abs", "round", "greatest", "least", "now",
        "substring", "trim", "btrim", "nullif", "concat", "position", "age", "date_trunc",
    };
}