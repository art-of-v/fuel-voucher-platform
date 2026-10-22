using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FuelFlow.Features.Contracts.CreateLegalEntity;

/// <summary>
/// Creates a new legal entity for the caller.
/// </summary>
public sealed class CreateLegalEntityCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly NotificationDispatcher _notifications;
    private readonly ContractWriter _contractWriter;

    public CreateLegalEntityCommandHandler(
        ApplicationDbContext context,
        NotificationDispatcher notifications,
        ContractWriter contractWriter)
    {
        _context = context;
        _notifications = notifications;
        _contractWriter = contractWriter;
    }

    /// <summary>
    /// Inserts a new legal entity for the caller.
    /// </summary>
    /// <param name="command">The caller and the details to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<LegalEntityWriteResult> HandleAsync(
        CreateLegalEntityCommand command,
        CancellationToken cancellationToken = default)
    {
        var problem = command.Input.DescribeProblem();
        if (problem != null)
            return LegalEntityWriteResult.Invalid(problem);

        if (command.UserId is null)
            return LegalEntityWriteResult.Unauthenticated();

        var userId = command.UserId.Value;

        var entity = new LegalEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Name = command.Input.Name,
            Edrpou = command.Input.Edrpou,
            VatNumber = command.Input.VatNumber,
            Address = command.Input.Address,
            DirectorName = command.Input.DirectorName,
            Phone = command.Input.Phone,
            Email = command.Input.Email,
            CreatedAtUtc = SystemClock.UtcNow,
            UpdatedAtUtc = SystemClock.UtcNow
        };
        _context.LegalEntities.Add(entity);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Duplicate legal_entities.edrpou (globally unique) — surface as a clean 409.
            return LegalEntityWriteResult.Conflict("EDRPOU already registered");
        }

        await _notifications.CompanyCreatedAsync(entity.Id, entity.Name, entity.Edrpou, cancellationToken);

        // A company gets its contracts here, not by an operator's hand: the owner decided on
        // 2026-10-21 that a company is created with a contract for every provider, and the
        // client signs what already exists. The writer only adds what is missing, so calling
        // it again when a provider publishes a new text is the backfill too.
        await _contractWriter.EnsureContractsForAsync(entity, userId, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return LegalEntityWriteResult.Done(LegalEntityDto.From(entity));
    }
}
