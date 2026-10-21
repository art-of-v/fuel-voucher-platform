using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.Company;

/// <summary>
/// Shared in-memory context + entity builders for the Company/B2B handler unit tests.
/// The transaction warning is ignored so handlers that open a DB transaction
/// (e.g. <c>FireWorkerCommandHandler</c>) run against the in-memory brand without throwing.
/// Phone numbers here are fully synthetic — never a real customer's.
/// </summary>
internal static class CompanyTestFactory
{
    public static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    public static User NewUser(
        Guid id,
        string phoneNumber,
        string? firstName = null,
        string? lastName = null,
        bool isDeleted = false)
        => new()
        {
            Id = id,
            PhoneNumber = phoneNumber,
            FirstName = firstName,
            LastName = lastName,
            IsDeleted = isDeleted,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

    public static LegalEntity NewLegalEntity(
        Guid id,
        Guid ownerUserId,
        string name = "ACME LLC",
        string edrpou = "12345678")
        => new()
        {
            Id = id,
            UserId = ownerUserId,
            Name = name,
            Edrpou = edrpou,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

    public static CompanyMember NewMember(Guid id, Guid legalEntityId, Guid workerUserId)
        => new()
        {
            Id = id,
            LegalEntityId = legalEntityId,
            WorkerUserId = workerUserId,
            JoinedAtUtc = TestClock.UtcNow
        };

    public static CompanyInvitation NewInvitation(
        Guid id,
        Guid legalEntityId,
        Guid ownerUserId,
        Guid workerUserId,
        string workerPhoneNumber,
        InvitationStatus status = InvitationStatus.Pending)
        => new()
        {
            Id = id,
            LegalEntityId = legalEntityId,
            OwnerUserId = ownerUserId,
            WorkerUserId = workerUserId,
            WorkerPhoneNumber = workerPhoneNumber,
            Status = status,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

    public static FuelVoucher NewVoucher(
        Guid id,
        VoucherStatus status,
        Guid? legalEntityId = null,
        Guid? assignedToUserId = null,
        Guid? workerUserId = null,
        string brand = "okko",
        string fuelTypeId = "okko-95",
        decimal liters = 50)
        => new()
        {
            Id = id,
            BrandId = BrandIds.ForSlug(brand),
            Brand = brand,
            FuelTypeId = ToUuid(fuelTypeId),
            Liters = liters,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
            VoucherNumber = $"V-{id.ToString()[..8]}",
            QrPayload = $"qr-{id}",
            Status = status,
            LegalEntityId = legalEntityId,
            AssignedToUserId = assignedToUserId,
            WorkerUserId = workerUserId,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
}