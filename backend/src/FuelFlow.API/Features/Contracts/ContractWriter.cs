using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts;

/// <summary>
/// Gives a company its contracts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The contracts table had a foreign key to a user, a company and a brand,
/// a validity term, and no writer: nothing in the platform created a contract row, so
/// <c>GET /api/legal-entity/contracts</c> answered with an empty list for every customer,
/// always, and the mobile's "REVIEW THE TEXT" had nothing to review. The feature was a screen
/// with no documents behind it.
/// </para>
/// <para>
/// <b>What the owner decided (2026-10-21).</b> Contracts are created automatically: when a
/// company is created it gets a contract for each provider, and the operator does nothing -
/// the client signs what already exists. The text lives once per provider and is versioned,
/// and a contract says which version it was signed against.
/// </para>
/// <para>
/// <b>One contract per provider, and only where there is a text to sign.</b> A provider whose
/// text has never been written has no contract for a company: an empty document with a term and
/// a "sign" button is worse than no document, because it looks like something to sign. The
/// exception is a company created before the text existed, and it is visible as exactly that.
/// </para>
/// <para>
/// <b>Idempotent on purpose.</b> The writer adds what is missing and changes nothing else, so
/// it can be called again when a provider publishes a new text - which is the backfill the
/// owner's question left open, and the answer to it that needs no separate job.
/// </para>
/// </remarks>
public sealed class ContractWriter(ApplicationDbContext db)
{
    /// <summary>
    /// Makes sure the company has a contract for every provider whose text exists.
    /// </summary>
    /// <param name="entity">The company the contracts are for.</param>
    /// <param name="userId">The user who owns the company, and who signs the contracts.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many contracts were added.</returns>
    public async Task<int> EnsureContractsForAsync(
        LegalEntity entity,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // The current version of every provider. The table holds one row per provider per
        // version, so it is small - four providers and a handful of versions each - and reading
        // it whole is simpler than a query that has to say "the highest version" in SQL.
        var versions = await db.ContractVersions
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var current = versions
            .GroupBy(v => v.BrandId)
            .Select(g => g.OrderByDescending(v => v.Version).First())
            .ToList();

        if (current.Count == 0)
        {
            // No provider has published a text yet, so there is nothing to sign. This is not an
            // error: it is the state of a platform whose operator has not written the terms.
            return 0;
        }

        var existing = await db.Contracts
            .Where(c => c.LegalEntityId == entity.Id)
            .Select(c => c.BrandId)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var version in current)
        {
            if (existing.Contains(version.BrandId))
            {
                continue;
            }

            db.Contracts.Add(new Contract
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                LegalEntityId = entity.Id,
                BrandId = version.BrandId,
                TermsVersion = version.Version,
                CreatedAtUtc = now,
                // The term the text implies, not a term of our choosing: a contract made
                // against a version of the terms carries that version's term.
                ExpiresAtUtc = now.AddDays(version.TermDays),
            });

            added++;
        }

        return added;
    }
}
