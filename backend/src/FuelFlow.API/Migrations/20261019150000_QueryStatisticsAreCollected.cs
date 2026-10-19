using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations;

// Hand-written, so it carries the two attributes discovery rides on - there is no generated
// .Designer.cs to carry them. MigrationDiscoveryTests fails the build if either is missing.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261019150000_QueryStatisticsAreCollected")]

/// <inheritdoc />
public partial class QueryStatisticsAreCollected : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // pg_stat_statements is the cheapest query-level visibility available: an extension, not an
        // agent or a sampler or anything else that has to be watched. Production had none, with
        // log_min_duration_statement off, so the database answered no question at all about which
        // statements the storefront actually runs.
        //
        // IF NOT EXISTS because the extension is infrastructure rather than application schema, and
        // an operator who added it by hand is not an error.
        //
        // There is deliberately NO check that shared_preload_libraries carries the library. An
        // earlier version of this migration raised, and it took the entire integration suite with
        // it - the Testcontainers Postgres did not preload it, so every test in the project failed
        // on a database nobody had misconfigured. A migration has to replay on any database holding
        // its history, and the preload belongs to the compose file, not to the schema. Installed
        // without the preload, the extension simply collects nothing, which is the pre-existing
        // state and not a new failure.
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_stat_statements;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately a no-op. Dropping the extension would discard the accumulated statistics,
        // which are the only record of what the database was doing before this migration, and the
        // extension costs nothing to leave installed.
        migrationBuilder.Sql("SELECT 1;");
    }
}