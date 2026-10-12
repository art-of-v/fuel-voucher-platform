using FluentAssertions;
using FuelFlow.Features.Referral.CreateReferralCode;
using FuelFlow.Features.Referral.RedeemReferralCode;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Referral;

public sealed class ReferralCommandHandlersTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid ReferrerId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    private readonly ApplicationDbContext _context;

    public ReferralCommandHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static User CreateUser(Guid id, string phone, string? referralCode = null, string? referredBy = null)
    {
        return new User
        {
            Id = id,
            PhoneNumber = phone,
            ReferralCode = referralCode,
            ReferredBy = referredBy,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task CreateReferralCode_ShouldSetCode()
    {
        _context.Users.Add(CreateUser(UserId, "+380991111111"));
        await _context.SaveChangesAsync();

        var handler = new CreateReferralCodeCommandHandler(_context);
        var command = new CreateReferralCodeCommand(UserId.ToString(), "  abc-123  ");

        var response = await handler.HandleAsync(command);

        response.UserId.Should().Be(UserId);
        response.ReferralCode.Should().Be("ABC-123");

        var user = await _context.Users.FindAsync(UserId);
        user!.ReferralCode.Should().Be("ABC-123");
    }

    [Fact]
    public async Task CreateReferralCode_ShouldThrow_WhenUserIdInvalid()
    {
        var handler = new CreateReferralCodeCommandHandler(_context);
        var command = new CreateReferralCodeCommand("not-a-guid", "ABC123");

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("Invalid user ID");
    }

    [Fact]
    public async Task CreateReferralCode_ShouldThrow_WhenUserMissing()
    {
        var handler = new CreateReferralCodeCommandHandler(_context);
        var command = new CreateReferralCodeCommand(UserId.ToString(), "ABC123");

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("User not found");
    }

    [Fact]
    public async Task CreateReferralCode_ShouldThrow_WhenCodeAlreadySet()
    {
        _context.Users.Add(CreateUser(UserId, "+380991111111", referralCode: "OLD123"));
        await _context.SaveChangesAsync();

        var handler = new CreateReferralCodeCommandHandler(_context);
        var command = new CreateReferralCodeCommand(UserId.ToString(), "NEW123");

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Referral code already set");
    }

    [Fact]
    public async Task CreateReferralCode_ShouldThrow_WhenCodeTaken()
    {
        _context.Users.Add(CreateUser(ReferrerId, "+380992222222", referralCode: "ABC123"));
        _context.Users.Add(CreateUser(UserId, "+380991111111"));
        await _context.SaveChangesAsync();

        var handler = new CreateReferralCodeCommandHandler(_context);
        var command = new CreateReferralCodeCommand(UserId.ToString(), "abc123");

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Referral code already taken");
    }

    [Fact]
    public async Task RedeemReferralCode_ShouldLinkReferredBy()
    {
        _context.Users.Add(CreateUser(ReferrerId, "+380992222222", referralCode: "ABC123"));
        _context.Users.Add(CreateUser(UserId, "+380991111111"));
        await _context.SaveChangesAsync();

        var handler = new RedeemReferralCodeCommandHandler(_context);
        var command = new RedeemReferralCodeCommand(UserId.ToString(), "  abc123  ");

        var response = await handler.HandleAsync(command);

        response.UserId.Should().Be(UserId);
        response.RedeemedCode.Should().Be("ABC123");
        response.ReferrerId.Should().Be(ReferrerId);

        var redeemer = await _context.Users.FindAsync(UserId);
        redeemer!.ReferredBy.Should().Be("ABC123");
    }

    [Fact]
    public async Task RedeemReferralCode_ShouldThrow_WhenCodeNotFound()
    {
        _context.Users.Add(CreateUser(UserId, "+380991111111"));
        await _context.SaveChangesAsync();

        var handler = new RedeemReferralCodeCommandHandler(_context);
        var command = new RedeemReferralCodeCommand(UserId.ToString(), "ZZZ999");

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Referral code not found");
    }

    [Fact]
    public async Task RedeemReferralCode_ShouldThrow_WhenSelfRedeem()
    {
        _context.Users.Add(CreateUser(UserId, "+380991111111", referralCode: "ABC123"));
        await _context.SaveChangesAsync();

        var handler = new RedeemReferralCodeCommandHandler(_context);
        var command = new RedeemReferralCodeCommand(UserId.ToString(), "abc123");

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Cannot redeem your own referral code");
    }

    [Fact]
    public async Task RedeemReferralCode_ShouldThrow_WhenAlreadyRedeemed()
    {
        _context.Users.Add(CreateUser(UserId, "+380991111111", referredBy: "ABC123"));
        await _context.SaveChangesAsync();

        var handler = new RedeemReferralCodeCommandHandler(_context);
        var command = new RedeemReferralCodeCommand(UserId.ToString(), "ZZZ999");

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Referral code already redeemed");
    }
}
