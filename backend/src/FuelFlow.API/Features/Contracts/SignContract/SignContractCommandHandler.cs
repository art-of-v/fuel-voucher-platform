using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.SignContract;

/// <summary>
/// Records the customer's signature on one contract.
/// </summary>
public sealed class SignContractCommandHandler
{
    private readonly ApplicationDbContext _context;

    public SignContractCommandHandler(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// Records the signature, once the contract has been found, is still valid and is not signed
    /// already.
    /// </summary>
    /// <param name="command">The caller, the contract and the signature.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<SignContractResult> HandleAsync(
        SignContractCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.ContractId == Guid.Empty)
            return SignContractResult.Invalid("ContractId is required");

        if (command.UserId is null)
            return SignContractResult.Unauthenticated();

        var userId = command.UserId.Value;

        var contract = await _context.Contracts
            .FirstOrDefaultAsync(c => c.Id == command.ContractId && c.UserId == userId, cancellationToken);

        if (contract is null)
            return SignContractResult.NotFound("Contract not found");

        // A contract that has already expired is not signed. The term is the whole point of
        // having one: without this check the expiry would be a field nobody reads, and the
        // signature would outlive the agreement it is a signature of.
        if (!ContractValidity.CanBeSigned(contract.ExpiresAtUtc, SystemClock.UtcNow))
            return SignContractResult.Invalid($"This contract expired on {contract.ExpiresAtUtc:yyyy-MM-dd} and cannot be signed.");

        var alreadySigned = await _context.UserContracts
            .AnyAsync(uc => uc.ContractId == command.ContractId && uc.UserId == userId, cancellationToken);

        if (alreadySigned)
            return SignContractResult.Conflict("Contract already signed");

        var userContract = new UserContract
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            ContractId = command.ContractId,
            SignatureData = command.SignatureData,
            SignedAtUtc = SystemClock.UtcNow,
            // The version that was actually signed, frozen into the signature. A later version
            // of the terms does not rewrite what the customer read, and the audit trail has to
            // be able to say which one it was.
            TermsVersion = contract.TermsVersion
        };

        _context.UserContracts.Add(userContract);
        await _context.SaveChangesAsync(cancellationToken);

        return SignContractResult.Done(new ContractSignatureDto
        {
            Id = userContract.Id,
            ContractId = userContract.ContractId,
            SignedAt = userContract.SignedAtUtc
        });
    }
}
