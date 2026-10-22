using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FuelFlow.Features.Contracts.UpdateLegalEntity;

/// <summary>
/// Updates a legal entity the caller owns.
/// </summary>
public sealed class UpdateLegalEntityCommandHandler
{
    private readonly ApplicationDbContext _context;

    public UpdateLegalEntityCommandHandler(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// Writes the new details onto the company the caller owns.
    /// </summary>
    /// <param name="command">The caller, the company and the details to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<LegalEntityWriteResult> HandleAsync(
        UpdateLegalEntityCommand command,
        CancellationToken cancellationToken = default)
    {
        var problem = command.Input.DescribeProblem();
        if (problem != null)
            return LegalEntityWriteResult.Invalid(problem);

        if (command.UserId is null)
            return LegalEntityWriteResult.Unauthenticated();

        var userId = command.UserId.Value;

        var entity = await _context.LegalEntities
            .FirstOrDefaultAsync(e => e.Id == command.Id && e.UserId == userId, cancellationToken);

        if (entity is null)
            return LegalEntityWriteResult.NotFound("Legal entity not found");

        entity.Name = command.Input.Name;
        entity.Edrpou = command.Input.Edrpou;
        entity.VatNumber = command.Input.VatNumber;
        entity.Address = command.Input.Address;
        entity.DirectorName = command.Input.DirectorName;
        entity.Phone = command.Input.Phone;
        entity.Email = command.Input.Email;
        entity.UpdatedAtUtc = SystemClock.UtcNow;
        _context.LegalEntities.Update(entity);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Duplicate legal_entities.edrpou (globally unique) — surface as a clean 409.
            return LegalEntityWriteResult.Conflict("EDRPOU already registered");
        }

        return LegalEntityWriteResult.Done(LegalEntityDto.From(entity));
    }
}
