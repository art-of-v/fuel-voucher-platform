using FuelFlow.Features.Vouchers.Security;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;

namespace FuelFlow.API;

/// <summary>
/// Builds a context for <c>dotnet ef</c>, outside the application container.
/// </summary>
/// <remarks>
/// The cipher here is a throwaway. Design-time tooling builds the model to diff it against the last
/// snapshot and never writes a row, so no key material is needed - and requiring a real deployment
/// key just to run <c>migrations add</c> would mean a developer could not work on the schema without
/// production credentials. What must not happen is the model being built with the QR column unmapped
/// or misconfigured, so the placeholder key is a real 32-byte AES-256 key and every other
/// requirement on it still applies.
/// </remarks>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        // The cap is on this string too: the guard that keeps every pool capped does not make an
        // exception for design-time tooling, and one capped connection changes nothing here.
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=fuelflow;Username=postgres;Password=postgres;SSL Mode=Disable;Trust Server Certificate=true;Maximum Pool Size=5");

        return new ApplicationDbContext(
            optionsBuilder.Options,
            new QrCipher(Options.Create(new QrCipherOptions
            {
                Key = Convert.ToBase64String(new byte[32])
            })));
    }
}