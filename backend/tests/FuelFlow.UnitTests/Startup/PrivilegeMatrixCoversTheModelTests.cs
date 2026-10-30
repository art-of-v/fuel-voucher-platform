using System.Runtime.CompilerServices;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Startup;

/// <summary>
/// The privilege matrix in <c>deploy/database-privileges.tsv</c> and the EF model describe the same
/// write surface, and neither is allowed to move without the other.
/// </summary>
/// <remarks>
/// <para>
/// The matrix is hand-written: it says what <c>fuelflow_app</c> may do, table by table, and the
/// deploy script applies exactly that and revokes everything else. The model says what the
/// application will try to do. When the two disagree in the direction of the model, the first write
/// that touches a forgotten column fails in production with a <c>42501</c> and nothing else to go on
/// - planning #725, where <c>monobank_statement_entries</c> was classified insert-only while the
/// importer moved <c>last_seen_at_utc</c> on every pass, and every hourly import for both merchants
/// failed for two days.
/// </para>
/// <para>
/// The matrix alone cannot tell a table that is deliberately append-only from one whose write path
/// nobody wrote down, so the model states the difference: <see cref="IAppendOnlyEntity"/> for rows
/// that are never rewritten, <see cref="MutableAfterInsertAttribute"/> for the few columns a row
/// closed in place may move, and an ordinary entity for everything else. This test holds the file
/// to those declarations in both directions - a grant wider than the model is a privilege nobody
/// needs, and a declaration the file does not grant is the #725 incident waiting for its first
/// write.
/// </para>
/// <para>
/// It lives in the unit suite because the comparison is model-to-file: no database is opened, and
/// the runtime half - that the live database actually carries these grants - is
/// <c>AppRolePrivilegeGuardTests</c> in the integration suite.
/// </para>
/// </remarks>
public sealed class PrivilegeMatrixCoversTheModelTests
{
    private const string MatrixRelativePath = "deploy/database-privileges.tsv";

    /// <summary>
    /// The <c>public</c> tables the matrix lists that are not EF entities, each for a reason the
    /// file states. A new table outside the model has to be added here on purpose.
    /// </summary>
    private static readonly HashSet<string> TablesOutsideTheModel = new(StringComparer.Ordinal)
    {
        // A view over payments for humans; never mapped, read-only for the application.
        "refunds",
        // EF's own migration history. The migrator owns it and the application never reads it.
        "__EFMigrationsHistory",
    };

    [Fact]
    public void Every_mapped_table_is_in_the_matrix()
    {
        var model = ReadModelTables();
        var matrix = ReadMatrix();

        var missing = model.Keys.Where(table => !matrix.ContainsKey(table)).ToList();

        missing.Should().BeEmpty(
            "a table the model maps but the matrix never names gets no grant at all, and the first "
            + "query against it is a 42501 in production");
    }

    [Fact]
    public void Every_table_in_the_matrix_is_mapped_or_a_declared_exception()
    {
        var model = ReadModelTables();
        var matrix = ReadMatrix();

        var extra = matrix.Keys.Except(model.Keys, StringComparer.Ordinal).ToList();

        extra.Should().BeEquivalentTo(
            TablesOutsideTheModel,
            "a matrix row with no entity behind it is either a view (say so here) or a table nobody "
            + "dropped or mapped");
    }

    [Fact]
    public void An_append_only_entity_grants_no_update_and_no_delete()
    {
        var model = ReadModelTables();
        var matrix = ReadMatrix();

        var appendOnly = model.Values.Where(entity => entity.IsAppendOnly).ToList();
        appendOnly.Should().NotBeEmpty("the marker is only a declaration if something carries it");

        foreach (var entity in appendOnly)
        {
            var row = RowOf(matrix, entity.Table);

            entity.MarkedProperties.Should().BeEmpty(
                $"'{entity.Table}' is declared append-only, so it has no column that moves after insert");

            row.TableWideUpdate.Should().BeFalse(
                $"'{entity.Table}' is written once; a table-level UPDATE would let any save rewrite the record");
            row.ColumnUpdates.Should().BeEmpty(
                $"'{entity.Table}' is written once; a column-level UPDATE is the shape a closed-in-place row uses");
            row.Delete.Should().BeFalse(
                $"'{entity.Table}' is the record of what happened, and the traffic role does not erase records");
        }
    }

    [Fact]
    public void A_row_closed_in_place_grants_exactly_the_columns_it_marks()
    {
        var model = ReadModelTables();
        var matrix = ReadMatrix();

        var inPlace = model.Values.Where(entity => entity.MarkedProperties.Count > 0).ToList();
        inPlace.Should().NotBeEmpty("the marker is only a declaration if something carries it");

        foreach (var entity in inPlace)
        {
            var row = RowOf(matrix, entity.Table);

            row.TableWideUpdate.Should().BeFalse(
                $"'{entity.Table}' keeps its evidence; a table-level UPDATE would let a later step rewrite the row");

            var markedColumns = new List<string>();
            foreach (var property in entity.MarkedProperties)
            {
                entity.ColumnByProperty.Should().ContainKey(property,
                    $"'{entity.Table}.{property}' is marked as movable after insert, so it has to be a mapped column");
                markedColumns.Add(entity.ColumnByProperty[property]);
            }

            row.ColumnUpdates.Should().BeEquivalentTo(markedColumns,
                $"the grant for '{entity.Table}' and the columns the model says later steps write are the "
                + "same set: anything else is either a 42501 waiting to happen or a privilege nobody uses");

            row.Delete.Should().BeFalse(
                $"'{entity.Table}' is closed in place, never deleted by the traffic role");
        }
    }

    [Fact]
    public void A_mutable_entity_grants_update_on_the_whole_table()
    {
        var model = ReadModelTables();
        var matrix = ReadMatrix();

        var mutable = model.Values
            .Where(entity => !entity.IsAppendOnly && entity.MarkedProperties.Count == 0)
            .ToList();

        foreach (var entity in mutable)
        {
            RowOf(matrix, entity.Table).TableWideUpdate.Should().BeTrue(
                $"'{entity.Table}' is ordinary product data, so the model may write any column - and a "
                + "column-level grant left a write path without its column in planning #725");
        }
    }

    private static MatrixRow RowOf(IReadOnlyDictionary<string, MatrixRow> matrix, string table)
    {
        matrix.Should().ContainKey(table);
        return matrix[table];
    }

    private static IReadOnlyDictionary<string, EntityTable> ReadModelTables()
    {
        using var context = CreateContext();
        var tables = new Dictionary<string, EntityTable>(StringComparer.Ordinal);

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is null)
                continue;

            var schema = entityType.GetSchema();
            if (schema is not null && schema != "public")
                continue;

            var columnByProperty = entityType.GetProperties()
                .Where(property => !property.IsShadowProperty())
                .Select(property => new { property.Name, Column = property.GetColumnName() })
                .Where(property => property.Column is not null)
                .ToDictionary(property => property.Name, property => property.Column!, StringComparer.Ordinal);

            var markedProperties = entityType.ClrType.GetProperties()
                .Where(property => property.IsDefined(typeof(MutableAfterInsertAttribute), false))
                .Select(property => property.Name)
                .ToList();

            tables[table] = new EntityTable(table, entityType.ClrType, columnByProperty, markedProperties);
        }

        return tables;
    }

    private static Dictionary<string, MatrixRow> ReadMatrix()
    {
        var path = Path.Combine(LocateRepositoryRoot(), MatrixRelativePath);
        var rows = new Dictionary<string, MatrixRow>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split('\t');
            if (parts.Length < 3 || parts[0] != "public" || parts[1] == "table")
                continue;

            // "SIUD" is a table-level grant; "SI+U(a,b)" names the columns a later step may move.
            var privileges = parts[2];
            var columnOpen = privileges.IndexOf("U(", StringComparison.Ordinal);
            var columnUpdates = Array.Empty<string>();
            var tableFlags = privileges;

            if (columnOpen >= 0)
            {
                var columnClose = privileges.IndexOf(')', columnOpen);
                columnUpdates = privileges[(columnOpen + 2)..columnClose]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                tableFlags = privileges[..columnOpen] + privileges[(columnClose + 1)..];
            }

            rows[parts[1]] = new MatrixRow(
                parts[1],
                TableWideUpdate: tableFlags.Contains('U'),
                ColumnUpdates: columnUpdates,
                Delete: tableFlags.Contains('D'));
        }

        return rows;
    }

    /// <summary>
    /// The model, built against the relational provider so tables and columns have names. No
    /// connection is opened: the model is a mapping, not a query.
    /// </summary>
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=fuelflow;Username=postgres;Password=postgres")
            .Options;

        return new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    private static string LocateRepositoryRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Startup/<this>.cs
        var root = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.Parent!.FullName;
        if (Directory.Exists(Path.Combine(root, "backend")))
            return root;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "backend"))
                && Directory.Exists(Path.Combine(dir.FullName, "admin")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from either the test file path or the assembly directory.");
    }

    private sealed record EntityTable(
        string Table,
        Type ClrType,
        IReadOnlyDictionary<string, string> ColumnByProperty,
        IReadOnlyList<string> MarkedProperties)
    {
        public bool IsAppendOnly => typeof(IAppendOnlyEntity).IsAssignableFrom(ClrType);
    }

    private sealed record MatrixRow(
        string Table,
        bool TableWideUpdate,
        IReadOnlyList<string> ColumnUpdates,
        bool Delete);
}
