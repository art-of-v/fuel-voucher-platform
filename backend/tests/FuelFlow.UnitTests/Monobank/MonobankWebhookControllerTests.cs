using System.Text;
using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Monobank;

/// <summary>
/// Two merchants means two webhook-signing keys, and the header cannot tell them apart: X-Key-Id
/// identifies which of ONE merchant's rotated keys signed the call, not which merchant. So a
/// callback is verified against each merchant's own key in turn (ECDSA matches exactly one of them).
///
/// The handler is sealed and deliberately so, so these tests run it for real against an in-memory
/// database and discriminate on the signature gate: a request whose signature verifies gets past
/// authentication and fails later (no such order exists), a request that verifies against nothing
/// is rejected with 401 before the handler ever runs.
/// </summary>
public class MonobankWebhookControllerTests : IDisposable
{
    private const string LiveKey = "live-public-key";
    private const string SandboxKey = "sandbox-public-key";
    private const string RotatedKey = "rotated-live-key";
    private const string Signature = "c2lnbmF0dXJl";

    private static readonly string Payload =
        "{ \"invoiceId\": \"INV-UNKNOWN\", \"status\": \"success\", \"amount\": 52000, " +
        "\"createdDate\": \"2026-01-01T10:00:00Z\", \"modifiedDate\": \"2026-01-01T10:05:00Z\" }";

    private readonly ApplicationDbContext _context;
    private readonly Mock<IAsymmetricSignatureVerifier> _verifier = new();

    public MonobankWebhookControllerTests()
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Webhook_SignedByLiveMerchant_IsAccepted()
    {
        _verifier.Setup(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), LiveKey)).Returns(true);

        var result = await Post(CreateController());

        result.Should().NotBeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Webhook_LiveFailsSandboxSucceeds_IsAccepted()
    {
        // The whole reason the sandbox key is checked: a QA order's callback is signed by the
        // other merchant and would be dropped as an invalid signature otherwise.
        _verifier.Setup(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), LiveKey)).Returns(false);
        _verifier.Setup(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), SandboxKey)).Returns(true);

        var result = await Post(CreateController());

        result.Should().NotBeOfType<UnauthorizedObjectResult>();
        _verifier.Verify(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), SandboxKey), Times.Once);
    }

    [Fact]
    public async Task Webhook_SignedByNeitherMerchant_IsRejected()
    {
        var result = await Post(CreateController());

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Webhook_MissingSignature_IsRejected()
    {
        _verifier.Setup(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var result = await Post(CreateController(), sendSignature: false);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _verifier.Verify(
            v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "an unsigned callback must be rejected before any key is consulted");
    }

    [Fact]
    public async Task Webhook_KeyIdForARotatedLiveKey_VerifiesAgainstThatKey()
    {
        _verifier.Setup(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), RotatedKey)).Returns(true);

        var result = await Post(CreateController(), keyId: "k2");

        result.Should().NotBeOfType<UnauthorizedObjectResult>();
        _verifier.Verify(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), RotatedKey), Times.Once);
    }

    [Fact]
    public async Task Webhook_UnknownKeyId_FallsBackToTheDefaultLiveKey()
    {
        _verifier.Setup(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), LiveKey)).Returns(true);

        var result = await Post(CreateController(), keyId: "k-unknown");

        result.Should().NotBeOfType<UnauthorizedObjectResult>();
        _verifier.Verify(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), LiveKey), Times.Once);
    }

    [Fact]
    public async Task Webhook_SandboxKeyNotConfigured_SandboxSignedCallbackIsRejected()
    {
        // Fail closed: with no sandbox key configured, only the live merchant's callbacks pass.
        // Silently accepting them would mean accepting an unverifiable signature.
        _verifier.Setup(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var result = await Post(CreateController(sandboxPublicKey: string.Empty));

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _verifier.Verify(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    private MonobankWebhookController CreateController(string sandboxPublicKey = SandboxKey)
    {
        var handler = new ProcessMonobankWebhookCommandHandler(
            _context,
            NullLogger<ProcessMonobankWebhookCommandHandler>.Instance,
            new Mock<Hangfire.IBackgroundJobClient>().Object,
            new Mock<RefundStatusSyncService>(null!, null!, null!).Object,
            new FuelFlowMetrics(),
            NotificationDispatcher.Disabled);

        var options = new MonobankOptions
        {
            Enabled = true,
            PublicKey = LiveKey,
            SandboxPublicKey = sandboxPublicKey,
            PublicKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["k2"] = RotatedKey
            }
        };

        return new MonobankWebhookController(
            handler,
            _verifier.Object,
            Options.Create(options),
            NullLogger<MonobankWebhookController>.Instance);
    }

    private static Task<IActionResult> Post(
        MonobankWebhookController controller,
        bool sendSignature = true,
        string? keyId = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Payload));

        if (sendSignature)
        {
            httpContext.Request.Headers["X-Sign"] = Signature;
        }

        if (keyId is not null)
        {
            httpContext.Request.Headers["X-Key-Id"] = keyId;
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller.ProcessWebhook(CancellationToken.None);
    }
}