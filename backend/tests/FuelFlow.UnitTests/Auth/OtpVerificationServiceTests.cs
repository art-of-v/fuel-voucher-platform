using FluentAssertions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// The step-up OTP check used by non-login flows (e.g. self-service email change). It must enforce
/// the same possession rules as the login path — newest active code wins, wrong guesses are counted
/// and the code is burned at the attempt cap, a correct code is single-use — without minting a
/// session. See <see cref="OtpVerificationService"/> for why it is separate from the login handler.
/// </summary>
public sealed class OtpVerificationServiceTests : IDisposable
{
    private const string Phone = "+380991112233";
    private readonly ApplicationDbContext _context;
    private readonly OtpVerificationService _service;

    public OtpVerificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _service = new OtpVerificationService(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task<VerificationCode> SeedCodeAsync(
        string code, string phone = Phone, bool used = false, DateTime? expiresAt = null, DateTime? createdAt = null)
    {
        var vc = new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            Code = SecretsHasher.Hash(code),
            IsUsed = used,
            ExpiresAtUtc = expiresAt ?? DateTime.UtcNow.AddMinutes(5),
            CreatedAtUtc = createdAt ?? DateTime.UtcNow
        };
        _context.VerificationCodes.Add(vc);
        await _context.SaveChangesAsync();
        return vc;
    }

    [Fact]
    public async Task TryConsume_WithCorrectCode_ReturnsTrueAndBurnsCode()
    {
        var seeded = await SeedCodeAsync("123456");

        var ok = await _service.TryConsumeAsync(Phone, "123456", CancellationToken.None);

        ok.Should().BeTrue();
        var vc = await _context.VerificationCodes.FindAsync(seeded.Id);
        vc!.IsUsed.Should().BeTrue();
        vc.UsedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task TryConsume_TrimsSubmittedCode()
    {
        await SeedCodeAsync("123456");

        (await _service.TryConsumeAsync(Phone, "  123456 ", CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task TryConsume_WithWrongCode_ReturnsFalseAndCountsAttempt()
    {
        var seeded = await SeedCodeAsync("123456");

        var ok = await _service.TryConsumeAsync(Phone, "000000", CancellationToken.None);

        ok.Should().BeFalse();
        var vc = await _context.VerificationCodes.FindAsync(seeded.Id);
        vc!.FailedAttempts.Should().Be(1);
        vc.IsUsed.Should().BeFalse("one wrong guess must not burn the code");
    }

    [Fact]
    public async Task TryConsume_BurnsCode_AfterMaxFailedAttempts()
    {
        var seeded = await SeedCodeAsync("123456");

        for (var i = 0; i < OtpVerificationService.MaxFailedAttempts; i++)
            (await _service.TryConsumeAsync(Phone, "000000", CancellationToken.None)).Should().BeFalse();

        var vc = await _context.VerificationCodes.FindAsync(seeded.Id);
        vc!.FailedAttempts.Should().Be(OtpVerificationService.MaxFailedAttempts);
        vc.IsUsed.Should().BeTrue();

        // The correct code can no longer be redeemed once the code is burned.
        (await _service.TryConsumeAsync(Phone, "123456", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task TryConsume_WithNoActiveCode_ReturnsFalse()
    {
        (await _service.TryConsumeAsync(Phone, "123456", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task TryConsume_WithExpiredCode_ReturnsFalse()
    {
        await SeedCodeAsync("123456", expiresAt: DateTime.UtcNow.AddMinutes(-1));

        (await _service.TryConsumeAsync(Phone, "123456", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task TryConsume_WithAlreadyUsedCode_ReturnsFalse()
    {
        await SeedCodeAsync("123456", used: true);

        (await _service.TryConsumeAsync(Phone, "123456", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task TryConsume_UsesNewestCode_WhenMultipleActive()
    {
        // Only the newest active code wins: an older still-live code with a different value is
        // never accepted (matches the login path's OrderByDescending(CreatedAtUtc)).
        await SeedCodeAsync("111111", createdAt: DateTime.UtcNow.AddMinutes(-2));
        await SeedCodeAsync("222222", createdAt: DateTime.UtcNow);

        (await _service.TryConsumeAsync(Phone, "111111", CancellationToken.None)).Should().BeFalse();
        (await _service.TryConsumeAsync(Phone, "222222", CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task TryConsume_IgnoresCodesForOtherPhones()
    {
        await SeedCodeAsync("123456", phone: "+380990000000");

        (await _service.TryConsumeAsync(Phone, "123456", CancellationToken.None)).Should().BeFalse();
    }
}
