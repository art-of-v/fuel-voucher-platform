using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Middleware;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FuelFlow.UnitTests.Middleware;

public sealed class MiddlewareTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public MiddlewareTests()
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

    [Fact]
    public async Task TryHandleAsync_ShouldReturn401_ForUnauthorizedAccessException()
    {
        var proxy = new GlobalExceptionHandlerProxy();
        var context = CreateHttpContext();

        var handled = await proxy.TryHandleAsync(context, new UnauthorizedAccessException("nope"));

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldReturn400_ForArgumentException()
    {
        var proxy = new GlobalExceptionHandlerProxy();
        var context = CreateHttpContext();

        var handled = await proxy.TryHandleAsync(context, new ArgumentException("bad argument"));

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldReturn404_ForKeyNotFoundException()
    {
        var proxy = new GlobalExceptionHandlerProxy();
        var context = CreateHttpContext();

        var handled = await proxy.TryHandleAsync(context, new KeyNotFoundException("missing"));

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldReturn500_ForGenericException()
    {
        var proxy = new GlobalExceptionHandlerProxy();
        var context = CreateHttpContext();

        var handled = await proxy.TryHandleAsync(context, new Exception("boom"));

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldWriteProblemDetailsJson()
    {
        var proxy = new GlobalExceptionHandlerProxy();
        var context = CreateHttpContext();

        await proxy.TryHandleAsync(context, new InvalidOperationException("bad state"));

        var json = ReadJsonBody(context);
        json.TryGetProperty("title", out _).Should().BeTrue();
        json.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetInt32().Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void Authorize_ShouldReturnTrue_WhenDevelopmentBypassEnabled()
    {
        var filter = new HangfireDashboardAuthorizationFilter(true);
        var context = CreateDashboardContext(new ClaimsPrincipal());

        filter.Authorize(context).Should().BeTrue();
    }

    [Fact]
    public void Authorize_ShouldReturnFalse_WithoutAuth()
    {
        var filter = new HangfireDashboardAuthorizationFilter(false);
        var context = CreateDashboardContext(new ClaimsPrincipal());

        filter.Authorize(context).Should().BeFalse();
    }

    [Fact]
    public void Authorize_ShouldReturnTrue_WhenAdminAuthenticated()
    {
        var filter = new HangfireDashboardAuthorizationFilter(false);
        var user = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "TestAuth"));
        var context = CreateDashboardContext(user);

        filter.Authorize(context).Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNext_WhenTokenVersionMatches()
    {
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId, isActive: true, tokenVersion: 3);
        var context = CreateHttpContext(AuthenticatedPrincipal(userId, "3"));

        var nextCalled = false;
        var middleware = new SessionValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<SessionValidationMiddleware>.Instance);

        await middleware.InvokeAsync(context, _context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().NotBe(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn401_WhenTokenVersionMismatch()
    {
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId, isActive: true, tokenVersion: 3);
        var context = CreateHttpContext(AuthenticatedPrincipal(userId, "9"));

        var nextCalled = false;
        var middleware = new SessionValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<SessionValidationMiddleware>.Instance);

        await middleware.InvokeAsync(context, _context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn401_WhenUserInactive()
    {
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId, isActive: false, tokenVersion: 1);
        var context = CreateHttpContext(AuthenticatedPrincipal(userId, "1"));

        var nextCalled = false;
        var middleware = new SessionValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<SessionValidationMiddleware>.Instance);

        await middleware.InvokeAsync(context, _context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNext_WhenAnonymous()
    {
        var context = CreateHttpContext(new ClaimsPrincipal());

        var nextCalled = false;
        var middleware = new SessionValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<SessionValidationMiddleware>.Instance);

        await middleware.InvokeAsync(context, _context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNext_WhenDisabled()
    {
        var nextCalled = false;
        var middleware = new DeviceSignatureMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<DeviceSignatureMiddleware>.Instance,
            new Mock<IAsymmetricSignatureVerifier>().Object);
        var options = Options.Create(new DeviceAuthOptions { Enabled = false });
        var context = CreateHttpContext();
        context.Request.Path = "/api/purchases";

        await middleware.InvokeAsync(
            context,
            _context,
            new Mock<ICacheService>().Object,
            options,
            CreateEnvironment("Production"));

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNext_WhenDevelopmentBypass()
    {
        var nextCalled = false;
        var middleware = new DeviceSignatureMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<DeviceSignatureMiddleware>.Instance,
            new Mock<IAsymmetricSignatureVerifier>().Object);
        var options = Options.Create(new DeviceAuthOptions
        {
            Enabled = true,
            AllowDevelopmentBypass = true,
            RequireSignatureForEndpoints = new List<string> { "/api/purchases" }
        });
        var context = CreateHttpContext();
        context.Request.Path = "/api/purchases";

        await middleware.InvokeAsync(
            context,
            _context,
            new Mock<ICacheService>().Object,
            options,
            CreateEnvironment("Development"));

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_ShouldRequireSignature_OnProtectedEndpoint()
    {
        var nextCalled = false;
        var middleware = new DeviceSignatureMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<DeviceSignatureMiddleware>.Instance,
            new Mock<IAsymmetricSignatureVerifier>().Object);
        var options = Options.Create(new DeviceAuthOptions
        {
            Enabled = true,
            AllowDevelopmentBypass = false,
            RequireSignatureForEndpoints = new List<string> { "/api/purchases" }
        });
        var context = CreateHttpContext();
        context.Request.Path = "/api/purchases";

        await middleware.InvokeAsync(
            context,
            _context,
            new Mock<ICacheService>().Object,
            options,
            CreateEnvironment("Production"));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void DeviceAuthOptions_DefaultScope_ShouldCoverRealCheckoutRoutes()
    {
        // Guards against scope drift: the protected list must name the
        // routes PurchaseController actually serves, not legacy aliases.
        var defaults = new DeviceAuthOptions().RequireSignatureForEndpoints;

        defaults.Should().Contain("/api/purchases");
        defaults.Should().Contain("/api/purchases/bulk");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReject_WhenDeviceNotBoundToJwtUser()
    {
        var deviceOwnerId = Guid.NewGuid();
        var jwtUserId = Guid.NewGuid();
        await SeedDeviceAsync(deviceOwnerId, "dev-bound-1", "test-key");

        var nextCalled = false;
        var middleware = new DeviceSignatureMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<DeviceSignatureMiddleware>.Instance,
            new Mock<IAsymmetricSignatureVerifier>().Object);
        var options = Options.Create(new DeviceAuthOptions
        {
            Enabled = true,
            AllowDevelopmentBypass = false,
            RequireSignatureForEndpoints = new List<string> { "/api/purchases" }
        });
        var context = CreateHttpContext(AuthenticatedPrincipal(jwtUserId, "1"));
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/purchases";
        context.Request.Headers["X-Device-Id"] = "dev-bound-1";
        context.Request.Headers["X-Signature"] = "sig";
        context.Request.Headers["X-Timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await middleware.InvokeAsync(
            context,
            _context,
            cache.Object,
            options,
            CreateEnvironment("Production"));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNext_WhenDeviceBoundToJwtUser()
    {
        var userId = Guid.NewGuid();
        await SeedDeviceAsync(userId, "dev-bound-2", "test-key");

        var nextCalled = false;
        var verifier = new Mock<IAsymmetricSignatureVerifier>();
        verifier.Setup(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        var middleware = new DeviceSignatureMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<DeviceSignatureMiddleware>.Instance,
            verifier.Object);
        var options = Options.Create(new DeviceAuthOptions
        {
            Enabled = true,
            AllowDevelopmentBypass = false,
            RequireSignatureForEndpoints = new List<string> { "/api/purchases" }
        });
        var context = CreateHttpContext(AuthenticatedPrincipal(userId, "1"));
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/purchases";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"x\":1}"));
        context.Request.Headers["X-Device-Id"] = "dev-bound-2";
        context.Request.Headers["X-Signature"] = "sig";
        context.Request.Headers["X-Timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await middleware.InvokeAsync(
            context,
            _context,
            cache.Object,
            options,
            CreateEnvironment("Production"));

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    private async Task SeedUserAsync(Guid userId, bool isActive, int tokenVersion)
    {
        _context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = "+380990000001",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsActive = isActive,
            TokenVersion = tokenVersion
        });
        await _context.SaveChangesAsync();
    }

    private async Task SeedDeviceAsync(Guid userId, string deviceId, string publicKey)
    {
        _context.Devices.Add(new Device
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeviceId = deviceId,
            PublicKey = publicKey,
            Status = DeviceStatus.Active,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    private static DefaultHttpContext CreateHttpContext(ClaimsPrincipal? user = null)
    {
        var context = new DefaultHttpContext { User = user ?? new ClaimsPrincipal() };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(Guid userId, string tokenVersion)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("token_version", tokenVersion)
            },
            "TestAuth");

        return new ClaimsPrincipal(identity);
    }

    private static DashboardContext CreateDashboardContext(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        httpContext.RequestServices = new ServiceCollection().BuildServiceProvider();
        return new AspNetCoreDashboardContext(new Mock<JobStorage>().Object, new DashboardOptions(), httpContext);
    }

    private static IWebHostEnvironment CreateEnvironment(string environmentName)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        return env.Object;
    }

    private static ILogger CreateNullLogger(Type category)
    {
        var nullLoggerType = typeof(NullLogger<>).MakeGenericType(category);
        var instance = nullLoggerType
            .GetField("Instance", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null);

        return (ILogger)instance!;
    }

    private static JsonElement ReadJsonBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(context.Response.Body);
        return document.RootElement.Clone();
    }

    private sealed class GlobalExceptionHandlerProxy
    {
        private static readonly Type HandlerType =
            typeof(ApplicationDbContext).Assembly.GetType("FuelFlow.Middleware.GlobalExceptionHandler", true)!;

        private readonly object _instance;

        public GlobalExceptionHandlerProxy()
        {
            var ctor = HandlerType.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single();
            _instance = ctor.Invoke([CreateNullLogger(HandlerType)])!;
        }

        public async Task<bool> TryHandleAsync(HttpContext context, Exception exception)
        {
            var method = HandlerType.GetMethod("TryHandleAsync", BindingFlags.Public | BindingFlags.Instance)!;
            var result = (ValueTask<bool>)method.Invoke(
                _instance,
                [context, exception, CancellationToken.None])!;

            return await result;
        }
    }
}
