using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.PublishVersion;

/// <summary>
/// Publishes a new version of a provider's text.
/// </summary>
/// <remarks>
/// <para>
/// The version number is the next one for that provider, and the database's unique index
/// on (brand, version) is what makes two concurrent publishes of the same number fail
/// rather than quietly produce two texts with the same name.
/// </para>
/// <para>
/// The text is trimmed, because what the customer reads is the text and the whitespace around it
/// is not part of it — and the length the screen reports is the length of the stored text, so an
/// operator can see that an upload was read rather than stored as an empty page.
/// </para>
/// </remarks>
public sealed class PublishContractVersionCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ContractWriter _contractWriter;

    public PublishContractVersionCommandHandler(ApplicationDbContext context, ContractWriter contractWriter)
    {
        _context = context;
        _contractWriter = contractWriter;
    }

    public async Task<PublishedContractVersion> HandleAsync(
        PublishContractVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await _context.ContractVersions
            .Where(v => v.BrandId == command.BrandId)
            .OrderByDescending(v => v.Version)
            .Select(v => (short?)v.Version)
            .FirstOrDefaultAsync(cancellationToken);

        // The text is the operator's, already refused by the controller when it is missing or
        // empty: what arrives here is either what they wrote or what the document carried.
        var body = command.Body!.Trim();

        var version = new ContractVersion
        {
            Id = Guid.CreateVersion7(),
            BrandId = command.BrandId,
            Version = (short)((current ?? 0) + 1),
            Body = body,
            TermDays = command.TermDays,
            CreatedAtUtc = SystemClock.UtcNow,
            CreatedByUserId = null
        };

        _context.ContractVersions.Add(version);
        await _context.SaveChangesAsync(cancellationToken);

        // The backfill: a company that was created before this provider published any text has
        // no contract for it, and without this it never would - the writer only runs when a
        // company is created. This is the owner's answer to "do the companies that already
        // existed get contracts too?".
        var contractsCreated = await _contractWriter.EnsureContractsForAllAsync(cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new PublishedContractVersion(
            version.Id,
            version.BrandId,
            version.Version,
            version.TermDays,
            version.CreatedAtUtc,
            version.Body.Length,
            contractsCreated);
    }
}
