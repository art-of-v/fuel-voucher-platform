using FluentAssertions;
using FuelFlow.Middleware;

namespace FuelFlow.UnitTests.Middleware;

/// <summary>
/// Pins the options the job dashboard is mounted with.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard can requeue, trigger and delete jobs, so it is mounted behind the Admin filter;
/// that much a request test can see. The token is the half it cannot: Hangfire answers a request
/// it does not trust with a bare 403, which looks like its authorization refusal on the wire.
/// </para>
/// <para>
/// The option used to be <c>IgnoreAntiforgeryToken = true</c>, written inline in the composition
/// root as a copy-pasted Hangfire escape hatch. With it on, any page an already-signed-in Admin
/// visited could post a trigger at the dashboard in their name, and neither the screen nor the
/// logs would say so. It lives in <see cref="HangfireDashboardSetup"/> now rather than in
/// <c>Program.cs</c> because the host needs a database and a Redis to boot, and an option only a
/// test that needs infrastructure can read is an option nobody checks.
/// </para>
/// </remarks>
public sealed class HangfireDashboardOptionsTests
{
    [Fact]
    public void Options_KeepTheRequestTokenCheck()
    {
        HangfireDashboardSetup.Options(allowDevelopmentBypass: false)
            .IgnoreAntiforgeryToken.Should().BeFalse(
                "the dashboard's own forms post to routes that change jobs, and the token is the " +
                "only thing that ties a post to the page that rendered it");
    }

    [Fact]
    public void TheDevelopmentBypass_DoesNotAlsoSwitchTheTokenOff()
    {
        // The bypass is an authorization decision for a local dashboard, not a request-forgery
        // one: a developer's browser can be made to post from another site exactly as an
        // operator's can.
        HangfireDashboardSetup.Options(allowDevelopmentBypass: true)
            .IgnoreAntiforgeryToken.Should().BeFalse();
    }

    [Fact]
    public void Options_MountTheDashboardBehindTheAdminFilter()
    {
        HangfireDashboardSetup.Options(allowDevelopmentBypass: false)
            .Authorization.Should().ContainSingle()
            .Which.Should().BeOfType<HangfireDashboardAuthorizationFilter>();
    }

    /// <summary>
    /// The option is only half of it. Hangfire resolves <c>IAntiforgery</c> with
    /// <c>GetService</c> and skips validation entirely when it comes back null - the dashboard
    /// boots, the option still reads <c>false</c>, and nothing is validated. MVC registers the
    /// service as a side effect today, so a composition-root guard is the only thing that would
    /// notice the day that stops being true.
    /// </summary>
    [Fact]
    public void TheCompositionRoot_RegistersAntiforgeryAndMountsThroughTheSetup()
    {
        var program = ReadSource("src/FuelFlow.API/Program.cs");

        program.Should().Contain("AddAntiforgery()",
            "Hangfire validates the token only when IAntiforgery can be resolved");
        program.Should().Contain("HangfireDashboardSetup.Options(",
            "the dashboard's options must come from the one place this suite can read them");
        program.Should().NotContain("IgnoreAntiforgeryToken",
            "a value set inline in the composition root is a decision nothing reads back");
    }

    private static string ReadSource(string relativePath)
    {
        // Walk up from the test output directory until the source file appears; counting fixed
        // levels is brittle because AppContext.BaseDirectory keeps its trailing separator.
        var candidate = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && candidate is not null; i++, candidate = Path.GetDirectoryName(candidate))
        {
            var path = Path.Combine(candidate, relativePath);
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        throw new FileNotFoundException(
            $"Could not locate {relativePath} walking up from {AppContext.BaseDirectory}; if the layout moved, update the path");
    }
}
