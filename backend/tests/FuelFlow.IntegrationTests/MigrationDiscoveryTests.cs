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
                .Options
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
}