using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.SharedModels;

/// <summary>
/// Outcome of resolving which legal entity a company-owner action targets.
/// </summary>
public readonly record struct OwnerCompanyResolution(Guid? LegalEntityId, bool ExplicitNotOwned)
{
    /// <summary>The owner simply has no legal entity at all (not an ownership violation).</summary>
    public bool NoCompany => LegalEntityId is null && !ExplicitNotOwned;
}

/// <summary>
/// Resolves which of an owner's legal entities a company action applies to. Epic #103 S0
/// dropped the one-entity-per-user constraint, so owner-facing endpoints can no longer assume
/// a single company — they must scope to an explicitly selected entity.
/// </summary>
public static class OwnerCompanyResolver
{
    /// <summary>
    /// Picks the legal entity an owner action applies to.
    /// <para>
    /// When <paramref name="explicitLegalEntityId"/> is supplied it must belong to the owner: if
    /// it does, the id is returned; if it does not, <see cref="OwnerCompanyResolution.ExplicitNotOwned"/>
    /// is set so the caller can reject it. When it is omitted the owner's oldest entity is used —
    /// this preserves the legacy single-company behaviour so already-shipped clients that do not
    /// send the id keep working.
    /// </para>
    /// </summary>
    public static async Task<OwnerCompanyResolution> ResolveOwnedLegalEntityAsync(
        this ApplicationDbContext context,
        Guid ownerUserId,
        Guid? explicitLegalEntityId,
        CancellationToken cancellationToken)
    {
        if (explicitLegalEntityId is { } requested)
        {
            var owns = await context.LegalEntities
                .AsNoTracking()
                .AnyAsync(x => x.Id == requested && x.UserId == ownerUserId, cancellationToken);

            return owns
                ? new OwnerCompanyResolution(requested, false)
                : new OwnerCompanyResolution(null, true);
        }

        var fallback = await context.LegalEntities
            .AsNoTracking()
            .Where(x => x.UserId == ownerUserId)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new OwnerCompanyResolution(fallback, false);
    }
}
