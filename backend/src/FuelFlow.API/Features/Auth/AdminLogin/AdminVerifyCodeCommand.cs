using FuelFlow.Features.Auth.Verify;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.AdminLogin;

public sealed record AdminVerifyCodeCommand(string PhoneNumber, string Code);

/// <summary>
/// Admin-panel verify. Enforces the same staff gate as <see cref="AdminSendCodeCommandHandler"/>
/// a second time - the request is only authorized if the phone still belongs to a non-deleted,
/// non-banned staff account - and then delegates to the shared <see cref="VerifyCodeCommandHandler"/>
/// with registration DISABLED, so an admin sign-in can never create an account. The staff re-check
/// closes the case where a normal user obtained a real code through the mobile flow and replays it
/// against the admin verify endpoint: without a staff role the code is rejected with the same
/// generic message as any invalid code.
/// </summary>
public sealed class AdminVerifyCodeCommandHandler
{
    private static readonly string[] StaffRoleNames =
        [SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName];

    private readonly ApplicationDbContext _context;
    private readonly IPhoneNumberService _phoneNumberService;
    private readonly VerifyCodeCommandHandler _inner;
    private readonly ILogger<AdminVerifyCodeCommandHandler> _logger;

    public AdminVerifyCodeCommandHandler(
        ApplicationDbContext context,
        IPhoneNumberService phoneNumberService,
        VerifyCodeCommandHandler inner,
        ILogger<AdminVerifyCodeCommandHandler> logger)
    {
        _context = context;
        _phoneNumberService = phoneNumberService;
        _inner = inner;
        _logger = logger;
    }

    public async Task<VerifyCodeResponse> HandleAsync(AdminVerifyCodeCommand command, CancellationToken cancellationToken)
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
            // Generic message - identical to a wrong code - so a non-staff caller learns nothing
            // about whether the number exists or what role it holds.
            _logger.LogWarning(
                "Admin verify refused for non-staff phone {PhoneNumber}",
                SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
            throw new UnauthorizedAccessException("Invalid or expired verification code");
        }

        return await _inner.HandleAsync(
            new VerifyCodeCommand(command.PhoneNumber, command.Code),
            cancellationToken,
            allowRegistration: false);
    }
}
