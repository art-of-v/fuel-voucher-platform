using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Xunit;

namespace FuelFlow.UnitTests;

/// <summary>
/// Every table and every column the model declares is snake_case.
/// </summary>
/// <remarks>
/// <para>
/// The review's F-16 found eight columns in <c>PascalCase</c> on an otherwise snake_case schema:
/// the developer forgot <c>HasColumnName</c>, so EF used the CLR property name. Those eight were
/// renamed (F-24, app PRs #1043 and #1047), and this is what stops the ninth.
/// </para>
/// <para>
/// The failure is silent, which is why it needs a test rather than a review rule. A column called
/// <c>IssuedAtUtc</c> is a valid column name: the migration applies, the application works, and the
/// only symptom is a report that has to quote it, a script that has to remember it, and a schema
/// where every other column is lowercase. Nothing fails on the day it happens.
/// </para>
/// <para>
/// The drift gate in CI does catch the same mistake - a forgotten name changes the model, and a
/// model that no longer matches the snapshot is refused by <c>migrations add</c> - but it says so in
/// the words of a migration ("the model has pending model changes"), which is a puzzle to read when
/// the cause is one forgotten line in a configuration file. This test says what is actually wrong.
/// </para>
/// <para>
/// The alternative the review proposed - adopting a naming convention and deleting the ~600
/// <c>HasColumnName</c> calls - is not available in this stack: EF Core 10 has no
/// <c>UseSnakeCaseNamingConvention</c>, and Npgsql's translator is not wired into EF's model names.
/// Writing that convention by hand is a separate piece of work, and it would have to be verified by
/// a from-zero schema diff. This test is the cheap half of the same promise: the convention stays
/// explicit, and a property that forgets its name fails the build with a sentence that names it.
/// </para>
/// </remarks>
public sealed class ColumnNamingConventionTests
{
    /// <summary>Lower case, digits and underscores, starting with a letter.</summary>
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    [Fact]
    public void EveryColumnInTheModel_IsSnakeCase()
    {
        using var context = CreateContext();

        var offenders = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties().Select(property => new
            {
                Table = entity.GetTableName(),
                Column = property.GetColumnName(),
                Property = property.Name
            }))
            .Where(x => x.Column is not null && !SnakeCase.IsMatch(x.Column))
            .Select(x => $"{x.Table}.{x.Column} (property {x.Property})")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "a column whose name is not snake_case is a column EF named after the C# property, " +
            "because the configuration forgot to say otherwise - and it breaks every report, " +
            "script and migration that has to quote it");
    }

    [Fact]
    public void EveryTableInTheModel_IsSnakeCase()
    {
        using var context = CreateContext();

        var offenders = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(table => table is not null && !SnakeCase.IsMatch(table))
            .OrderBy(table => table, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "a table whose name is not snake_case is the same omission one level up");
    }
}
