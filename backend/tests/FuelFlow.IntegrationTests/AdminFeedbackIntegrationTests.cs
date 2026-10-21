using System.Security.Claims;
using FluentAssertions;
using FuelFlow.Features.Feedback;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The admin view of the feedback backlog: what the triage step is FOR. If the default list
/// showed "йоук" as prominently as a complaint, the classifier would be doing nothing for the
/// person reading it - so the defaults and the filters are the behaviour under test here.
/// <para>
/// Every assertion is scoped to the rows this class seeded. The list endpoint is global by
/// design (an admin filtering the backlog should see all of it), so an absolute count here would
/// assert on whatever the pipeline tests happened to leave behind.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class AdminFeedbackIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private static long _nextChatId = 7_000_000_000;
    private static long _nextMessageId = 1;

    private readonly TestDatabaseFixture _fixture;

    public AdminFeedbackIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The per-class clone the fixture made, not the container's default database.
    /// <para>
    /// The fixture now gives every class its own database cloned from a frozen template, and the
    /// template itself refuses connections (`datallowconn = false`) so a stray session cannot
    /// disturb a clone. Connecting to <c>DbContainer.GetConnectionString()</c> therefore fails
    /// with "database ... is not currently accepting connections" - a message that reads like a
    /// crash and is really this one wrong line.
    /// </para>
    /// </summary>
    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task TheListOpensOnKeptMessagesRatherThanOnWhateverArrived()
    {
        var kept = await Seed(FeedbackVerdict.Kept, FeedbackCategory.Bug, "оплата не проходить");
        var junk = await Seed(FeedbackVerdict.Junk, category: null, "йоук");
        var unclassified = await Seed(verdict: null, category: null, "ще не класифіковано");

        var body = await ReadListAsync(c => c.GetAll());

        // The point of triage is that the team reads fewer, better rows: with no verdict filter the
        // list is the kept ones, not everything that arrived.
        Mine(body, junk, unclassified).Should().BeEmpty();
        Mine(body, kept).Should().ContainSingle()
            .Which.Text.Should().Be("оплата не проходить");
    }

    [Fact]
    public async Task JunkIsReachableThroughAnExplicitVerdictFilter()
    {
        var kept = await Seed(FeedbackVerdict.Kept, FeedbackCategory.Idea, "хочу фільтр");
        var junk = await Seed(FeedbackVerdict.Junk, category: null, "привіт");

        var withoutFilter = await ReadListAsync(c => c.GetAll());
        var junkOnly = await ReadListAsync(c => c.GetAll(verdict: nameof(FeedbackVerdict.Junk)));

        Mine(withoutFilter, junk).Should().BeEmpty();
        Mine(junkOnly, junk).Should().ContainSingle().Which.Text.Should().Be("привіт");
        Mine(junkOnly, kept).Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnrecognisedVerdictFallsBackToKeptRatherThanDumpingTheTable()
    {
        var kept = await Seed(FeedbackVerdict.Kept, FeedbackCategory.Bug, "реальна скарга");
        var junk = await Seed(FeedbackVerdict.Junk, category: null, "привіт");

        // A crafted query string must not widen the filter to "no filter".
        var body = await ReadListAsync(c => c.GetAll(verdict: "nonsense"));

        Mine(body, kept).Should().ContainSingle();
        Mine(body, junk).Should().BeEmpty();
    }

    [Fact]
    public async Task TheDriftFilterSurfacesJunkTheClassifierWasNotSureAbout()
    {
        await Seed(FeedbackVerdict.Kept, FeedbackCategory.Complaint, "впевнена скарга");
        var unsure = await Seed(FeedbackVerdict.Junk, FeedbackCategory.Complaint, "йоук", confidence: 0.2);
        var sure = await Seed(FeedbackVerdict.Junk, category: null, "привіт", confidence: 0.99);

        // The early warning that the classifier has started eating real complaints. It is not
        // visible anywhere else, because a dropped message looks exactly like a dropped message.
        var body = await ReadListAsync(c => c.GetAll(
            verdict: nameof(FeedbackVerdict.Junk),
            maxConfidence: 0.6m));

        Mine(body, unsure).Should().ContainSingle().Which.Text.Should().Be("йоук");
        Mine(body, sure).Should().BeEmpty();
    }

    [Fact]
    public async Task CategoryAndStatusFiltersNarrowTheList()
    {
        var bug = await Seed(FeedbackVerdict.Kept, FeedbackCategory.Bug, "зламалося");
        var praise = await Seed(FeedbackVerdict.Kept, FeedbackCategory.Praise, "дякую");

        var byCategory = await ReadListAsync(c => c.GetAll(category: nameof(FeedbackCategory.Bug)));
        var byStatus = await ReadListAsync(c => c.GetAll(status: nameof(FeedbackStatus.Handled)));

        Mine(byCategory, bug).Should().ContainSingle();
        Mine(byCategory, praise).Should().BeEmpty();
        Mine(byStatus, bug, praise).Should().BeEmpty("nothing has been marked handled");
    }

    [Fact]
    public async Task SearchLooksInBothTheMessageAndTheClassifierSummary()
    {
        var id = await Seed(
            FeedbackVerdict.Kept,
            FeedbackCategory.Bug,
            "щось довге",
            summary: "клієнт не може заплатити карткою");

        var bySummary = await ReadListAsync(c => c.GetAll(search: "заплатити"));
        var byText = await ReadListAsync(c => c.GetAll(search: "довге"));
        var byNothing = await ReadListAsync(c => c.GetAll(search: "такого немає"));

        Mine(bySummary, id).Should().ContainSingle();
        Mine(byText, id).Should().ContainSingle();
        Mine(byNothing, id).Should().BeEmpty();
    }

    [Fact]
    public async Task TheDetailViewReturnsTheMessageWithItsAnswers()
    {
        var id = await Seed(FeedbackVerdict.Kept, FeedbackCategory.Idea, "хочу фільтр");

        await using (var context = CreateContext())
        {
            context.FeedbackMessageReplies.Add(new FeedbackMessageReply
            {
                Id = Guid.CreateVersion7(),
                FeedbackMessageId = id,
                StaffUserId = 555,
                StaffUserName = "Artem",
                Text = "додамо",
                DeliveredMessageId = 42,
                SentAtUtc = TestClock.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var body = ReadBody<FeedbackDetailResponse>(
            await NewController(verify).GetById(id, default));

        body.Item.Id.Should().Be(id);
        body.Replies.Should().ContainSingle().Which.Text.Should().Be("додамо");
    }

    [Fact]
    public async Task AMissingMessageIsANotFoundRatherThanAnEmptyShell()
    {
        await using var context = CreateContext();

        var result = await NewController(context).GetById(Guid.CreateVersion7(), default);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task MarkingHandledIsIdempotentAndLeavesAlreadyHandledRowsAlone()
    {
        var id = await Seed(FeedbackVerdict.Kept, FeedbackCategory.Bug, "скарга");

        await using (var context = CreateContext())
        {
            var first = await NewController(context).UpdateStatus(
                new UpdateFeedbackStatusRequest { Ids = [id], Status = nameof(FeedbackStatus.Handled) },
                default);

            ReadBody<FeedbackMutationResponse>(first).Affected.Should().Be(1);
        }

        await using (var verify = CreateContext())
        {
            var second = await NewController(verify).UpdateStatus(
                new UpdateFeedbackStatusRequest { Ids = [id], Status = nameof(FeedbackStatus.Handled) },
                default);

            // A double-click is not a second decision, and the audit log records decisions.
            ReadBody<FeedbackMutationResponse>(second).Affected.Should().Be(0);
        }

        (await StatusOfAsync(id)).Should().Be(FeedbackStatus.Handled);
    }

    [Fact]
    public async Task AnUnknownStatusIsRejectedRatherThanStored()
    {
        var id = await Seed(FeedbackVerdict.Kept, FeedbackCategory.Bug, "скарга");

        await using var context = CreateContext();
        var result = await NewController(context).UpdateStatus(
            new UpdateFeedbackStatusRequest { Ids = [id], Status = "Archived" },
            default);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await StatusOfAsync(id)).Should().Be(FeedbackStatus.New);
    }

    [Fact]
    public async Task DeletingOneMessageAlsoRemovesItsReplies()
    {
        // The FK is Restrict, so a delete that left replies behind would fail the request.
        var junk = await Seed(FeedbackVerdict.Junk, category: null, "привіт");
        var kept = await Seed(FeedbackVerdict.Kept, FeedbackCategory.Idea, "ідея");

        await using (var context = CreateContext())
        {
            context.FeedbackMessageReplies.Add(new FeedbackMessageReply
            {
                Id = Guid.CreateVersion7(),
                FeedbackMessageId = junk,
                StaffUserId = 1,
                Text = "відповідь",
                SentAtUtc = TestClock.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var verify = CreateContext())
        {
            var result = await NewController(verify).Delete(id: junk, ct: default);
            ReadBody<FeedbackMutationResponse>(result).Affected.Should().Be(1);
        }

        await using var check = CreateContext();
        (await check.FeedbackMessages.AnyAsync(m => m.Id == junk)).Should().BeFalse();
        (await check.FeedbackMessageReplies.AnyAsync(r => r.FeedbackMessageId == junk)).Should().BeFalse();
        (await check.FeedbackMessages.AnyAsync(m => m.Id == kept)).Should().BeTrue(
            "deleting one row must not reach into the rest of the backlog");
    }

    [Fact]
    public async Task DeleteWithoutAScopeIsRefused()
    {
        await using var context = CreateContext();

        var result = await NewController(context).Delete(default, default);

        result.Should().BeOfType<BadRequestObjectResult>(
            because: "an unscoped delete would empty the table on a mistyped query string");
    }

    [Fact]
    public async Task FacetsReportTheTriageSplit()
    {
        await using var context = CreateContext();
        var before = ReadBody<FeedbackFacetsResponse>(await NewController(context).GetFacets(default));

        await Seed(FeedbackVerdict.Kept, FeedbackCategory.Bug, "а");
        await Seed(FeedbackVerdict.Kept, FeedbackCategory.Idea, "б");
        await Seed(FeedbackVerdict.Junk, category: null, "в");

        await using var verify = CreateContext();
        var after = ReadBody<FeedbackFacetsResponse>(await NewController(verify).GetFacets(default));

        after.Total.Should().Be(before.Total + 3);
        after.Kept.Should().Be(before.Kept + 2);
        after.Junk.Should().Be(before.Junk + 1);
        after.Categories.Should().Contain([FeedbackCategory.Bug, FeedbackCategory.Idea]);
        after.Statuses.Should().Contain(FeedbackStatus.New);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The subset of a list response that belongs to this test.</summary>
    private static List<AdminFeedbackDto> Mine(FeedbackListResponse body, params Guid[] ids) =>
        body.Items.Where(i => ids.Contains(i.Id)).ToList();

    /// <summary>The staff member every test acts as. The status endpoint stamps who made the
    /// decision into the audit log, so a controller with no principal would 401.</summary>
    private static readonly Guid ActingStaffId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private AdminFeedbackController NewController(ApplicationDbContext context)
    {
        var controller = new AdminFeedbackController(
            context,
            new FuelFlow.Features.Brands.AuditEventService(context));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, ActingStaffId.ToString()),
                        new Claim(ClaimTypes.Name, "Test Staff")
                    ],
                    "test"))
            }
        };

        return controller;
    }

    private async Task<Guid> Seed(
        FeedbackVerdict? verdict,
        FeedbackCategory? category,
        string text,
        double confidence = 0.9,
        string? summary = null)
    {
        var message = new FeedbackMessage
        {
            Id = Guid.CreateVersion7(),
            TelegramChatId = Interlocked.Increment(ref _nextChatId),
            TelegramMessageId = Interlocked.Increment(ref _nextMessageId),
            Text = text,
            Verdict = verdict,
            Category = category,
            Severity = FeedbackSeverity.Low,
            Confidence = Math.Round((decimal)confidence, 2),
            Language = FeedbackLanguage.Uk,
            Summary = summary,
            Status = FeedbackStatus.New,
            CreatedAtUtc = TestClock.UtcNow
        };

        await using var context = CreateContext();
        context.FeedbackMessages.Add(message);
        await context.SaveChangesAsync();

        return message.Id;
    }

    /// <summary>Calls the controller and unwraps the list response. A named record rather than an
    /// anonymous object, so a renamed field fails here instead of quietly yielding null.</summary>
    private async Task<FeedbackListResponse> ReadListAsync(
        Func<AdminFeedbackController, Task<IActionResult>> call)
    {
        await using var context = CreateContext();
        return ReadBody<FeedbackListResponse>(await call(NewController(context)));
    }

    private static T ReadBody<T>(IActionResult result)
    {
        result.Should().BeOfType<OkObjectResult>();
        return (T)((OkObjectResult)result).Value!;
    }

    private async Task<FeedbackStatus> StatusOfAsync(Guid id)
    {
        await using var context = CreateContext();
        return await context.FeedbackMessages
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => m.Status)
            .SingleAsync();
    }
}