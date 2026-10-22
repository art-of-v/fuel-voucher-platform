using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FuelFlow.Features.Contracts.UpsertLegalEntityProfile;

/// <summary>
/// Writes the caller's own legal entity, creating it when the caller has none.
/// </summary>
public sealed class UpsertLegalEntityProfileCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly NotificationDispatcher _notifications;
    private readonly ContractWriter _contractWriter;

    public UpsertLegalEntityProfileCommandHandler(
        ApplicationDbContext context,
        NotificationDispatcher notifications,
        ContractWriter contractWriter)
    {
        _context = context;
        _notifications = notifications;
        _contractWriter = contractWriter;
    }

    /// <summary>
    /// Upserts the caller's legal entity by <c>UserId</c>.
    /// </summary>
    /// <param name="command">The caller and the details to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<LegalEntityWriteResult> HandleAsync(
        UpsertLegalEntityProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var problem = command.Input.DescribeProblem();
        if (problem != null)
            return LegalEntityWriteResult.Invalid(problem);

        if (command.UserId is null)
            return LegalEntityWriteResult.Unauthenticated();

        var userId = command.UserId.Value;

        var entity = await _context.LegalEntities
            .FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken);

        bool isNewEntity = false;
        if (entity is null)
        {
            entity = new LegalEntity
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                CreatedAtUtc = SystemClock.UtcNow
            };
            _context.LegalEntities.Add(entity);
            isNewEntity = true;
        }

        entity.Name = command.Input.Name;
        entity.Edrpou = command.Input.Edrpou;
        entity.VatNumber = command.Input.VatNumber;
        entity.Address = command.Input.Address;
        entity.DirectorName = command.Input.DirectorName;
        entity.Phone = command.Input.Phone;
        entity.Email = command.Input.Email;
        entity.UpdatedAtUtc = SystemClock.UtcNow;

        if (!isNewEntity)
        {
            _context.LegalEntities.Update(entity);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The EDRPOU is already registered to a different user. This endpoint upserts by
            // UserId, so the unique index that trips here is the one on legal_entities.edrpou —
            // surface it as a clean 409 rather than an unhandled 500 (mirrors Sign()'s Conflict).
            return LegalEntityWriteResult.Conflict("EDRPOU already registered");
        }

        // Only the initial creation is reported; this endpoint is an upsert and
        // profile edits are routine.
        if (isNewEntity)
        {
            await _notifications.CompanyCreatedAsync(entity.Id, entity.Name, entity.Edrpou, cancellationToken);

            // A company gets its contracts here, not by an operator's hand: the owner decided on
            // 2026-10-21 that a company is created with a contract for every provider, and the
            // client signs what already exists. The writer only adds what is missing, so calling
            // it again when a provider publishes a new text is the backfill too.
            await _contractWriter.EnsureContractsForAsync(entity, userId, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return LegalEntityWriteResult.Done(LegalEntityDto.From(entity));
    }
}
