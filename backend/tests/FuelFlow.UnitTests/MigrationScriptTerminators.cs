using System.Text;
using FluentAssertions;
using FuelFlow.Features.Vouchers.Security;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.UnitTests;

/// <summary>
/// A raw SQL statement written with <c>migrationBuilder.Sql</c> has to carry its own terminating
/// semicolon, and nothing in the build tells you when it does not.
/// </summary>
/// <remarks>
/// <para>
/// EF hands each <see cref="SqlOperation"/> down the extended protocol as its own command, where a
/// trailing semicolon is optional - so <c>Migrate()</c> applies the statement either way. That is
/// why this went unnoticed for four months and why CI was green: the Testcontainers suite
/// migrates from zero on every run and passed.
/// </para>
/// <para>
/// <c>dotnet ef migrations script</c> is the exception. It writes those same commands into one
/// text file, and in a file the semicolon is what separates one statement from the next - psql
/// reads an unterminated statement and the one after it as a single malformed one. The script was
/// unparseable from line 437 onward and never reached the end of the migration history, which
/// breaks the migration dry run and any environment bootstrapped from a generated script.
/// </para>
/// <para>
/// This asserts the property rather than pinning the twenty statements that were broken, so the
/// next one fails here instead of in somebody's psql.
/// </para>
/// </remarks>
public sealed class MigrationScriptTerminators
{
    private static IEnumerable<(string Migration, string Direction, SqlOperation Operation)> RawSqlOperations()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=none;Username=none")
            .Options;

        using var context = new ApplicationDbContext(options, Cipher);

        // The assembly hands back TypeInfos; CreateMigration is what instantiates one and lets its
        // Up()/Down() run to produce the operation list this reads.
        var assembly = context.GetService<IMigrationsAssembly>();
        var activeProvider = context.GetService<IDatabaseProvider>().Name;

        foreach (var (id, type) in assembly.Migrations)
        {
            var migration = (Migration)assembly.CreateMigration(type, activeProvider);

            foreach (var operation in migration.UpOperations.OfType<SqlOperation>())
            {
                yield return (id, "Up", operation);
            }

            foreach (var operation in migration.DownOperations.OfType<SqlOperation>())
            {
                yield return (id, "Down", operation);
            }
        }
    }

    private static IQrCipher Cipher { get; } = new QrCipher(Options.Create(new QrCipherOptions
    {
        // 32 bytes exactly: the cipher refuses any other length, and this test needs a cipher only
        // because ApplicationDbContext takes one rather than a default.
        Key = Convert.ToBase64String(Encoding.UTF8.GetBytes("migration-sql-terminator-checks!"))
    }));

    [Fact]
    public void Every_Raw_Sql_Statement_Ends_With_A_Terminator()
    {
        var operations = RawSqlOperations().ToList();

        operations.Should().NotBeEmpty("the migration set is not empty, and this asserts nothing if it is");

        var unterminated = operations
            .Where(o => o.Operation.Sql.Trim() != "")
            .Where(o => !o.Operation.Sql.TrimEnd().EndsWith(';'))
            .Select(o => $"{o.Migration}.{o.Direction}: {Tail(o.Operation.Sql)}")
            .ToList();

        unterminated.Should().BeEmpty(
            "a migrationBuilder.Sql statement without a trailing ';' parses fine through Migrate() " +
            "and corrupts `dotnet ef migrations script`, because in a text file the semicolon is what " +
            "ends the statement. Add the ';' inside the string literal.");
    }

    /// <summary>
    /// The last line, trimmed: enough to identify the statement without printing a whole DO block.
    /// </summary>
    private static string Tail(string sql)
    {
        var last = sql.TrimEnd().Split('\n')[^1].Trim();
        return last.Length <= 90 ? last : "..." + last[^90..];
    }
}