using FluentAssertions;
using FuelFlow.Features.Feedback.GetAdminFeedback;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Feedback;

/// <summary>
/// The feedback backlog's list: which messages, and which page of them.
/// </summary>
/// <remarks>
/// The list opens on the kept messages, because the whole point of the triage step is that the
/// team reads fewer, better rows. Every filter narrows, and an unrecognised one does not widen:
/// a crafted query string must not turn a narrow request into a dump of the table. The rules used
/// to sit in the controller, where no unit test could reach them.
/// </remarks>
public sealed class GetAdminFeedbackQueryHandlerTests : IDisposable
{
    private long _nextChatId = 7_000_000_000;
    private long _nextMessageId = 1;

    private readonly ApplicationDbContext _context;
    private readonly GetAdminFeedbackQueryHandler _handler;

    public GetAdminFeedbackQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = new GetAdminFeedbackQueryHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task The_list_opens_on_the_kept_messages()
    {
        await SeedAsync(FeedbackVerdict.Kept, "оплата не проходить");
        await SeedAsync(FeedbackVerdict.Junk, "йоук");
        await SeedAsync(null, "ще не класифіковано");

        var page = await _handler.HandleAsync(new GetAdminFeedbackQuery(), CancellationToken.None);

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Text.Should().Be("оплата не проходить");
    }

    [Fact]
    public async Task An_unrecognised_verdict_falls_back_to_kept_rather_than_dumping_the_table()
    {
        await SeedAsync(FeedbackVerdict.Kept, "реальна скарга");
        await SeedAsync(FeedbackVerdict.Junk, "привіт");

        var page = await _handler.HandleAsync(
            new GetAdminFeedbackQuery(Verdict: "nonsense"),
            CancellationToken.None);

        page.Items.Should().ContainSingle().Which.Text.Should().Be("реальна скарга");
    }

    [Fact]
    public async Task Junk_is_reachable_through_an_explicit_verdict_filter()
    {
        await SeedAsync(FeedbackVerdict.Kept, "хочу фільтр");
        await SeedAsync(FeedbackVerdict.Junk, "привіт");

        var page = await _handler.HandleAsync(
            new GetAdminFeedbackQuery(Verdict: nameof(FeedbackVerdict.Junk)),
            CancellationToken.None);

        page.Items.Should().ContainSingle().Which.Text.Should().Be("привіт");
    }

    [Fact]
    public async Task The_drift_filter_surfaces_junk_the_classifier_was_not_sure_about()
    {
        await SeedAsync(FeedbackVerdict.Junk, "йоук", confidence: 0.2m);
        await SeedAsync(FeedbackVerdict.Junk, "привіт", confidence: 0.99m);

        var page = await _handler.HandleAsync(
            new GetAdminFeedbackQuery(Verdict: nameof(FeedbackVerdict.Junk), MaxConfidence: 0.6m),
            CancellationToken.None);

        page.Items.Should().ContainSingle().Which.Text.Should().Be("йоук");
    }

    [Fact]
    public async Task The_reply_count_is_the_number_of_answers_the_message_got()
    {
        var id = await SeedAsync(FeedbackVerdict.Kept, "скарга");

        _context.FeedbackMessageReplies.Add(new FeedbackMessageReply
        {
            Id = Guid.CreateVersion7(),
            FeedbackMessageId = id,
            StaffUserId = 1,
            Text = "додамо",
            SentAtUtc = TestClock.UtcNow
        });
        await _context.SaveChangesAsync();

        var page = await _handler.HandleAsync(new GetAdminFeedbackQuery(), CancellationToken.None);

        page.Items.Should().ContainSingle().Which.ReplyCount.Should().Be(1);
    }

    private async Task<Guid> SeedAsync(FeedbackVerdict? verdict, string text, decimal confidence = 0.9m)
    {
        var message = new FeedbackMessage
        {
            Id = Guid.CreateVersion7(),
            TelegramChatId = Interlocked.Increment(ref _nextChatId),
            TelegramMessageId = Interlocked.Increment(ref _nextMessageId),
            Text = text,
            Verdict = verdict,
            Severity = FeedbackSeverity.Low,
            Confidence = Math.Round(confidence, 2),
            Language = FeedbackLanguage.Uk,
            Status = FeedbackStatus.New,
            CreatedAtUtc = TestClock.UtcNow
        };

        _context.FeedbackMessages.Add(message);
        await _context.SaveChangesAsync();

        return message.Id;
    }
}
