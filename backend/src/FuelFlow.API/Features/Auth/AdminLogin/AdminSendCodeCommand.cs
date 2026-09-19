using FuelFlow.Features.Auth.SendCode;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.AdminLogin;

public sealed record AdminSendCodeCommand(string PhoneNumber);

/// <summary>
/// Admin-panel send-code. Unlike the public <see cref="SendCodeCommandHandler"/>, this checks
/// authorization BEFORE any code is generated or delivered (spec §13): a code is sent only when
/// the phone belongs to an existing, non-deleted, non-banned STAFF account. For every other phone
/// - unknown, normal user, deleted, banned - nothing is sent (no SMS, no email) and the response
/// is byte-for-byte identical to the success case, so the endpoint cannot be used to enumerate
/// which numbers are staff accounts.
/// </summary>
public sealed class AdminSendCodeCommandHandler
{
    // Staff role names, spelled out as a list because EF Core cannot translate the
    // SeedRoles.IsStaff method call into SQL. Mirrors the set enforced by the "Staff"
    // authorization policy and by AdminUserController.
    private static readonly string[] StaffRoleNames =
        [SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName];

    private readonly ApplicationDbContext _context;
    private readonly IPhoneNumberService _phoneNumberService;
    private readonly SendCodeCommandHandler _inner;
    private readonly ILogger<AdminSendCodeCommandHandler> _logger;

    public AdminSendCodeCommandHandler(
        ApplicationDbContext context,
        IPhoneNumberService phoneNumberService,
        SendCodeCommandHandler inner,
        ILogger<AdminSendCodeCommandHandler> logger)
    {
        _context = context;
        _phoneNumberService = phoneNumberService;
        _inner = inner;
        _logger = logger;
    }

    public async Task<SendCodeResponse> HandleAsync(AdminSendCodeCommand command, CancellationToken cancellationToken)
    {
        var phoneNumber = _phoneNumberService.Normalize(command.PhoneNumber);

        var isStaff = await _context.Users
            .AsNoTracking()
            .AnyAsync(u =>
                u.PhoneNumber == phoneNumber
                && !u.IsDeleted
                && !u.IsBanned
                && u.Role != null
                && StaffRoleNames.Contains(u.Role.Name),
                cancellationToken);

        if (!isStaff)
        {
            // Fail closed and SILENT: no code is created or delivered, but the caller sees the
            // same {success:true} a staff phone would produce. Do not reveal that the number is
            // not staff - that is the enumeration leak spec §13 forbids.
            _logger.LogWarning(
                "Admin send-code refused for non-staff phone {PhoneNumber}; no code sent",
                SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
            return new SendCodeResponse(true);
        }

        // Staff account confirmed - hand off to the shared pipeline, which generates the code,
        // stores its hash, and picks email vs SMS delivery.
        return await _inner.HandleAsync(new SendCodeCommand(command.PhoneNumber), cancellationToken);
    }
}
