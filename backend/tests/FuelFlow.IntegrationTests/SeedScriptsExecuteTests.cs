using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Every SQL script under <c>scripts/</c> has to run against the schema that exists today.
/// </summary>
/// <remarks>
/// <para>
/// The concrete failure this exists for: <c>scripts/seed-qa-catalogue.sql</c> and
/// <c>scripts/seed-qa-full-emulation.sql</c> still created a <c>fuel_offers</c> twin and a
/// <c>fuel_offer_volumes</c> twin under it. Both tables were dropped - <c>fuel_offer_volumes</c> in
/// <c>20261018224126</c> and <c>fuel_offers</c> in <c>20261019095748</c>, the migration that moved the
/// price onto the fuel itself - so the two scripts that seed the App Store reviewer's catalogue had
/// been failing for a week, and <c>scripts/reset-test-data.sql</c> still deleted from
/// <c>voucher_renewal_items</c>, a table dropped in <c>20261017073518</c>.
/// </para>
/// <para>
/// A script that cannot run is a script nobody notices is broken: these are run by hand, against
/// production, by whoever is doing a QA session - so the failure surfaces as "the reviewer sees an
/// empty shop" rather than as an error naming the table. That is the whole reason this test exists.
/// </para>
/// <para>
/// The scripts are executed rather than parsed, and through a small psql interpreter rather than
/// through psql itself: the file's own directives (<c>\set</c>, <c>\if</c>, <c>\gset</c>,
/// <c>\quit</c>) are part of how the script decides what to do, and a plain SQL parser would
/// either refuse them or run a branch the script never meant to take.
/// </para>
/// <para>
/// What is asserted is that every statement RESOLVES against the current schema. Statements that
/// fail on DATA - a foreign key to a voucher that is not there, a unique index that already holds
/// the row - are not failures of this test: the database here has no live catalogue and no QA
/// session behind it, and the scripts are written to seed one. A missing table, a missing column, a
/// column of the wrong type and a syntax error are failures, because those are the ones that make
/// the script unable to run at all.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class SeedScriptsExecuteTests : IClassFixture<TestDatabaseFixture>
{
    private const string SeededFuelName = "А-95";
    private const string SeededBrandSlug = "okko";

    private readonly TestDatabaseFixture _fixture;

    public SeedScriptsExecuteTests(TestDatabaseFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The scripts that are checked. Every other file in <c>scripts/</c> is either one of these or
    /// is listed in <see cref="ScriptsThatAreNotChecked"/> with a reason - so a new script cannot
    /// arrive unchecked without somebody saying why.
    /// </summary>
    private static readonly string[] CheckedScripts =
    [
        "seed-qa-catalogue.sql",
        "seed-qa-full-emulation.sql",
        "seed-test-vouchers.sql",
        "remove-qa-catalogue.sql",
        "reset-test-data.sql",
    ];

    /// <summary>
    /// Scripts that are not executed, and why. A script that writes to a table nobody has can still
    /// be checked - the fixture below creates the QA account, the brand, the fuel and the nominal
    /// these scripts need - so this list is short on purpose.
    /// </summary>
    private static readonly (string File, string Reason)[] ScriptsThatAreNotChecked =
    [
        ("seed-demo-reports.sql", "writes demo analytics rows, not part of the QA pool"),
    ];

    public static IEnumerable<object[]> CheckedScriptFiles() => CheckedScripts.Select(name => new object[] { name });

    [Fact]
    public void EveryScriptInTheDirectory_IsEitherCheckedOrExplained()
    {
        var directory = Path.Combine(RepoRoot(), "scripts");
        var files = Directory.GetFiles(directory, "*.sql").Select(Path.GetFileName).ToList();

        files.Should().NotBeEmpty();

        var known = CheckedScripts.Concat(ScriptsThatAreNotChecked.Select(s => s.File)).ToHashSet(StringComparer.Ordinal);

        files.Should().AllSatisfy(file => known.Should().Contain(file,
            $"{file} is not in the checked list and not in the not-checked list. Add it to one of them - " +
            "a script nobody runs is a script nobody notices is broken."));
    }

    [Theory]
    [MemberData(nameof(CheckedScriptFiles))]
    public async Task EverySeedScript_RunsAgainstTheSchemaTheApplicationRuns(string fileName)
    {
        var qaUserId = await SeedTheFixtureAsync();

        var path = Path.Combine(RepoRoot(), "scripts", fileName);
        var script = File.ReadAllText(path);

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        // The variables a script insists on: which QA account it seeds for, and which fuel. The
        // fuel is the one this fixture creates, so the lookup in seed-test-vouchers.sql resolves it
        // the way it resolves on a database that carries the catalogue.
        var runner = new PsqlInterpreter(connection, new Dictionary<string, string>
        {
            ["qa_user_id"] = qaUserId,
            ["fuel_name"] = SeededFuelName,
            ["fuel_brand"] = SeededBrandSlug,
        });

        var failures = await runner.RunAsync(script);

        failures.Should().BeEmpty(
            $"{fileName} does not run against the current schema. The statements that failed to " +
            "resolve are listed above; the ones that failed on data (a foreign key, a unique index) " +
            $"are expected here, because this database has no live catalogue to seed. Data failures: {string.Join(" | ", runner.DataFailures)}. Last statements: {string.Join(" | ", runner.Executed.TakeLast(8))}");
    }

    /// <summary>
    /// The removal script has to REMOVE, not merely run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Running is not enough for this one, and the difference is the whole point. A script that
    /// guards a table by asking whether it exists, and then deletes from the name it asked about,
    /// runs cleanly whether or not the rows are there - so a table that was renamed under it leaves
    /// its rows behind and the script still says it did its job. That is exactly what happened here:
    /// <c>fuel_offer_prices</c> was renamed <c>fuel_price_history</c> on 2026-10-19
    /// (<c>20261019132728</c>), and a script that asked only about the new name would leave the QA
    /// price history on a database that has not taken the rename - silently, because the report at
    /// the end would be reading the same table.
    /// </para>
    /// <para>
    /// So this seeds a QA pool, runs the script, and asks the only question that matters: is any of
    /// it still there? The real pool is checked too - a removal script that takes the live
    /// catalogue with it is the other way this could be wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheRemovalScript_RemovesTheQaPoolAndLeavesTheRealOne()
    {
        await SeedTheFixtureAsync();
        await SeedQaPoolAsync();

        var before = await ReadQaPoolAsync();

        // The QA pool is really there, or the test below proves nothing.
        before.Should().ContainKey("qa nominals");
        before["qa nominals"].Should().BeGreaterThan(0, "the QA pool was seeded and must be there to remove");
        before["qa price history"].Should().BeGreaterThan(0, "the QA price history is the row a rename would leave behind");

        var path = Path.Combine(RepoRoot(), "scripts", "remove-qa-catalogue.sql");
        var script = File.ReadAllText(path);

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        var runner = new PsqlInterpreter(connection);
        var failures = await runner.RunAsync(script);

        failures.Should().BeEmpty(
            "remove-qa-catalogue.sql does not run against the current schema. Data failures: " +
            string.Join(" | ", runner.DataFailures));

        var after = await ReadQaPoolAsync();

        after.Where(pair => pair.Key.StartsWith("qa ", StringComparison.Ordinal) && pair.Value > 0)
            .Select(pair => $"{pair.Key}: {pair.Value}")
            .Should().BeEmpty("the QA pool is still there after the removal script ran");

        after["real nominals"].Should().Be(before["real nominals"],
            "the removal script must leave the real catalogue alone");
    }

    /// <summary>
    /// A QA pool of the shape the scripts create: a size list, stock behind it, a price history, and
    /// the supplier -> import -> batch chain that holds them.
    /// </summary>
    private async Task SeedQaPoolAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO fuel_nominals (fuel_type_id, data_kind, liters, created_at_utc, updated_at_utc)
            VALUES ('33333333-3333-4333-8333-0000000000cc', 'qa', 10.00, now(), now())
            ON CONFLICT (fuel_type_id, data_kind, liters) DO NOTHING;

            INSERT INTO fuel_vouchers
                (id, brand, brand_id, fuel_type_id, liters, supplier_expiration_date, customer_expiration_date,
                 voucher_number, qr_payload, qr_payload_sha256, status, data_kind, acquisition_source,
                 is_deleted, created_at_utc, updated_at_utc)
            VALUES ('44444444-4444-4444-8444-0000000000aa', 'okko',
                    (SELECT id FROM brands WHERE slug = 'okko'),
                    '33333333-3333-4333-8333-0000000000cc',
                    10.00, current_date + 180, current_date + 7, 'QA-REMOVE-1', 'QA-QR-REMOVE-1',
                    sha256(convert_to('QA-QR-REMOVE-1', 'UTF8')), 'Available', 'qa', 'direct', false, now(), now())
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO fuel_price_history
                (id, brand_id, fuel_type_id, data_kind, effective_from_utc, recorded_at_utc, reason,
                 final_price_per_liter, supplier_price_per_liter)
            SELECT '55555555-5555-4555-8555-0000000000aa', b.id, '33333333-3333-4333-8333-0000000000cc',
                   'qa', now(), now(), 'operator', 45.00, 40.00
            FROM brands b WHERE b.slug = 'okko'
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO suppliers (id, name, is_active, edr_ipn, created_at_utc, updated_at_utc)
            VALUES ('66666666-6666-4666-8666-0000000000aa', 'QA Supplier (emulated)', true, '9999999999', now(), now())
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO voucher_imports
                (id, file_name, page_count, started_at_utc, completed_at_utc, status,
                 imported_count, duplicate_count, failed_count, verification_failed_count,
                 verified_with_warnings_count)
            VALUES ('77777777-7777-4777-8777-0000000000aa', 'QA-EMULATED-okko.pdf', 1, now(), now(),
                    'Completed', 1, 0, 0, 0, 0)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO purchase_batches
                (id, import_job_id, fuel_type_id, brand, brand_id, supplier_id, created_at_utc, updated_at_utc)
            VALUES ('88888888-8888-4888-8888-0000000000aa', '77777777-7777-4777-8777-0000000000aa',
                    '33333333-3333-4333-8333-0000000000cc', 'okko',
                    (SELECT id FROM brands WHERE slug = 'okko'),
                    '66666666-6666-4666-8666-0000000000aa', now(), now())
            ON CONFLICT (id) DO NOTHING;
            """,
            connection);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// What is there, counted: the QA pool and the real pool.
    /// </summary>
    private async Task<Dictionary<string, long>> ReadQaPoolAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT 'qa nominals' AS what, count(*) AS rows FROM fuel_nominals WHERE data_kind = 'qa'
            UNION ALL SELECT 'qa vouchers', count(*) FROM fuel_vouchers WHERE data_kind = 'qa'
            UNION ALL SELECT 'qa price history', count(*) FROM fuel_price_history WHERE data_kind = 'qa'
            UNION ALL SELECT 'qa imports', count(*) FROM voucher_imports WHERE file_name LIKE 'QA-EMULATED-%'
            UNION ALL SELECT 'qa supplier', count(*) FROM suppliers WHERE name = 'QA Supplier (emulated)'
            UNION ALL SELECT 'qa batches', count(*) FROM purchase_batches
                WHERE supplier_id = '66666666-6666-4666-8666-0000000000aa'
            UNION ALL SELECT 'real nominals', count(*) FROM fuel_nominals WHERE data_kind = 'real'
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        var counts = new Dictionary<string, long>();

        while (await reader.ReadAsync())
        {
            counts[reader.GetString(0)] = reader.GetInt64(1);
        }

        return counts;
    }

    /// <summary>
    /// The minimum the scripts need to get past their own guards: the QA account the emulation
    /// script refuses to run without, and one brand with one fuel and one nominal so the catalogue
    /// mirrors have something to mirror.
    /// </summary>
    /// <returns>The id of the QA account, which one of the scripts demands as a variable.</returns>
    private async Task<string> SeedTheFixtureAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users (id, phone_number, created_at_utc, updated_at_utc, bonus_balance,
                               is_active, token_version, is_deleted, is_banned, is_qa_account)
            SELECT '11111111-1111-4111-8111-0000000000aa', '+380110010203', now(), now(), 0,
                    true, 0, false, false, true
            WHERE NOT EXISTS (SELECT 1 FROM users WHERE phone_number = '+380110010203');

            -- The brand is the one the catalogue already carries, found by its slug: inserting a
            -- second 'okko' would break the unique index on the slug.
            INSERT INTO fuel_types (id, name, brand_id, base_price, discount_price, created_at_utc, updated_at_utc, is_deleted)
            SELECT '33333333-3333-4333-8333-0000000000cc', 'А-95', b.id, 40.00, 40.00, now(), now(), false
            FROM brands b
            WHERE b.slug = 'okko'
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO fuel_nominals (fuel_type_id, data_kind, liters, created_at_utc, updated_at_utc)
            VALUES ('33333333-3333-4333-8333-0000000000cc', 'real', 10.00, now(), now())
            ON CONFLICT (fuel_type_id, data_kind, liters) DO NOTHING;
            """,
            connection);

        await command.ExecuteNonQueryAsync();

        await using var read = new NpgsqlCommand(
            "SELECT id::text FROM users WHERE phone_number = '+380110010203' AND NOT is_deleted LIMIT 1",
            connection);

        var id = (string?)await read.ExecuteScalarAsync();
        id.Should().NotBeNull("the QA account the scripts insist on has to exist");
        return id!;
    }

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

    /// <summary>
    /// A small psql: enough of the client's behaviour for these scripts to run the way they run on
    /// the box.
    /// </summary>
    /// <remarks>
    /// It is not a psql. It reads the directives these files use - <c>\set</c>, <c>\if</c>,
    /// <c>\else</c>, <c>\endif</c>, <c>\echo</c>, <c>\gset</c>, <c>\quit</c> - substitutes variables
    /// the way psql does, and executes everything else as SQL. What it does not do is anything the
    /// files do not need: there are no <c>\copy</c>, no <c>\gexec</c>, and no <c>:var</c> inside a
    /// string literal, because psql would not substitute there either.
    /// </remarks>
    private sealed class PsqlInterpreter
    {
        private readonly NpgsqlConnection _connection;
        private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _schemaFailures = [];
        private readonly List<string> _dataFailures = [];
        private readonly List<string> _executed = [];

        public PsqlInterpreter(NpgsqlConnection connection, Dictionary<string, string>? variables = null)
        {
            _connection = connection;
            if (variables is not null)
            {
                foreach (var pair in variables)
                {
                    _variables[pair.Key] = pair.Value;
                }
            }
        }

        /// <summary>Statements that failed to RESOLVE: a missing table, a missing column, bad SQL.</summary>
        public IReadOnlyList<string> SchemaFailures => _schemaFailures;

        public IReadOnlyList<string> DataFailures => _dataFailures;

        public IReadOnlyList<string> Executed => _executed;

        public async Task<IReadOnlyList<string>> RunAsync(string script)
        {
            var statement = new StringBuilder();
            var gsetPending = false;
            var emitting = new Stack<bool>();
            var branchTaken = new Stack<bool>();

            foreach (var raw in script.Split('\n'))
            {
                var line = StripComment(raw).TrimEnd();

                if (line.TrimStart().StartsWith('\\'))
                {
                    var directive = line.Trim();

                    // The branch directives are processed whatever branch we are in - they are what
                    // decides the branch - but everything else is skipped inside a branch that is
                    // not taken, exactly as psql does it.
                    var branchDirective = directive.StartsWith("\\if", StringComparison.Ordinal)
                        || directive.StartsWith("\\else", StringComparison.Ordinal)
                        || directive.StartsWith("\\endif", StringComparison.Ordinal);

                    if (!branchDirective && emitting.Count > 0 && !emitting.Peek())
                    {
                        continue;
                    }

                    if (directive.StartsWith("\\set", StringComparison.Ordinal))
                    {
                        var assignment = directive["\\set".Length..].Trim();
                        var space = assignment.IndexOf(' ');
                        if (space < 0)
                        {
                            _variables[assignment] = string.Empty;
                        }
                        else
                        {
                            var name = assignment[..space];
                            var value = assignment[(space + 1)..].Trim();
                            _variables[name] = value.Trim('\'');
                        }
                    }
                    else if (directive.StartsWith("\\if", StringComparison.Ordinal))
                    {
                        var condition = EvaluateCondition(directive["\\if".Length..].Trim());
                        emitting.Push(condition);
                        branchTaken.Push(condition);
                    }
                    else if (directive.StartsWith("\\else", StringComparison.Ordinal))
                    {
                        // The else branch REPLACES the if branch: it is the other side of the same
                        // condition, not a nested one. Pushing a second level here would leave the
                        // stack one deeper than the matching \endif pops, and everything after the
                        // block would be read as "inside a branch that is not taken" - which is how
                        // this interpreter came to execute two statements out of a hundred.
                        var taken = branchTaken.Pop();
                        branchTaken.Push(!taken);
                        emitting.Pop();
                        emitting.Push(!taken);
                    }
                    else if (directive.StartsWith("\\endif", StringComparison.Ordinal))
                    {
                        emitting.Pop();
                        branchTaken.Pop();
                    }
                    else if (directive.StartsWith("\\gset", StringComparison.Ordinal))
                    {
                        // A \gset on a line of its own ends the statement above it: the query it
                        // belongs to has no semicolon, and psql reads the directive as the end of
                        // it. Without this, the query and the statement after it arrive together
                        // and the database refuses to parse them as one.
                        gsetPending = true;
                        if (statement.ToString().Trim().Length > 0)
                        {
                            var pending = statement.ToString().Trim();
                            statement.Clear();
                            await ExecuteAsync(pending, capture: true);
                            gsetPending = false;

                            if (ShouldQuit)
                            {
                                break;
                            }
                        }
                    }
                    else if (directive.StartsWith("\\quit", StringComparison.Ordinal))
                    {
                        // psql stops the script here. The emulation script uses it to refuse to run
                        // without the QA account, and the test vouchers script uses it to refuse to
                        // guess a fuel - both are guards that should stop the script, exactly as
                        // they stop it on the box.
                        ShouldQuit = true;
                    }
                    // \echo writes a message, which has no bearing on what runs.

                    continue;
                }

                if (emitting.Count > 0 && !emitting.Peek())
                {
                    continue;
                }

                var substituted = Substitute(line);

                // A \gset at the end of a line ends the statement with it, and there is no semicolon
                // in that form: "SELECT count(*) AS x FROM ... \gset".
                var gsetHere = substituted.Contains("\\gset", StringComparison.Ordinal);
                if (gsetHere)
                {
                    substituted = substituted[..substituted.IndexOf("\\gset", StringComparison.Ordinal)].TrimEnd();
                }

                statement.AppendLine(substituted);

                var endsStatement = substituted.TrimEnd().EndsWith(';');
                if (!endsStatement && !gsetHere)
                {
                    continue;
                }

                var sql = statement.ToString().Trim();
                statement.Clear();

                if (sql.Length == 0)
                {
                    continue;
                }

                await ExecuteAsync(sql, gsetHere || gsetPending);
                gsetPending = false;

                if (ShouldQuit)
                {
                    break;
                }
            }

            return _schemaFailures;
        }

        private bool ShouldQuit { get; set; }

        private bool EvaluateCondition(string condition)
        {
            // "{?name}" asks whether a variable was set at all.
            if (condition.StartsWith(":{?", StringComparison.Ordinal))
            {
                var name = condition[3..].TrimEnd('}');
                return _variables.ContainsKey(name);
            }

            if (condition.StartsWith(":", StringComparison.Ordinal))
            {
                var name = condition[1..].Trim();
                var conditionValue = _variables.TryGetValue(name, out var found) ? found : string.Empty;

                // Lower-cased, because a boolean that came out of the database arrives as "True" and
                // psql's own truth test is case-insensitive. Reading it as anything else turned a
                // `\if` on a query result into "false", and the branch it guards did not run - which
                // is how a DELETE could be skipped by a script that reported no error.
                var normalized = conditionValue.Trim().ToLowerInvariant();

                return normalized switch
                {
                    "t" or "true" or "on" or "yes" or "1" => true,
                    "f" or "false" or "off" or "no" or "0" or "" => false,
                    _ => double.TryParse(normalized, out var number) && number != 0,
                };
            }

            // Anything else is a psql expression this interpreter does not implement. Treating it as
            // false would silently skip a branch; treating it as true would run one. Failing loudly
            // is the only honest answer.
            throw new NotSupportedException(
                $"psql condition '{condition}' is not implemented by the test's psql interpreter");
        }

        private string Substitute(string line)
        {
            var result = new StringBuilder();
            var inString = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '\'')
                {
                    inString = !inString;
                    result.Append(c);
                    continue;
                }
                if (inString || c != ':')
                {
                    result.Append(c);
                    continue;
                }

                // A cast, not a variable: "n::text" is a cast to text, and a variable named "text"
                // would be substituted out of it. psql reads "::" the same way.
                if (i + 1 < line.Length && line[i + 1] == ':')
                {
                    result.Append("::");
                    i++;
                    continue;
                }

                // :'name' is a quoted value, :name is a raw one.
                if (i + 1 < line.Length && line[i + 1] == '\'')
                {
                    var end = line.IndexOf('\'', i + 2);
                    if (end < 0)
                    {
                        result.Append(c);
                        continue;
                    }

                    var name = line[(i + 2)..end];
                    var quoted = _variables.TryGetValue(name, out var found) ? found : string.Empty;
                    result.Append('\'').Append(quoted.Replace("'", "''", StringComparison.Ordinal)).Append('\'');
                    i = end;
                    continue;
                }

                var identifierEnd = i + 1;
                while (identifierEnd < line.Length && (char.IsLetterOrDigit(line[identifierEnd]) || line[identifierEnd] == '_'))
                {
                    identifierEnd++;
                }

                if (identifierEnd == i + 1)
                {
                    result.Append(c);
                    continue;
                }

                var raw = line[(i + 1)..identifierEnd];
                result.Append(_variables.TryGetValue(raw, out var value) ? value : string.Empty);
                i = identifierEnd - 1;
            }

            return result.ToString();
        }

        private async Task ExecuteAsync(string sql, bool capture)
        {
            // The script's own transaction is dropped here, and the temp tables it creates are kept
            // to the end of the script instead of the end of the transaction.
            //
            // The reason is error recovery: in one transaction, the first statement that fails
            // aborts the whole thing and every statement after it answers "current transaction is
            // aborted" - which would be silently tolerated here and would hide the failures this
            // test exists to find. In autocommit each statement stands on its own, so a script that
            // names a dropped table is caught at the statement that names it.
            //
            // `ON COMMIT DROP` would then drop each temp table the moment it was created, because
            // each statement is its own transaction - so it becomes `ON COMMIT PRESERVE ROWS`,
            // which is what psql's own BEGIN...COMMIT gives these tables.
            var text = Regex.Replace(sql, @"^\s*(BEGIN|COMMIT)\s*;\s*$", string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
            text = text.Replace("ON COMMIT DROP", "ON COMMIT PRESERVE ROWS", StringComparison.OrdinalIgnoreCase);

            if (text.Trim().Length == 0)
            {
                return;
            }

            await using var command = new NpgsqlCommand(text, _connection);
            _executed.Add(FirstLine(text));

            try
            {
                if (capture)
                {
                    await using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        for (var i = 0; i < reader.FieldCount; i++)
                        {
                            _variables[reader.GetName(i)] = reader.IsDBNull(i) ? string.Empty : reader.GetValue(i).ToString()!;
                        }
                    }
                }
                else
                {
                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (PostgresException exception)
            {
                if (IsSchemaFailure(exception))
                {
                    _schemaFailures.Add($"[{exception.SqlState}] {exception.MessageText} -- in: {FirstLine(sql)}");
                }
                else
                {
                    _dataFailures.Add($"[{exception.SqlState}] {exception.MessageText} -- in: {FirstLine(sql)}");
                }
            }
            catch (NpgsqlException exception)
            {
                _schemaFailures.Add($"[connection] {exception.Message} -- in: {FirstLine(sql)}");
            }
        }

        /// <summary>
        /// The failures that mean the script cannot run at all, as opposed to the ones that mean the
        /// database has nothing to seed.
        /// </summary>
        private static bool IsSchemaFailure(PostgresException exception) => exception.SqlState switch
        {
            "42P01" => true,   // undefined_table
            "42703" => true,   // undefined_column
            "42704" => true,   // undefined_object
            "42883" => true,   // undefined_function
            "42P07" => true,   // duplicate_table
            "42710" => true,   // duplicate_object
            "42P10" => true,   // duplicate_column
            "3F000" => true,   // invalid_schema_name
            "42601" => true,   // syntax_error
            "42725" => true,   // ambiguous_column
            _ => false,
        };

        private static string FirstLine(string sql)
        {
            var line = sql.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? sql;
            return line.Trim().Length > 160 ? line.Trim()[..160] + "..." : line.Trim();
        }

        /// <summary>
        /// Removes a <c>--</c> comment, which psql also removes. Quote-aware, because a doubled
        /// quote inside a string literal is not the end of it.
        /// </summary>
        private static string StripComment(string line)
        {
            var inString = false;

            for (var i = 0; i < line.Length - 1; i++)
            {
                if (line[i] == '\'')
                {
                    inString = !inString;
                    continue;
                }

                if (!inString && line[i] == '-' && line[i + 1] == '-')
                {
                    return line[..i];
                }
            }

            return line;
        }
    }
}
