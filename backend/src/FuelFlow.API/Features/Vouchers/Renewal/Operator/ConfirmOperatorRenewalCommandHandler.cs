using System.Text.Json;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers.Renewal.Checkout;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Renewal.Operator;

/// <summary>
/// An operator records, in the admin app, a renewal they performed off-platform on a CUSTOMER's
/// voucher. <see cref="SurchargeUah"/> is the доплата the customer already paid the operator (0 = free
/// compensation) — recorded for audit only, never a Monobank charge and never a stock reprice. Acting
/// user fields are filled from the caller's claims by the controller, not the client.
/// </summary>
public sealed class ConfirmOperatorRenewalCommand
{
    public Guid VoucherId { get; set; }
    public string TermCode { get; set; } = string.Empty;
    public decimal SurchargeUah { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }

    public Guid ActingUserId { get; set; }
    public string? ActingUserName { get; set; }
}

/// <summary>Outcome of a confirmed operator renewal — enough for the admin UI to render the result card.</summary>
public sealed class ConfirmOperatorRenewalResult
{
    public string Branch { get; set; } = string.Empty;
    public Guid VoucherId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public Guid? ReplacementVoucherId { get; set; }
    public string? ReplacementVoucherNumber { get; set; }
    public DateOnly OldExpiration { get; set; }
    public DateOnly NewExpiration { get; set; }
    public string TermCode { get; set; } = string.Empty;
    public decimal SurchargeUah { get; set; }
    public Guid CustomerUserId { get; set; }
    public string? CustomerName { get; set; }
}

/// <summary>
/// Applies the two-branch renewal (same rules as the mobile flow, via <see cref="VoucherRenewalEligibility"/>)
/// to a customer's voucher on an operator's behalf, inside one DB transaction with tracked EF updates.
/// Unlike <see cref="RenewalCheckoutCommandHandler"/> there is no payment, no device signature and no
/// trigger-window gate — the operator acts deliberately. Business rejections surface as
/// <see cref="VoucherRenewalException"/> with a stable code (controller maps <c>not_found</c>→404,
/// <c>no_stock</c>→409, the rest→400).
/// </summary>
public sealed class ConfirmOperatorRenewalCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ProviderEventService _events;

    public ConfirmOperatorRenewalCommandHandler(ApplicationDbContext context, ProviderEventService events)
    {
        _context = context;
        _events = events;
    }

    public async Task<ConfirmOperatorRenewalResult> HandleAsync(
        ConfirmOperatorRenewalCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!VoucherRenewalTerms.TryFromCode(command.TermCode, out var term))
            throw new VoucherRenewalException("unknown_term", $"Unknown renewal term '{command.TermCode}'.");

        if (command.SurchargeUah < 0)
            throw new VoucherRenewalException("invalid_surcharge", "Surcharge must be zero or positive.");

        // Tracked load (NoTracking is the DbContext default — mutations would silently no-op otherwise).
        // IgnoreQueryFilters so a soft-deleted row is distinguishable (→ not_found) rather than invisible.
        var voucher = await _context.FuelVouchers
            .AsTracking()
            .IgnoreQueryFilters()
            .Include(v => v.AssignedToUser)
            .FirstOrDefaultAsync(v => v.Id == command.VoucherId, cancellationToken);

        if (voucher is null || voucher.IsDeleted)
            throw new VoucherRenewalException("not_found", "Voucher not found.");

        if (voucher.AssignedToUserId is null)
            throw new VoucherRenewalException("not_customer_voucher", "Only a customer-owned voucher can be renewed here; this is unassigned stock.");

        if (!VoucherRenewalEligibility.IsRenewableStatus(voucher.Status))
            throw new VoucherRenewalException("not_renewable", $"A voucher in status '{voucher.Status}' cannot be renewed.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var branch = voucher.CustomerExpirationDate >= today
            ? VoucherRenewalBranch.Extend
            : VoucherRenewalBranch.Replace;

        var oldExpiration = voucher.CustomerExpirationDate;
        var now = DateTime.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        Guid? replacementVoucherId = null;
        string? replacementVoucherNumber = null;
        DateOnly newExpiration;

        if (branch == VoucherRenewalBranch.Extend)
        {
            newExpiration = VoucherRenewalEligibility.NewExpirationForExtend(oldExpiration, term);
            voucher.CustomerExpirationDate = newExpiration;
            voucher.Status = VoucherStatus.Assigned; // keep it the customer's active voucher
            voucher.UpdatedAtUtc = now;
        }
        else
        {
            var minExpiration = VoucherRenewalEligibility.MinStockExpirationForReplace(today, term);
            var providerLower = voucher.Provider.ToLower();

            // Oldest-eligible first, mirroring FulfillmentService.FindReplacementVoucherAsync.
            var stock = await _context.FuelVouchers
                .AsTracking()
                .Where(v => v.Status == VoucherStatus.Available
                         && v.Provider.ToLower() == providerLower
                         && v.FuelTypeId == voucher.FuelTypeId
                         && v.Liters == voucher.Liters
                         && v.ProviderExpirationDate >= minExpiration)
                .OrderBy(v => v.ProviderExpirationDate)
                .FirstOrDefaultAsync(cancellationToken);

            if (stock is null)
                throw new VoucherRenewalException(
                    "no_stock",
                    "No replacement voucher is available in stock for this provider, fuel and volume. Import stock first.");

            stock.Status = VoucherStatus.Assigned;
            stock.AssignedToUserId = voucher.AssignedToUserId;
            stock.LegalEntityId = voucher.LegalEntityId;
            stock.WorkerUserId = null;
            stock.UpdatedAtUtc = now;

            voucher.Status = VoucherStatus.Expired;
            voucher.UpdatedAtUtc = now;

            replacementVoucherId = stock.Id;
            replacementVoucherNumber = stock.VoucherNumber;
            newExpiration = stock.ProviderExpirationDate;
        }

        var branchCode = branch == VoucherRenewalBranch.Extend ? "extend" : "replace";

        _context.OperatorVoucherRenewals.Add(new OperatorVoucherRenewal
        {
            Id = Guid.NewGuid(),
            VoucherId = voucher.Id,
            ReplacementVoucherId = replacementVoucherId,
            CustomerUserId = voucher.AssignedToUserId.Value,
            Branch = branchCode,
            TermCode = term.Code(),
            OldExpiration = oldExpiration,
            NewExpiration = newExpiration,
            SurchargeUah = command.SurchargeUah,
            InvoiceNumber = string.IsNullOrWhiteSpace(command.InvoiceNumber) ? null : command.InvoiceNumber.Trim(),
            InvoiceDate = command.InvoiceDate,
            ActingUserId = command.ActingUserId,
            ActingUserName = command.ActingUserName,
            CreatedAtUtc = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        var customerName = ComposeName(voucher.AssignedToUser);
        var summary = branch == VoucherRenewalBranch.Extend
            ? $"Operator extended customer voucher {voucher.VoucherNumber} ({term.Code()}) → {newExpiration:yyyy-MM-dd}, surcharge ₴{command.SurchargeUah}"
            : $"Operator replaced lapsed customer voucher {voucher.VoucherNumber} from stock {replacementVoucherNumber} ({term.Code()}), surcharge ₴{command.SurchargeUah}";

        // old_value / new_value are jsonb columns — pass serialized JSON, never a bare string.
        var newValueJson = JsonSerializer.Serialize(new
        {
            branch = branchCode,
            voucherId = voucher.Id,
            replacementVoucherId,
            termCode = term.Code(),
            oldExpiration = oldExpiration.ToString("yyyy-MM-dd"),
            newExpiration = newExpiration.ToString("yyyy-MM-dd"),
            surchargeUah = command.SurchargeUah,
            customerUserId = voucher.AssignedToUserId.Value
        });

        await _events.RecordEventAsync(
            aggregateType: "Voucher",
            aggregateId: voucher.Id.ToString(),
            eventType: "VoucherRenewedByOperator",
            oldValue: null,
            newValue: newValueJson,
            changedByUserId: command.ActingUserId,
            changedByUserName: command.ActingUserName,
            summary: summary,
            providerId: voucher.Provider,
            ct: cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ConfirmOperatorRenewalResult
        {
            Branch = branchCode,
            VoucherId = voucher.Id,
            VoucherNumber = voucher.VoucherNumber,
            ReplacementVoucherId = replacementVoucherId,
            ReplacementVoucherNumber = replacementVoucherNumber,
            OldExpiration = oldExpiration,
            NewExpiration = newExpiration,
            TermCode = term.Code(),
            SurchargeUah = command.SurchargeUah,
            CustomerUserId = voucher.AssignedToUserId.Value,
            CustomerName = customerName
        };
    }

    private static string? ComposeName(SharedKernel.Domain.User? user)
    {
        if (user is null) return null;
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrEmpty(name) ? user.PhoneNumber : name;
    }
}
