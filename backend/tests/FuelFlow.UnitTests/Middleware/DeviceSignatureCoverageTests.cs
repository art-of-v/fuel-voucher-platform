using System.Security.Claims;
using FluentAssertions;
using FuelFlow.Features.Auth.SharedServices;
using FuelFlow.Middleware;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Middleware;

/// <summary>
/// The signed set has to grow without breaking the phones that are already out. These pin both
/// halves: checkout is enforced from every version, and the voucher and cancellation routes are
/// enforced only from the app version that signs them.
/// </summary>
public sealed class DeviceSignatureCoverageTests : IDisposable
{
    private const string MarkerId = "3f1c8d2a-1b4e-4a7f-9a01-2c3d4e5f6a7b";

    private readonly ApplicationDbContext _context;

    public DeviceSignatureCoverageTests()
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

    [Theory]
    [InlineData("1.2.0")]
    [InlineData("1.3.0")]
    [InlineData(null)]
    public async Task Checkout_RequiresTheSignatureFromEveryVersion(string? appVersion)
    {
        var (nextCalled, status, _) = await InvokeAsync(HttpMethods.Post, "/api/purchases", appVersion);

        nextCalled.Should().BeFalse();
        status.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Theory]
    [InlineData("/api/vouchers/" + MarkerId + "/mark-used")]
    [InlineData("/api/vouchers/" + MarkerId + "/restore")]
    [InlineData("/api/vouchers/" + MarkerId + "/mark-used/")]
    public async Task VoucherActions_AreAllowedForABuildThatDoesNotSignThemYet(string path)
    {
        var (nextCalled, status, _) = await InvokeAsync(HttpMethods.Patch, path, "1.2.0");

        nextCalled.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("/api/vouchers/" + MarkerId + "/mark-used")]
    [InlineData("/api/vouchers/" + MarkerId + "/restore")]
    [InlineData("/api/vouchers/" + MarkerId + "/mark-used/")]
    public async Task VoucherActions_RequireTheSignatureOnceTheGateVersionIsInstalled(string path)
    {
        var (nextCalled, status, _) = await InvokeAsync(HttpMethods.Patch, path, "1.3.0");

        nextCalled.Should().BeFalse();
        status.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task VoucherActions_AreNotEnforcedWhenTheClientSendsNoVersion()
    {
        var (nextCalled, status, _) = await InvokeAsync(
            HttpMethods.Patch,
            $"/api/vouchers/{MarkerId}/mark-used",
            appVersion: null);

        nextCalled.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task VoucherReads_StayUnsignedAtEveryVersion()
    {
        var (nextCalled, _, _) = await InvokeAsync(HttpMethods.Get, "/api/vouchers/my", "1.3.0");

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task CancellingAPurchase_RequiresTheSignatureOnceTheGateVersionIsInstalled()
    {
        var (nextCalled, status, _) = await InvokeAsync(
            HttpMethods.Delete,
            $"/api/purchases/{MarkerId}",
            "1.3.0");

        nextCalled.Should().BeFalse();
        status.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task CancellingAPurchase_IsAllowedForABuildThatDoesNotSignItYet()
    {
        var (nextCalled, _, _) = await InvokeAsync(
            HttpMethods.Delete,
            $"/api/purchases/{MarkerId}",
            "1.2.0");

        nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/purchases/my")]
    [InlineData("/api/purchases/renew/quote")]
    [InlineData("/api/purchases/renew/config")]
    public async Task PurchaseReads_StayUnsignedAtTheGateVersion(string path)
    {
        var (nextCalled, _, _) = await InvokeAsync(HttpMethods.Get, path, "1.3.0");

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public void Defaults_NameTheRoutesTheControllersActuallyServe()
    {
        // A pattern that drifts from the controller's template silently protects nothing.
        var defaults = new DeviceAuthOptions().RequireSignatureForEndpointsFrom;

        defaults.Should().ContainKey("/api/vouchers/*/mark-used");
        defaults.Should().ContainKey("/api/vouchers/*/restore");
        defaults.Should().ContainKey("DELETE /api/purchases/*");

        // The gate has to sit above the version that is already installed, or the first deploy
        // would refuse the pump tap on every phone holding a pre-signing bundle.
        defaults.Values.Should().OnlyContain(version => AppVersionOptions.Compare(version, "1.2.0") > 0);
    }

    private async Task<(bool NextCalled, int StatusCode, HttpContext Context)> InvokeAsync(
        string method,
        string path,
        string? appVersion)
    {
        var nextCalled = false;
        var middleware = new DeviceSignatureMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<DeviceSignatureMiddleware>.Instance,
            new DeviceSignatureVerifier(new Mock<IAsymmetricSignatureVerifier>().Object));

        var options = Options.Create(new DeviceAuthOptions
        {
            Enabled = true,
            AllowDevelopmentBypass = false
        });
        var context = CreateContext(method, path, appVersion);
        var cache = new Mock<ICacheService>();

        await middleware.InvokeAsync(
            context,
            _context,
            cache.Object,
            options,
            CreateEnvironment("Production"));

        return (nextCalled, context.Response.StatusCode, context);
    }

    private static HttpContext CreateContext(string method, string path, string? appVersion)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                "TestAuth"))
        };

        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (appVersion is not null)
            context.Request.Headers[AttestationMiddleware.AppVersionHeader] = appVersion;

        return context;
    }

    private static IWebHostEnvironment CreateEnvironment(string environmentName)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        return env.Object;
    }
}
