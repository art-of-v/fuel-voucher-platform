using System.Net;
using System.Security.Claims;
using FluentAssertions;
using FuelFlow.Features.Feedback;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The feedback pipeline end to end against real Postgres: Telegram update in, stored row,
/// acknowledgement out, triage to the staff group, and a staff answer routed back to the customer
/// - plus the two gates that decide who is allowed to send that answer.
/// </summary>
[Collection("Integration Tests")]
public sealed class FeedbackBotIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    // Fresh chat ids per test: the unique index on (chat, message) makes a shared chat id collide
    // between tests running against the same database, which shows up as a phantom duplicate.
    private static long _nextChatId = 9_000_000;

    private readonly long CustomerChatId = Interlocked.Increment(ref _nextChatId);
    private readonly long OtherCustomerChatId = Interlocked.Increment(ref _nextChatId) + 500_000;

    private static int _nextPhoneSuffix;

    private readonly TestDatabaseFixture _fixture;

    public FeedbackBotIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

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

    // ---------------------------------------------------------------- intake

    [Fact]
    public async Task ACustomerMessageIsStoredAcknowledgedAndQueuedForTriage()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        Guid storedId;
        await using (var context = CreateContext())
        {
            await FeedbackTestFactory.Router(context, client, jobs)
                .HandleAsync(FeedbackTestFactory.CustomerMessage(CustomerChatId, 101, "Не працює оплата"));
            storedId = await context.FeedbackMessages
                .Where(m => m.TelegramChatId == CustomerChatId)
                .Select(m => m.Id)
                .SingleAsync();
        }

        var row = await ReadOne(storedId);
        row.Text.Should().Be("Не працює оплата");
        row.TelegramMessageId.Should().Be(101);
        row.Status.Should().Be(FeedbackStatus.New);
        row.Verdict.Should().BeNull("classification happens in the background job, not in the webhook");

        // The customer hears back immediately, which is the promise that keeps them from writing twice.
        client.SentTo(CustomerChatId).Should().ContainSingle()
            .Which.Should().Be(FeedbackBotTexts.Acknowledge);

        jobs.TriagedMessageIds.Should().ContainSingle().Which.Should().Be(storedId);
    }

    [Fact]
    public async Task ARedeliveredUpdateIsStoredOnceAndStillAcknowledged()
    {
        // Telegram re-sends an update whenever it does not get a 2xx, so this is the normal case,
        // not an edge case - without the unique index one complaint becomes two.
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var update = FeedbackTestFactory.CustomerMessage(CustomerChatId, 102, "Дубль");

        await using (var context = CreateContext())
        {
            var router = FeedbackTestFactory.Router(context, client, jobs);
            await router.HandleAsync(update);
            await router.HandleAsync(update);
        }

        await using var verify = CreateContext();
        (await verify.FeedbackMessages.CountAsync(m => m.TelegramMessageId == 102))            .Should().Be(1);

        // Acked both times: the customer asked twice, and silence is the failure mode that
        // produces a third message.
        client.SentTo(CustomerChatId).Should().HaveCount(2);
        jobs.TriagedMessageIds.Should().ContainSingle("the redelivery must not queue a second triage");
    }

    [Fact]
    public async Task AGroupMessageOutsideTheStaffChatIsIgnoredEntirely()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        await using var context = CreateContext();
        await FeedbackTestFactory.Router(context, client, jobs).HandleAsync(
            TelegramUpdateFactory(supergroup: true, chatId: -100999, text: "привіт всім у чаті"));

        (await CountForChatAsync(-100999)).Should().Be(0, "a bot added to some group must not collect that group's conversation");
        client.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task AnOverlongMessageIsRejectedBeforeItIsStored()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var options = FeedbackTestFactory.Options(o => o.MaxMessageLength = 50);

        await using var context = CreateContext();
        await FeedbackTestFactory.Router(context, client, jobs, options).HandleAsync(
            FeedbackTestFactory.CustomerMessage(CustomerChatId, 103, new string('я', 200)));

        (await CountForMessageAsync(103)).Should().Be(0, "an over-long paste is never stored");        client.SentTo(CustomerChatId).Should().ContainSingle().Which.Should().Be(FeedbackBotTexts.TooLong);
    }

    [Fact]
    public async Task AChatOverTheHourlyCeilingIsToldSoRatherThanSilenced()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var options = FeedbackTestFactory.Options(o => o.MinMessagesPerHour = 2);

        await using var context = CreateContext();
        var router = FeedbackTestFactory.Router(context, client, jobs, options);
        for (var i = 0; i < 4; i++)
            await router.HandleAsync(FeedbackTestFactory.CustomerMessage(CustomerChatId, (long)(200 + i), $"msg {i}"));

        (await CountForMessageRangeAsync(200, 203)).Should().Be(2);
        client.SentTo(CustomerChatId).Should().Contain(FeedbackBotTexts.RateLimited);
    }

    [Fact]
    public async Task AChatThatIsTurnedOffIsToldToUseTheWebFormInstead()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var options = FeedbackTestFactory.Options(o => o.Enabled = false);

        await using var context = CreateContext();
        await FeedbackTestFactory.Router(context, client, jobs, options).HandleAsync(
            FeedbackTestFactory.CustomerMessage(CustomerChatId, 210, "оплата не працює"));

        // Nothing stored and no work queued, but the person who is already inside the chat is
        // told where to go rather than left talking to a bot that has gone quiet.
        (await CountForMessageAsync(210)).Should().Be(0, "nothing is stored while the feature is off");        client.SentTo(CustomerChatId).Should().ContainSingle().Which.Should().Be(FeedbackBotTexts.Unavailable);
    }

    // ---------------------------------------------------------------- binding

    [Fact]
    public async Task AValidStartPayloadBindsTheChatToTheAccountAndGreetsIt()
    {
        var userId = await SeedUserAsync();
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        var payload = FeedbackDeepLink.CreatePayload(
            userId, FeedbackTestFactory.SigningKey, TimeSpan.FromDays(30), TestClock.UtcNow);

        await using (var context = CreateContext())
        {
            await FeedbackTestFactory.Router(context, client, jobs)
                .HandleAsync(FeedbackTestFactory.StartCommand(CustomerChatId, payload));
        }

        client.SentTo(CustomerChatId).Should().ContainSingle()
            .Which.Should().Be(FeedbackBotTexts.WelcomeBound);

        // A message sent after the binding inherits the account.
        Guid bindingId;
        await using (var context = CreateContext())
        {
            await FeedbackTestFactory.Router(context, client, jobs)
                .HandleAsync(FeedbackTestFactory.CustomerMessage(CustomerChatId, 104, "дорого"));

            bindingId = await context.FeedbackMessages
                .Where(m => m.TelegramMessageId == 104)
                .Select(m => m.Id)
                .SingleAsync();
        }

        (await ReadOne(bindingId)).UserId.Should().Be(userId);
    }

    [Fact]
    public async Task AForgedStartPayloadLeavesTheChatUnboundButStillWelcomed()
    {
        await SeedUserAsync();
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        var forged = FeedbackDeepLink.CreatePayload(
            Guid.CreateVersion7(), FeedbackTestFactory.SigningKey, TimeSpan.FromDays(30), TestClock.UtcNow);

        await using var context = CreateContext();
        await FeedbackTestFactory.Router(context, client, jobs)
            .HandleAsync(FeedbackTestFactory.StartCommand(CustomerChatId, forged));

        // Refusing the message is not an option: this is a person who found the bot by name and
        // is telling us something. They get the unbound greeting and their text still lands.
        client.SentTo(CustomerChatId).Should().ContainSingle()
            .Which.Should().Be(FeedbackBotTexts.WelcomeUnbound);

        (await CountForChatAsync(-100999)).Should().Be(0, "a bot added to some group must not collect that group's conversation");
    }

    [Fact]
    public async Task AStartWithoutAPayloadLeavesTheChatUnbound()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        await using var context = CreateContext();
        await FeedbackTestFactory.Router(context, client, jobs)
            .HandleAsync(FeedbackTestFactory.StartCommand(CustomerChatId, payload: null));

        client.SentTo(CustomerChatId).Should().ContainSingle()
            .Which.Should().Be(FeedbackBotTexts.WelcomeUnbound);
    }

    [Fact]
    public async Task BindingASecondChatToTheSameAccountDetachesTheFirst()
    {
        // One account, one chat: the newest link wins, because a stale link in an old message is
        // not what the customer would expect after pressing the button again.
        var userId = await SeedUserAsync();
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var payload = FeedbackDeepLink.CreatePayload(
            userId, FeedbackTestFactory.SigningKey, TimeSpan.FromDays(30), TestClock.UtcNow);

        await using var context = CreateContext();
        var router = FeedbackTestFactory.Router(context, client, jobs);

        await router.HandleAsync(FeedbackTestFactory.StartCommand(CustomerChatId, payload));
        await router.HandleAsync(FeedbackTestFactory.StartCommand(OtherCustomerChatId, payload));
        await router.HandleAsync(FeedbackTestFactory.CustomerMessage(CustomerChatId, 105, "старий чат"));
        await router.HandleAsync(FeedbackTestFactory.CustomerMessage(OtherCustomerChatId, 106, "новий чат"));

        var bound = await context.FeedbackMessages.AsNoTracking().ToListAsync();
        bound.Single(m => m.TelegramMessageId == 105).UserId.Should().BeNull();
        bound.Single(m => m.TelegramMessageId == 106).UserId.Should().Be(userId);
    }

    [Fact]
    public async Task HelpAnswersWithoutStoringAnything()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        await using var context = CreateContext();
        await FeedbackTestFactory.Router(context, client, jobs)
            .HandleAsync(FeedbackTestFactory.CustomerMessage(CustomerChatId, 107, "/help"));

        client.SentTo(CustomerChatId).Should().ContainSingle().Which.Should().Be(FeedbackBotTexts.Help);
        (await CountForChatAsync(-100999)).Should().Be(0, "a bot added to some group must not collect that group's conversation");
    }

    // ---------------------------------------------------------------- triage

    [Fact]
    public async Task AKeptMessageReachesTheStaffGroupAndCanBeAnswered()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var classifier = new StubFeedbackClassifier();

        var messageId = await IngestAsync(client, jobs, "Хочу фільтр за ціною", 300);
        var staffPostId = await RunTriageAsync(classifier, client, messageId, kept: true);
        staffPostId.Should().BeGreaterThan(0, "triage must record which staff post the message became");

        client.SentTo(FeedbackTestFactory.StaffChatId).Should().ContainSingle();
        client.SentTo(FeedbackTestFactory.StaffChatId).Single().Should().Contain("Хочу фільтр за ціною");

        var row = await ReadOne(messageId);
        row.Verdict.Should().Be(FeedbackVerdict.Kept);
        row.StaffChatId.Should().Be(FeedbackTestFactory.StaffChatId);
        row.StaffMessageId.Should().Be(staffPostId);

        // The team answers by quoting the bot's post; the customer gets it in their own chat.
        await using (var context = CreateContext())
        {
            await FeedbackTestFactory.Router(context, client, jobs).HandleAsync(
                FeedbackTestFactory.CustomerMessage(
                    chatId: FeedbackTestFactory.StaffChatId,
                    messageId: 400,
                    text: "Дякуємо, додамо у наступному релізі.",
                    fromId: FeedbackTestFactory.StaffUserId,
                    replyToMessageId: staffPostId,
                    replyToChatId: FeedbackTestFactory.StaffChatId));
        }

        client.SentTo(CustomerChatId).Should().Contain("Дякуємо, додамо у наступному релізі.");

        await using var verify = CreateContext();
        var reply = await verify.FeedbackMessageReplies
            .AsNoTracking()
            .SingleAsync(r => r.FeedbackMessageId == messageId);
        reply.StaffUserId.Should().Be(FeedbackTestFactory.StaffUserId);
        reply.StaffUserName.Should().Be("Test");
        reply.DeliveredMessageId.Should().NotBeNull();
        reply.DeliveryError.Should().BeNull();

        (await ReadOne(messageId)).StaffRepliedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task AJunkMessageIsClassifiedButNeverReachesTheStaffGroup()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var classifier = new StubFeedbackClassifier
        {
            Result = new FeedbackClassification(
                FeedbackVerdict.Junk, null, null, null, FeedbackLanguage.Uk, 0.95)
        };

        var messageId = await IngestAsync(client, jobs, "йоук", 301);
        await RunTriageAsync(classifier, client, messageId, kept: false);

        client.SentTo(FeedbackTestFactory.StaffChatId).Should().BeEmpty();

        var row = await ReadOne(messageId);
        row.Verdict.Should().Be(FeedbackVerdict.Junk);
        row.StaffMessageId.Should().BeNull();
    }

    [Fact]
    public async Task ALowConfidenceKeepIsDroppedButStaysVisibleWithItsConfidence()
    {
        // The whole reason junk is stored rather than discarded: a classifier that quietly
        // started eating real complaints has to be able to show up in a filter.
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var classifier = new StubFeedbackClassifier
        {
            Result = new FeedbackClassification(
                FeedbackVerdict.Kept, FeedbackCategory.Complaint, FeedbackSeverity.High,
                "дуже коротко", FeedbackLanguage.Uk, 0.3)
        };

        var messageId = await IngestAsync(client, jobs, "оплата", 302);
        await RunTriageAsync(classifier, client, messageId, kept: false);

        client.SentTo(FeedbackTestFactory.StaffChatId).Should().BeEmpty();

        var row = await ReadOne(messageId);
        row.Verdict.Should().Be(FeedbackVerdict.Junk);
        row.Confidence.Should().Be(0.3m);
        row.Category.Should().Be(FeedbackCategory.Complaint, "the detail survives even when the verdict does not");
    }

    [Fact]
    public async Task AClassifierOutageLeavesTheMessageReadableAndUndelivered()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var classifier = new StubFeedbackClassifier
        {
            Throw = new InvalidOperationException("classifier unavailable")
        };

        var messageId = await IngestAsync(client, jobs, "оплата не працює", 303);

        await using (var context = CreateContext())
        {
            var act = async () => await FeedbackTestFactory.Triage(context, classifier, client)
                .TriageAsync(messageId);

            // Rethrown so the failure is visible on the Hangfire dashboard instead of being a
            // job that quietly succeeded and was never looked at.
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        client.SentTo(FeedbackTestFactory.StaffChatId).Should().BeEmpty();

        var row = await ReadOne(messageId);
        row.Verdict.Should().BeNull();
        row.ClassifierError.Should().Contain("classifier unavailable");
        row.Text.Should().Be("оплата не працює", "the message itself is never at risk");
    }

    [Fact]
    public async Task WithNoClassifierConfiguredEveryMessageIsPostedUnclassified()
    {
        // The deployment shape that has not wired an LLM yet still collects feedback; it just
        // does not filter it.
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var classifier = new StubFeedbackClassifier { IsConfigured = false };

        var messageId = await IngestAsync(client, jobs, "привіт", 304);
        await RunTriageAsync(classifier, client, messageId, kept: true);

        client.SentTo(FeedbackTestFactory.StaffChatId).Should().ContainSingle();
        (await ReadOne(messageId)).Verdict.Should().BeNull();
    }

    [Fact]
    public async Task TheStaffPostIsSentAsHtmlWithTheCustomersMarkupEscaped()
    {
        // The staff post is the only message in the feature authored as HTML. It used to be sent
        // with no parse_mode at all, so the team read literal "<blockquote>" and "[b]Idea[/b]"
        // instead of a formatted post - while the escaping in FeedbackBotTexts went unused.
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var classifier = new StubFeedbackClassifier();

        var messageId = await IngestAsync(client, jobs, "<b>не працює</b> & ламає", 306);
        await RunTriageAsync(classifier, client, messageId, kept: true);

        client.ParseModesFor(FeedbackTestFactory.StaffChatId)
            .Should().ContainSingle()
            .Which.Should().Be("HTML", "without parse_mode Telegram ships the team our raw markup");

        var post = client.SentTo(FeedbackTestFactory.StaffChatId).Single();
        post.Should().Contain("<blockquote>", "our own tags must survive");
        post.Should().Contain("&lt;b&gt;не працює&lt;/b&gt; &amp; ламає", "customer markup must not");
    }

    [Fact]
    public async Task AStaffReplyIsRelayedAsPlainText()
    {
        // The mirror image: a staff member's own words may contain "&" or "<", and HTML mode
        // would have Telegram reject the delivery with an opaque 400 over "Ціна & доставка".
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        var messageId = await IngestAsync(client, jobs, "скарга", 307);
        var staffPostId = await RunTriageAsync(new StubFeedbackClassifier(), client, messageId, kept: true);

        await using (var context = CreateContext())
        {
            await FeedbackTestFactory.Router(context, client, jobs).HandleAsync(
                FeedbackTestFactory.CustomerMessage(
                    chatId: FeedbackTestFactory.StaffChatId,
                    messageId: 401,
                    text: "Ціна & доставка — уточніть",
                    fromId: FeedbackTestFactory.StaffUserId,
                    replyToMessageId: staffPostId,
                    replyToChatId: FeedbackTestFactory.StaffChatId));
        }

        client.ParseModesFor(CustomerChatId).Should().OnlyContain(mode => mode == null);
    }

    [Fact]
    public async Task AnUnclassifiedPostCarriesNoPlaceholderLabels()
    {
        // "[?] · [?]" told the team nothing; a bare author line reads as "not triaged yet", which
        // is what it actually is.
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        var messageId = await IngestAsync(client, jobs, "оплата не працює", 308);
        await RunTriageAsync(new StubFeedbackClassifier { IsConfigured = false }, client, messageId, kept: true);

        var post = client.SentTo(FeedbackTestFactory.StaffChatId).Single();
        post.Should().NotContain("?");
        post.Should().NotContain("<b>"); // no classification block at all
        post.Should().StartWith("🟡");
    }

    [Fact]
    public async Task StartingAnAlreadyLinkedChatDoesNotRepeatTheOnboardingCopy()
    {
        // Every press of "Написати нам" re-sends /start through the deep link, so the onboarding text
        // used to land in the chat again on every visit, burying the conversation under identical
        // copies. A repeat must be one line.
        var userId = await SeedUserAsync();
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        var payload = FeedbackDeepLink.CreatePayload(
            userId, FeedbackTestFactory.SigningKey, TimeSpan.FromDays(30), TestClock.UtcNow);

        await using (var context = CreateContext())
        {
            await FeedbackTestFactory.Router(context, client, jobs)
                .HandleAsync(FeedbackTestFactory.StartCommand(CustomerChatId, payload));
        }

        client.SentTo(CustomerChatId).Should().ContainSingle()
            .Which.Should().Be(FeedbackBotTexts.WelcomeBound, "the first contact is the onboarding");

        await using (var context = CreateContext())
        {
            await FeedbackTestFactory.Router(context, client, jobs)
                .HandleAsync(FeedbackTestFactory.StartCommand(CustomerChatId, payload));
        }

        var sent = client.SentTo(CustomerChatId).ToList();
        sent.Should().HaveCount(2);
        sent[0].Should().Be(FeedbackBotTexts.WelcomeBound, "the first contact is the onboarding");
        sent[1].Should().Be(FeedbackBotTexts.AlreadyLinked, "a second visit is not a first contact");
    }

    [Fact]
    public async Task AHandTypedStartOnALinkedChatDoesNotTellThemToLinkItAgain()
    {
        // The advice "open it in the app so the chat links to your account" is false for someone who
        // already is linked, and sending it to them is how you teach customers to distrust the bot.
        var userId = await SeedUserAsync();
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        var payload = FeedbackDeepLink.CreatePayload(
            userId, FeedbackTestFactory.SigningKey, TimeSpan.FromDays(30), TestClock.UtcNow);

        await using (var context = CreateContext())
        {
            await FeedbackTestFactory.Router(context, client, jobs)
                .HandleAsync(FeedbackTestFactory.StartCommand(CustomerChatId, payload));
        }

        client.Sent.Clear();

        await using (var context = CreateContext())
        {
            await FeedbackTestFactory.Router(context, client, jobs)
                .HandleAsync(FeedbackTestFactory.StartCommand(CustomerChatId, null));
        }

        client.SentTo(CustomerChatId).Should().ContainSingle()
            .Which.Should().NotBe(FeedbackBotTexts.WelcomeUnbound)
            .And.Be(FeedbackBotTexts.AlreadyLinked);
    }

    [Fact]
    public async Task SwitchingAccountsOnTheSameChatGetsTheFullOnboardingAgain()
    {
        // The one case where repeating the long copy is right: the chat genuinely changed hands, and
        // the customer should be told which account it now belongs to.
        var firstUser = await SeedUserAsync();
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        var firstPayload = FeedbackDeepLink.CreatePayload(
            firstUser, FeedbackTestFactory.SigningKey, TimeSpan.FromDays(30), TestClock.UtcNow);

        await using (var context = CreateContext())
        {
            await FeedbackTestFactory.Router(context, client, jobs)
                .HandleAsync(FeedbackTestFactory.StartCommand(CustomerChatId, firstPayload));
        }

        var secondUser = await SeedUserAsync();
        var secondPayload = FeedbackDeepLink.CreatePayload(
            secondUser, FeedbackTestFactory.SigningKey, TimeSpan.FromDays(30), TestClock.UtcNow);

        client.Sent.Clear();

        await using (var context = CreateContext())
        {
            await FeedbackTestFactory.Router(context, client, jobs)
                .HandleAsync(FeedbackTestFactory.StartCommand(CustomerChatId, secondPayload));
        }

        client.SentTo(CustomerChatId).Should().ContainSingle()
            .Which.Should().Be(FeedbackBotTexts.WelcomeBound);

        await using var verify = CreateContext();
        var binding = await verify.FeedbackChatBindings
            .AsNoTracking()
            .SingleAsync(b => b.TelegramChatId == CustomerChatId);
        binding.UserId.Should().Be(secondUser);
    }

    [Fact]
    public async Task AFirstStartWithoutAPayloadStillGetsTheLinkingInstructions()
    {
        // The counterpart: an unlinked chat must still be told how to link, or the channel is
        // unreachable for anyone who finds the bot by name.
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        await using (var context = CreateContext())
        {
            await FeedbackTestFactory.Router(context, client, jobs)
                .HandleAsync(FeedbackTestFactory.StartCommand(CustomerChatId, null));
        }

        client.SentTo(CustomerChatId).Should().ContainSingle()
            .Which.Should().Be(FeedbackBotTexts.WelcomeUnbound);
    }

    [Fact]
    public async Task AFailedStaffDeliveryLeavesTheMessageStoredForALaterRetry()
    {
        var client = new RecordingTelegramFeedbackClient { FailForChatId = FeedbackTestFactory.StaffChatId };
        var jobs = new RecordingJobClient();
        var classifier = new StubFeedbackClassifier();

        var messageId = await IngestAsync(client, jobs, "скарга", 305);
        await RunTriageAsync(classifier, client, messageId, kept: true);

        var row = await ReadOne(messageId);
        row.StaffMessageId.Should().BeNull("nothing landed in the group, so nothing may claim it did");
        row.Text.Should().Be("скарга");
    }

    [Fact]
    public async Task RunningTriageTwicePostsToTheStaffGroupOnce()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var classifier = new StubFeedbackClassifier();

        var messageId = await IngestAsync(client, jobs, "дубль роботи", 306);
        await RunTriageAsync(classifier, client, messageId, kept: true);
        await RunTriageAsync(classifier, client, messageId, kept: true);

        // Two identical posts read as two customers reporting the same bug.
        client.SentTo(FeedbackTestFactory.StaffChatId).Should().ContainSingle();
    }

    // ---------------------------------------------------------------- relay gates

    [Fact]
    public async Task GroupChatterThatQuotesNothingNeverReachesACustomer()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var (messageId, _) = await SeedDeliveredRowAsync(client);
        var customerInboxAfterSeed = client.SentTo(CustomerChatId).Count();

        await using var context = CreateContext();
        await FeedbackTestFactory.Router(context, client, jobs).HandleAsync(
            FeedbackTestFactory.CustomerMessage(
                chatId: FeedbackTestFactory.StaffChatId,
                messageId: 500,
                text: "до обіду перенесімо реліз",
                fromId: FeedbackTestFactory.StaffUserId,
                replyToChatId: FeedbackTestFactory.StaffChatId));

        await AssertNothingRelayed(client, context, messageId, customerInboxAfterSeed);
    }

    [Fact]
    public async Task AnAllowlistedMemberQuotingSomethingTheBotNeverPostedReachesNobody()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var (messageId, _) = await SeedDeliveredRowAsync(client);
        var customerInboxAfterSeed = client.SentTo(CustomerChatId).Count();

        await using var context = CreateContext();
        await FeedbackTestFactory.Router(context, client, jobs).HandleAsync(
            FeedbackTestFactory.CustomerMessage(
                chatId: FeedbackTestFactory.StaffChatId,
                messageId: 501,
                text: "це не для клієнтів",
                fromId: FeedbackTestFactory.StaffUserId,
                replyToMessageId: 123_456_789,
                replyToChatId: FeedbackTestFactory.StaffChatId));

        await AssertNothingRelayed(client, context, messageId, customerInboxAfterSeed);
    }

    [Fact]
    public async Task SomebodyInTheGroupWhoIsNotOnTheAllowlistCannotReplyToCustomers()
    {
        // Being added to the group is a Telegram action; it must not be enough to start messaging
        // customers. This is the gate that makes the reply relay safe to leave switched on.
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();
        var (messageId, deliveredId) = await SeedDeliveredRowAsync(client);
        var customerInboxAfterSeed = client.SentTo(CustomerChatId).Count();

        await using var context = CreateContext();
        await FeedbackTestFactory.Router(context, client, jobs).HandleAsync(
            FeedbackTestFactory.CustomerMessage(
                chatId: FeedbackTestFactory.StaffChatId,
                messageId: 502,
                text: "я тут не в списку",
                fromId: FeedbackTestFactory.OutsiderUserId,
                replyToMessageId: deliveredId,
                replyToChatId: FeedbackTestFactory.StaffChatId));

        await AssertNothingRelayed(client, context, messageId, customerInboxAfterSeed);
    }

    [Fact]
    public async Task AFailedRelayIsRecordedWithTheReasonRatherThanVanishing()
    {
        var client = new RecordingTelegramFeedbackClient { FailForChatId = CustomerChatId };
        var jobs = new RecordingJobClient();
        var (messageId, deliveredId) = await SeedDeliveredRowAsync(client);

        await using var context = CreateContext();
        await FeedbackTestFactory.Router(context, client, jobs).HandleAsync(
            FeedbackTestFactory.CustomerMessage(
                chatId: FeedbackTestFactory.StaffChatId,
                messageId: 503,
                text: "відповідь, яка не дійде",
                fromId: FeedbackTestFactory.StaffUserId,
                replyToMessageId: deliveredId,
                replyToChatId: FeedbackTestFactory.StaffChatId));

        await using var verify = CreateContext();
        var reply = await verify.FeedbackMessageReplies
            .AsNoTracking()
            .SingleAsync(r => r.FeedbackMessageId == messageId);
        reply.DeliveredMessageId.Should().BeNull();
        reply.DeliveryError.Should().NotBeNullOrWhiteSpace();

        // staff_replied_at means "the customer has it", so a failed send must not stamp it - a
        // timestamp here would tell the team the person was answered when they were not.
        (await ReadOne(messageId)).StaffRepliedAtUtc.Should().BeNull();
    }

    // ---------------------------------------------------------------- webhook auth

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("FBHOOK")]
    public async Task AWebhookDeliveryWithoutTheRightSecretIsRejectedAndStoresNothing(string? secret)
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        await using var context = CreateContext();
        var controller = NewWebhookController(context, client, jobs, NewHttpContext(secret));

        var result = await controller.Receive(
            FeedbackTestFactory.CustomerMessage(CustomerChatId, 600, "підробка"), default);

        result.Should().BeOfType<UnauthorizedResult>();
        (await CountForMessageAsync(600)).Should().Be(0, "an unauthenticated delivery must not reach the table");
        client.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task AWebhookDeliveryWithTheRightSecretIsAccepted()
    {
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        await using var context = CreateContext();
        var controller = NewWebhookController(context, client, jobs, NewHttpContext(FeedbackTestFactory.WebhookSecret));

        var result = await controller.Receive(
            FeedbackTestFactory.CustomerMessage(CustomerChatId, 601, "справжнє"), default);

        result.Should().BeOfType<OkResult>();

        // And it always answers 2xx once authenticated: a 500 here would make Telegram redeliver
        // the same update forever.
        (await CountForMessageAsync(601)).Should().Be(1);
    }

    [Fact]
    public async Task AWebhookDeliveryIsAnsweredWithTwoHundredEvenWhenThePipelineBlowsUp()
    {
        // A non-2xx makes Telegram retry the update forever, and a retry of a message that was
        // already stored is a duplicate in the staff group. Nothing downstream may turn into a 5xx.
        var client = new RecordingTelegramFeedbackClient();
        var jobs = new RecordingJobClient();

        await using var context = CreateContext();
        var router = new ExplodingRouter();
        var controller = new TelegramFeedbackWebhookController(
            router,
            Microsoft.Extensions.Options.Options.Create(FeedbackTestFactory.Options()),
            NullLogger<TelegramFeedbackWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = NewHttpContext(FeedbackTestFactory.WebhookSecret) }
        };

        var result = await controller.Receive(
            FeedbackTestFactory.CustomerMessage(CustomerChatId, 602, "вибух"), default);

        result.Should().BeOfType<OkResult>();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A router that fails the way a database outage would, to pin the webhook's
    /// "always answer 2xx once authenticated" contract.</summary>
    private sealed class ExplodingRouter : Features.Feedback.FeedbackUpdateRouter
    {
        public ExplodingRouter()
            : base(
                null!,
                null!,
                null!,
                Microsoft.Extensions.Options.Options.Create(FeedbackTestFactory.Options()),
                NullLogger<Features.Feedback.FeedbackUpdateRouter>.Instance)
        {
        }

        public override Task HandleAsync(TelegramUpdate update, CancellationToken ct = default)
            => throw new InvalidOperationException("database is down");
    }

    private TelegramFeedbackWebhookController NewWebhookController(
        ApplicationDbContext context,
        RecordingTelegramFeedbackClient client,
        RecordingJobClient jobs,
        DefaultHttpContext http)
    {
        var controller = new TelegramFeedbackWebhookController(
            FeedbackTestFactory.Router(context, client, jobs),
            Microsoft.Extensions.Options.Options.Create(FeedbackTestFactory.Options()),
            NullLogger<TelegramFeedbackWebhookController>.Instance);

        // A controller built by hand has no ControllerContext, and Request is resolved through it -
        // which is where the secret header lives.
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        return controller;
    }

    private static DefaultHttpContext NewHttpContext(string? secret)
    {
        var http = new DefaultHttpContext();
        if (secret is not null)
            http.Request.Headers[TelegramFeedbackWebhookController.SecretHeader] = secret;
        return http;
    }

    /// <summary>Posts one customer message through the router and returns the stored id.</summary>
    private async Task<Guid> IngestAsync(
        RecordingTelegramFeedbackClient client,
        RecordingJobClient jobs,
        string text,
        long messageId)
    {
        await using var context = CreateContext();
        await FeedbackTestFactory.Router(context, client, jobs)
            .HandleAsync(FeedbackTestFactory.CustomerMessage(CustomerChatId, messageId, text));

        return await context.FeedbackMessages
            .Where(m => m.TelegramChatId == CustomerChatId && m.TelegramMessageId == messageId)
            .Select(m => m.Id)
            .SingleAsync();
    }

    private async Task<long> RunTriageAsync(
        StubFeedbackClassifier classifier,
        RecordingTelegramFeedbackClient client,
        Guid messageId,
        bool kept)
    {
        await using var context = CreateContext();
        await FeedbackTestFactory.Triage(context, classifier, client).TriageAsync(messageId);

        var row = await context.FeedbackMessages.AsNoTracking().FirstAsync(m => m.Id == messageId);
        if (!kept)
            return 0;

        return row.StaffMessageId ?? 0;
    }

    /// <summary>A row that already reached the staff group, so the relay gates have a real target.
    /// Returns the message id and the id of the post it became.</summary>
    private async Task<(Guid MessageId, long StaffPostId)> SeedDeliveredRowAsync(
        RecordingTelegramFeedbackClient client)
    {
        var messageId = await IngestAsync(client, new RecordingJobClient(), "скарга для відповіді", 700);

        await using var context = CreateContext();
        var row = await context.FeedbackMessages.AsTracking().FirstAsync(m => m.Id == messageId);
        var postId = await client.SendMessageAsync(FeedbackTestFactory.StaffChatId, "seeded staff post");
        row.StaffChatId = FeedbackTestFactory.StaffChatId;
        row.StaffMessageId = postId;
        await context.SaveChangesAsync();

        return (messageId, postId ?? 0);
    }

    /// <summary>
    /// The guard these callers exist to prove. It MUST be awaited: un-awaited, xUnit finishes the
    /// test the moment the method returns, the assertions below never run, and a failure inside
    /// lands in an unobserved Task where it is silently dropped — so the test would report PASS
    /// on exactly the regressions it was written to catch (group chatter reaching a customer).
    /// </summary>
    private async Task AssertNothingRelayed(
        RecordingTelegramFeedbackClient client,
        ApplicationDbContext context,
        Guid messageId,
        int customerInboxAfterSeed)
    {
        // Measured against a snapshot taken right after the seed, NOT against an empty inbox.
        // SeedDeliveredRowAsync ingests the customer's own message through the real router, which
        // answers it with the bot's acknowledgement (FeedbackUpdateRouter:133) — so an inbox of
        // zero was never a reachable state, and asserting it is what made this helper unpassable.
        // The guard is that the count must NOT GROW: the seed's acknowledgement is the customer
        // chat's due, anything after it is group chatter leaking.
        client.SentTo(CustomerChatId).Should().HaveCount(
            customerInboxAfterSeed,
            "group chatter must never reach a customer");

        await using var verify = CreateContext();
        (await verify.FeedbackMessageReplies.CountAsync(r => r.FeedbackMessageId == messageId))
            .Should().Be(0);
    }

    private async Task<FeedbackMessage> ReadOne(Guid id)
    {
        await using var context = CreateContext();
        return await context.FeedbackMessages.AsNoTracking().FirstAsync(m => m.Id == id);
    }

    /// <summary>
    /// Rows for one Telegram message id. The class shares a database with every other integration
    /// test class, so an unqualified count would assert on whatever else happened to be in the table.
    /// </summary>
    private async Task<int> CountForMessageAsync(long messageId)
    {
        await using var context = CreateContext();
        return await context.FeedbackMessages.CountAsync(m => m.TelegramMessageId == messageId);
    }

    private async Task<int> CountForMessageRangeAsync(long fromInclusive, long toInclusive)
    {
        await using var context = CreateContext();
        return await context.FeedbackMessages.CountAsync(
            m => m.TelegramMessageId >= fromInclusive && m.TelegramMessageId <= toInclusive);
    }

    private async Task<int> CountForChatAsync(long chatId)
    {
        await using var context = CreateContext();
        return await context.FeedbackMessages.CountAsync(m => m.TelegramChatId == chatId);
    }

    private async Task<List<FeedbackMessage>> ReadByChat(long chatId)
    {
        await using var context = CreateContext();
        return await context.FeedbackMessages.AsNoTracking().Where(m => m.TelegramChatId == chatId).ToListAsync();
    }

    private async Task<Guid> SeedUserAsync()
    {
        var id = Guid.CreateVersion7();
        await using var context = CreateContext();
        context.Users.Add(new FuelFlow.SharedKernel.Domain.User
        {
            Id = id,
            PhoneNumber = $"+38099{Interlocked.Increment(ref _nextPhoneSuffix):D8}",
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static TelegramUpdate TelegramUpdateFactory(bool supergroup, long chatId, string text) =>
        new()
        {
            UpdateId = 999,
            Message = new TelegramMessage
            {
                MessageId = 1,
                Text = text,
                From = new TelegramUser { Id = 1, FirstName = "Someone" },
                Chat = new TelegramChat { Id = chatId, Type = supergroup ? "supergroup" : "private" }
            }
        };
}