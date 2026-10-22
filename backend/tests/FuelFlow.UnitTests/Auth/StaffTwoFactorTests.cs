using FluentAssertions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.TwoFactor;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// The second factor of a staff sign-in: the enrolment, the proof, the burning and the taking
/// back.
/// </summary>
public sealed class StaffTwoFactorTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public StaffTwoFactorTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // --- helpers ---------------------------------------------------------

    private StaffTwoFactorService BuildService() =>
        new(_context,
            TestQrCipher.Instance,
            Options.Create(new AuthOptions()),
            new Mock<ILogger<StaffTwoFactorService>>().Object);

    private async Task<User> SeedStaffAsync(string roleName = SeedRoles.AdminName)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = roleName, CreatedAtUtc = TestClock.UtcNow };
        _context.Roles.Add(role);

        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+38099111" + TestClock.UniqueDigits(4),
            RoleId = role.Id,
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    // --- the challenge ---------------------------------------------------

    [Fact]
    public async Task TheFirstChallenge_Enrols_AndCarriesASecret()
    {
        var user = await SeedStaffAsync();

        var challenge = await BuildService().IssueChallengeAsync(user, CancellationToken.None);

        // "Enrol" because the account has no factor yet: the client has to be given a secret.
        challenge.Status.Should().Be(TwoFactorStatus.Enroll);
        challenge.EnrollmentSecret.Should().NotBeNullOrWhiteSpace();
        challenge.ProvisioningUri.Should().Contain("otpauth://totp/FuelFlow:");
        challenge.Challenge.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TheSecretIsStoredEncrypted_AndTheChallengeOnlyAsItsHash()
    {
        var user = await SeedStaffAsync();

        var challenge = await BuildService().IssueChallengeAsync(user, CancellationToken.None);

        var row = await _context.StaffTwoFactors.SingleAsync();
        row.SecretCiphertext.Should().BeNull("the secret is not a factor until it is proven");
        row.PendingSecretCiphertext.Should().NotBeNull();
        row.PendingChallengeHash.Should().Be(SecretsHasher.Hash(challenge.Challenge));
        row.PendingChallengeHash.Should().NotBe(challenge.Challenge, "the challenge is stored as a hash");

        // The ciphertext is not the secret: a database dump does not hand out working codes.
        var stored = System.Text.Encoding.UTF8.GetString(row.PendingSecretCiphertext!);
        stored.Should().NotContain(challenge.EnrollmentSecret!);
    }

    [Fact]
    public async Task AnAccountWithAFactor_IsAskedForACode_AndGetsNoSecret()
    {
        var user = await SeedStaffAsync();
        var service = BuildService();

        var enrolment = await service.IssueChallengeAsync(user, CancellationToken.None);
        var code = Totp.ComputeCode(enrolment.EnrollmentSecret!, TestClock.UtcNow);
        (await service.CompleteAsync(enrolment.Challenge, code, CancellationToken.None)).Succeeded.Should().BeTrue();

        var next = await service.IssueChallengeAsync(user, CancellationToken.None);

        next.Status.Should().Be(TwoFactorStatus.Required);
        next.EnrollmentSecret.Should().BeNull("an enrolled factor is not re-issued");
        next.ProvisioningUri.Should().BeNull();
    }

    // --- the proof -------------------------------------------------------

    [Fact]
    public async Task TheCodeFromTheAuthenticator_EnrolsTheFactor()
    {
        var user = await SeedStaffAsync();
        var service = BuildService();

        var enrolment = await service.IssueChallengeAsync(user, CancellationToken.None);
        var code = Totp.ComputeCode(enrolment.EnrollmentSecret!, TestClock.UtcNow);

        var result = await service.CompleteAsync(enrolment.Challenge, code, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.UserId.Should().Be(user.Id);

        var row = await _context.StaffTwoFactors.SingleAsync();
        row.SecretCiphertext.Should().NotBeNull("the proven secret becomes the factor");
        row.PendingSecretCiphertext.Should().BeNull();
        row.PendingChallengeHash.Should().BeNull("a challenge is spent by the attempt that redeems it");
        row.LastVerifiedAtUtc.Should().Be(TestClock.UtcNow);
    }

    [Fact]
    public async Task AWrongCode_IsRefused_AndTheChallengeStays()
    {
        var user = await SeedStaffAsync();
        var service = BuildService();
        var enrolment = await service.IssueChallengeAsync(user, CancellationToken.None);

        var result = await service.CompleteAsync(enrolment.Challenge, "000000", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        var row = await _context.StaffTwoFactors.SingleAsync();
        row.FailedAttempts.Should().Be(1);
        row.PendingChallengeHash.Should().NotBeNull("one wrong guess does not spend the challenge");
    }

    [Fact]
    public async Task TheChallengeIsBurnedAfterFiveWrongCodes()
    {
        var user = await SeedStaffAsync();
        var service = BuildService();
        var enrolment = await service.IssueChallengeAsync(user, CancellationToken.None);

        for (var attempt = 0; attempt < StaffTwoFactorService.MaxFailedAttempts; attempt++)
        {
            (await service.CompleteAsync(enrolment.Challenge, "000000", CancellationToken.None)).Succeeded.Should().BeFalse();
        }

        var row = await _context.StaffTwoFactors.SingleAsync();
        row.PendingChallengeHash.Should().BeNull("the challenge is burned, so it cannot be brute-forced");

        // Even the right code cannot revive it.
        var code = Totp.ComputeCode(enrolment.EnrollmentSecret!, TestClock.UtcNow);
        (await service.CompleteAsync(enrolment.Challenge, code, CancellationToken.None)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AnUnknownChallenge_IsRefused()
    {
        var user = await SeedStaffAsync();
        var service = BuildService();
        await service.IssueChallengeAsync(user, CancellationToken.None);

        var result = await service.CompleteAsync("a-challenge-nobody-issued", "123456", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AnExpiredChallenge_IsRefused()
    {
        var user = await SeedStaffAsync();
        var service = BuildService();
        var enrolment = await service.IssueChallengeAsync(user, CancellationToken.None);

        // The row is made to look old rather than the clock being moved: the clock is global to
        // the suite, and a test that moves it moves it for everybody.
        var row = await _context.StaffTwoFactors.SingleAsync();
        row.PendingExpiresAtUtc = TestClock.UtcNow.AddMinutes(-1);
        await _context.SaveChangesAsync();

        var code = Totp.ComputeCode(enrolment.EnrollmentSecret!, TestClock.UtcNow);
        var result = await service.CompleteAsync(enrolment.Challenge, code, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task TheChallengeIsBoundToTheAccount_AndTheCodeOfAnotherSecretIsRefused()
    {
        var user = await SeedStaffAsync();
        var service = BuildService();
        var enrolment = await service.IssueChallengeAsync(user, CancellationToken.None);

        // A code computed from a different secret - what an attacker with the challenge but
        // without the authenticator app would produce.
        var wrongCode = Totp.ComputeCode(Totp.GenerateSecret(), TestClock.UtcNow);

        var result = await service.CompleteAsync(enrolment.Challenge, wrongCode, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    // --- taking it back --------------------------------------------------

    [Fact]
    public async Task Revoking_TakesTheFactorAway_AndTheNextSignInEnrolsAgain()
    {
        var user = await SeedStaffAsync();
        var service = BuildService();

        var enrolment = await service.IssueChallengeAsync(user, CancellationToken.None);
        var code = Totp.ComputeCode(enrolment.EnrollmentSecret!, TestClock.UtcNow);
        await service.CompleteAsync(enrolment.Challenge, code, CancellationToken.None);

        (await service.IsEnrolledAsync(user.Id, CancellationToken.None)).Should().BeTrue();

        (await service.RevokeAsync(user.Id, CancellationToken.None)).Should().BeTrue();

        (await service.IsEnrolledAsync(user.Id, CancellationToken.None)).Should().BeFalse();
        var row = await _context.StaffTwoFactors.SingleAsync();
        row.SecretCiphertext.Should().BeNull();

        var next = await service.IssueChallengeAsync(user, CancellationToken.None);
        next.Status.Should().Be(TwoFactorStatus.Enroll);
    }

    [Fact]
    public async Task RevokingAnAccountThatHasNoFactor_IsNotAFailure()
    {
        var user = await SeedStaffAsync();
        var service = BuildService();

        (await service.RevokeAsync(user.Id, CancellationToken.None)).Should().BeFalse("there was nothing to take back");
    }
}
