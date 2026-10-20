using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Every migration has to be reversible, and the newest one is the one nobody has tried.
/// <para>
/// The migration history is append-only, so a migration with a broken <c>Down</c> is a trap:
/// there is no way to rewrite it, and the only thing that can be done about it is another
/// migration. The trap is found here rather than in production, because the failure is
/// silent until somebody rolls a schema back - which is exactly what a broken deployment
/// needs.
/// </para>
/// <para>
/// This was not hypothetical. The migration that added the renewal's idempotency key had a
/// <c>Down</c> that CREATED the index instead of dropping it, because a text replacement
/// during authoring had rewritten both halves. The first symptom was a CI failure in a test
/// that rolls the schema back - which is the test doing its job - and the second symptom
/// would have been a migration that could not be undone.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class MigrationReversibilityTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public MigrationReversibilityTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task TheNewestMigration_CanBeRolledBackAndForwardAgain()
    {
        await using var context = CreateContext();

        var migrations = context.GetService<IMigrationsAssembly>().Migrations.Keys.OrderBy(x => x).ToList();
        migrations.Should().HaveCountGreaterThan(1, "there has to be a migration to roll back to");

        var target = migrations[^2];
        var migrator = context.Database.GetService<IMigrator>();

        try
        {
            // Back one step. If the Down does not undo its Up, this is where it shows.
            await migrator.MigrateAsync(target);

            // And forward again, which is what a deployment after a rollback needs.
            await migrator.MigrateAsync();

            using var verify = CreateContext();
            var applied = await verify.Database
                .SqlQuery<string>($"SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"")
                .ToListAsync();

            applied.Should().Contain(migrations[^1],
                "the newest migration is applied again after the round trip");
        }
        finally
        {
            // The schema is returned to head whatever happened, so one failure cannot
            // cascade into the next test.
            await migrator.MigrateAsync();
        }
    }
}
