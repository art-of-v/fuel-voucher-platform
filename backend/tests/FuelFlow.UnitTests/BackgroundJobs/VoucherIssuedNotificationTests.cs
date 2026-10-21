using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Notifications.Push;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.BackgroundJobs;

/// <summary>
/// #150 W3 - the worker half of a company issue. A worker is not the buyer and has no order screen
/// of their own, so the notification they get has to speak about the company rather than about an
/// order, and a tapped push has to land in that company's worker context.
/// </summary>
public sealed class VoucherIssuedNotificationTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IExpoPushSender> _pushSender = new();
    private readonly NotificationService _service;

    public VoucherIssuedNotificationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options, TestQrCipher.Instance);

        _pushSender
            .Setup(x => x.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExpoPushResult>());

        _service = new NotificationService(
            _context,
            _pushSender.Object,
            new Mock<ILogger<NotificationService>>().Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static OutboxEvent CreateEvent(
        Guid workerUserId,
        string? companyName,
        int voucherCount,
        string? legalEntityId,
        bool processed = false)
        => new()
        {
            EventType = OutboxEventType.VoucherIssued,
            // camelCase anonymous object, mirroring the producer in GiftVouchersCommand.
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                workerUserId = workerUserId.ToString(),
                companyName,
                voucherCount,
                legalEntityId,
            }),
            Processed = processed,
            CreatedAtUtc = DateTime.UtcNow - OutboxDispatchJob.SafetyNetClaimWindow - TimeSpan.FromMinutes(1),
        };

    private void Seed(OutboxEvent evt) => _context.OutboxEvents.Add(evt);

    [Fact]
    public async Task Creates_A_Notification_For_The_Worker_And_Marks_The_Event_Processed()
    {
        var workerId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        Seed(CreateEvent(workerId, "ACME LLC", 1, companyId.ToString()));
        await _context.SaveChangesAsync();

        await _service.ProcessVoucherIssuedEventsAsync();

        var notification = await _context.Notifications.SingleAsync();
        notification.UserId.Should().Be(workerId);
        notification.IsRead.Should().BeFalse();
        notification.Title.Should().Be("Вам видано паливо");
        notification.Message.Should().Contain("ACME LLC");

        (await _context.OutboxEvents.SingleAsync()).Processed.Should().BeTrue();
    }

    /// <summary>
    /// The copy is worker-context copy. It must not name an order: the order belongs to the
    /// employer and the worker never sees it, so pointing at one is a dead end.
    /// </summary>
    [Fact]
    public async Task The_Copy_Does_Not_Talk_About_An_Order()
    {
        var workerId = Guid.NewGuid();
        Seed(CreateEvent(workerId, "ACME LLC", 2, Guid.NewGuid().ToString()));
        await _context.SaveChangesAsync();

        await _service.ProcessVoucherIssuedEventsAsync();

        var notification = await _context.Notifications.SingleAsync();
        notification.Message.Should().NotContain("замовлен");
        notification.Message.Should().Contain("2 талони");
    }

    /// <summary>Ukrainian plurals: 1 талон, 2-4 талони, 5+ талонів, and the teens are the trap.</summary>
    [Theory]
    [InlineData(1, "1 талон")]
    [InlineData(2, "2 талони")]
    [InlineData(4, "4 талони")]
    [InlineData(5, "5 талонів")]
    [InlineData(11, "11 талонів")]
    [InlineData(21, "21 талон")]
    [InlineData(22, "22 талони")]
    public async Task Pluralises_The_Count(int count, string expected)
    {
        var workerId = Guid.NewGuid();
        Seed(CreateEvent(workerId, "ACME LLC", count, Guid.NewGuid().ToString()));
        await _context.SaveChangesAsync();

        await _service.ProcessVoucherIssuedEventsAsync();

        (await _context.Notifications.SingleAsync()).Message.Should().Contain(expected);
    }

    /// <summary>
    /// The push carries the company so the tap can open that company's worker context. Without it a
    /// worker who belongs to several companies (W1) would land in whichever one was already open.
    /// </summary>
    [Fact]
    public async Task Tags_The_Push_With_The_Company_For_The_Worker_Context_Deep_Link()
    {
        var workerId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var token = "ExponentPushToken[worker]";
        _context.PushTokens.Add(new UserPushToken
        {
            Id = Guid.NewGuid(),
            UserId = workerId,
            Token = token,
            Platform = "android",
            DeviceId = null,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow - OutboxDispatchJob.SafetyNetClaimWindow - TimeSpan.FromMinutes(1),
            UpdatedAtUtc = DateTime.UtcNow,
        });
        Seed(CreateEvent(workerId, "ACME LLC", 1, companyId.ToString()));
        await _context.SaveChangesAsync();

        IReadOnlyList<ExpoPushMessage>? sent = null;
        _pushSender
            .Setup(x => x.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ExpoPushMessage>, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(new List<ExpoPushResult>());

        await _service.ProcessVoucherIssuedEventsAsync();

        sent.Should().NotBeNull("the worker has a device, so the push must carry the company id");
        var data = sent!.First().Data!;
        data["type"].Should().Be("voucher_issued");
        data["legalEntityId"].Should().Be(companyId.ToString());
    }

    /// <summary>
    /// The in-app row has to carry the company too, and it has to say what it is about.
    /// <para>
    /// The push is the fast path and it can be missing: no notification permission, no APNs token,
    /// a device that was reinstalled. The row in the bell is the fallback, and a fallback that
    /// cannot say which company the fuel came from opens the wrong wallet - a worker in two
    /// companies would read one company's fuel as another's.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_Row_Itself_Carries_The_Type_And_The_Company()
    {
        var workerId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        Seed(CreateEvent(workerId, "ACME LLC", 2, companyId.ToString()));
        await _context.SaveChangesAsync();

        await _service.ProcessVoucherIssuedEventsAsync();

        var notification = await _context.Notifications.SingleAsync();
        notification.Type.Should().Be(NotificationTypes.VoucherIssued);
        NotificationPayload.ReadId(notification.Payload, "legalEntityId").Should().Be(companyId.ToString());
    }

    /// <summary>
    /// A row with no company cannot be routed to a worker context, and the worker may work for
    /// several companies (W1) - so the row must not pretend. The type is still right: the fuel WAS
    /// issued, the copy still names the company, and the client falls back to the notifications
    /// list rather than opening the wrong one.
    /// </summary>
    [Fact]
    public async Task A_Payload_Without_A_Company_Still_Carries_The_Type()
    {
        var workerId = Guid.NewGuid();
        Seed(CreateEvent(workerId, "ACME LLC", 1, legalEntityId: null));
        await _context.SaveChangesAsync();

        await _service.ProcessVoucherIssuedEventsAsync();

        var notification = await _context.Notifications.SingleAsync();
        notification.Type.Should().Be(NotificationTypes.VoucherIssued);
        NotificationPayload.ReadId(notification.Payload, "legalEntityId").Should().BeNull();
    }

    [Fact]
    public async Task An_Invalid_Payload_Is_Marked_Processed_Without_Creating_Anything()
    {
        _context.OutboxEvents.Add(new OutboxEvent
        {
            EventType = OutboxEventType.VoucherIssued,
            Payload = """{"workerUserId":"not-a-guid"}""",
            Processed = false,
            CreatedAtUtc = DateTime.UtcNow - OutboxDispatchJob.SafetyNetClaimWindow - TimeSpan.FromMinutes(1),
        });
        await _context.SaveChangesAsync();

        await _service.ProcessVoucherIssuedEventsAsync();

        (await _context.Notifications.CountAsync()).Should().Be(0);
        (await _context.OutboxEvents.SingleAsync()).Processed.Should().BeTrue();
    }

    [Fact]
    public async Task An_Already_Processed_Event_Is_Not_Handled_Twice()
    {
        var workerId = Guid.NewGuid();
        Seed(CreateEvent(workerId, "ACME LLC", 1, Guid.NewGuid().ToString(), processed: true));
        await _context.SaveChangesAsync();

        await _service.ProcessVoucherIssuedEventsAsync();

        (await _context.Notifications.CountAsync()).Should().Be(0);
    }
}
