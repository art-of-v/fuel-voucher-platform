using FuelFlow.Features.Auth.TwoFactor;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// Builds the second-factor service for a unit test.
/// </summary>
/// <remarks>
/// The service is a constructor argument of the verify handler, so every test that builds that
/// handler needs one. The cipher is the test cipher - nothing encrypted here outlives the test -
/// and the options are the production defaults, which is what makes the default the thing the
/// tests exercise.
/// </remarks>
public static class StaffTwoFactorTestFactory
{
    public static StaffTwoFactorService Build(ApplicationDbContext context, AuthOptions? authOptions = null) =>
        new(context,
            TestQrCipher.Instance,
            Options.Create(authOptions ?? new AuthOptions { DevBypass = false }),
            new Mock<ILogger<StaffTwoFactorService>>().Object);
}
