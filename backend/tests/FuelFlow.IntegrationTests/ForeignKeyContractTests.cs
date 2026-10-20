using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The foreign keys the model declares must be the ones the database has, with the same delete
/// behaviour - and every foreign key column must have an index.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a test about the whole schema and not about the five keys this change
/// added.</b> A foreign key that the model declares and the database does not have is invisible:
/// the model looks right, the database is unguarded, and nothing fails. So does the reverse - a
/// key in the database that the model does not know about, which EF will not remove and which
/// nobody can reason about from the code. And a key whose delete rule differs is the same kind
/// of silence: RESTRICT in the model and NO ACTION in the database block different things.
/// </para>
/// <para>
/// <b>Why the index is in the same test.</b> PostgreSQL does not create an index for a foreign
/// key. It does not have to: the constraint is checked on the parent's delete, which scans the
/// child table without one. So a key without an index is not a constraint with a cost - it is a
/// constraint that makes every delete of the parent a sequential scan, and every join down that
/// column one too.
/// </para>
/// <para>
/// <b>Why there is a second test about the keys that must NOT be there.</b> The audit actor
/// columns carry no foreign key on purpose: an audit row must not be the thing that blocks
/// deleting its own subject, which is the rule issue #307 writes down. A rule nobody checks is
/// a rule one afternoon of adding "just one more key" erases. So the columns are written out
/// here and the test fails if a key appears on any of them.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class ForeignKeyContractTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ForeignKeyContractTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task EveryForeignKeyTheModelDeclares_IsInTheDatabase_WithTheSameDeleteRule()
    {
        await using var context = CreateContext();

        var declared = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys().Select(fk => new
            {
                Table = e.GetTableName(),
                Column = fk.Properties.Single().GetColumnName(),
                Principal = fk.PrincipalEntityType.GetTableName(),
                Rule = fk.DeleteBehavior
            }))
            .Where(x => x.Table is not null && x.Principal is not null)
            .ToList();

        declared.Should().NotBeEmpty("the model declares foreign keys");

        var installed = (await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT tc.table_name || '.' || kcu.column_name || ' -> ' || ccu.table_name || ' ' || rc.delete_rule
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                JOIN information_schema.constraint_column_usage ccu
                  ON tc.constraint_name = ccu.constraint_name AND tc.table_schema = ccu.table_schema
                JOIN information_schema.referential_constraints rc
                  ON tc.constraint_name = rc.constraint_name AND tc.constraint_schema = rc.constraint_schema
                WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema = 'public'
                """)
            .ToListAsync())
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared
            .Where(d => !installed.Contains($"{d.Table}.{d.Column} -> {d.Principal} {RuleToRule(d.Rule)}"))
            .Select(d => $"{d.Table}.{d.Column} -> {d.Principal} ({d.Rule})")
            .ToArray();

        missing.Should().BeEmpty(
            "a foreign key the model declares but the database does not have is a constraint that " +
            "does not exist, and one whose delete rule differs blocks different things");
    }

    [Fact]
    public async Task EveryForeignKeyColumnInTheDatabase_HasAnIndex()
    {
        await using var context = CreateContext();

        // Postgres does not index a foreign key on its own: the check on the parent's delete scans
        // the child table, and so does every join down that column.
        var withoutIndex = (await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT tc.table_name || '.' || kcu.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema = 'public'
                  AND NOT EXISTS (
                    SELECT 1 FROM pg_index i
                    JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = i.indkey[0]
                    WHERE i.indrelid = format('%I.%I', tc.table_schema, tc.table_name)::regclass
                      AND a.attname = kcu.column_name)
                """)
            .ToListAsync())
            .ToArray();

        withoutIndex.Should().BeEmpty(
            "a foreign key without an index makes every delete of the parent a scan of the child table");
    }

    [Fact]
    public async Task TheAuditActorColumns_CarryNoForeignKey()
    {
        await using var context = CreateContext();

        // The rule is issue #307's: an audit row must not be the thing that blocks deleting its
        // own subject. A key on any of these columns would either wedge the job that deletes the
        // account, or force it to delete the history - and the history is the point.
        //
        // The list is written out rather than derived from the model. A derived list would follow
        // the model and prove nothing; this one is the rule, in the form a code change has to
        // argue with before it can add a key here.
        //
        // payments.webhook_event_id is in the list for a neighbouring reason: it names the delivery
        // that proved the payment, and the payment is the money - one of the two must never be what
        // blocks writing the other.
        var unconstrained = new[]
        {
            "app_settings.updated_by_user_id",
            "error_logs.resolved_by_user_id",
            "fuel_price_history.changed_by_user_id",
            "fuel_types.price_updated_by_user_id",
            "operator_voucher_renewals.acting_user_id",
            "operator_voucher_renewals.customer_user_id",
            "payments.webhook_event_id",
            "provider_event_outbox.changed_by_user_id",
            "settlement_messages.resolved_user_id"
        };

        var constrained = (await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT tc.table_name || '.' || kcu.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema = 'public'
                """)
            .ToListAsync())
            .ToHashSet(StringComparer.Ordinal);

        var wronglyConstrained = unconstrained.Where(constrained.Contains).ToArray();

        wronglyConstrained.Should().BeEmpty(
            "an audit row must not be the thing that blocks deleting its own subject - " +
            "issue #307 writes the rule down, and this test is what keeps it true");
    }

    [Fact]
    public async Task TheAuditOutboxAuthor_IsNullable_AndCarriesNoSentinel()
    {
        await using var context = CreateContext();

        // The all-zero guid stood for "no user" in a column that was NOT NULL, which is why it
        // could have no foreign key: a value that means "nobody" is not a user. The system writes
        // NULL now, so the column can be checked - and the rows that carried the sentinel are
        // normalised, with the name surviving in changed_by_user_name.
        var sentinels = await context.Database
            .SqlQueryRaw<int>(
                """
                SELECT count(*) FROM "provider_event_outbox"
                WHERE "changed_by_user_id" = '00000000-0000-0000-0000-000000000000'::uuid
                """)
            .ToListAsync();

        sentinels.Should().ContainSingle().Which.Should().Be(0,
            "the sentinel is what a machine event used to write, and it is not a user");

        var nullable = (await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT is_nullable FROM information_schema.columns
                WHERE table_name = 'provider_event_outbox' AND column_name = 'changed_by_user_id'
                """)
            .ToListAsync())
            .Should().ContainSingle().Which;

        nullable.Should().Be("YES", "a machine event has no user, and NULL says that without pretending otherwise");
    }

    private static string RuleToRule(DeleteBehavior behavior) => behavior switch
    {
        DeleteBehavior.Cascade => "CASCADE",
        DeleteBehavior.SetNull => "SET NULL",
        DeleteBehavior.Restrict => "RESTRICT",
        DeleteBehavior.NoAction => "NO ACTION",
        _ => behavior.ToString().ToUpperInvariant()
    };
}
