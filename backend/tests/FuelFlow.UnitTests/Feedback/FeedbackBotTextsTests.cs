using FluentAssertions;
using FuelFlow.Features.Feedback;
using Xunit;

namespace FuelFlow.UnitTests.Feedback;

/// <summary>
/// The staff post is the only place a customer sees a human-written product surface, and it is
/// rendered by Telegram as HTML. An unescaped bracket in a customer's complaint would make
/// Telegram reject the whole post - losing exactly the message that happened to contain one.
/// </summary>
public class FeedbackBotTextsTests
{
    [Fact]
    public void CustomerTextIsHtmlEscapedInTheStaffPost()
    {
        var post = FeedbackBotTexts.StaffPost(
            "Bug",
            "Medium",
            "…0203",
            "Клієнт пише про помилку",
            "Не працює <б>оплата</б> & списання",
            "1.2.3",
            "ios");

        post.Should().Contain("&lt;б&gt;");
        post.Should().Contain("&amp;");
        post.Should().NotContain("<б>");
    }

    [Fact]
    public void TheStaffPostCarriesTheCategorySeverityAuthorAndVerbatimText()
    {
        var post = FeedbackBotTexts.StaffPost(
            "Idea",
            "Low",
            "…0203",
            "Хоче фільтр за ціною",
            "Хочу фільтрувати станції за грн/л у кошику",
            "1.2.3",
            "android");

        post.Should().Contain("<b>Idea</b>");
        post.Should().Contain("<b>Low</b>");
        post.Should().Contain("…0203");
        post.Should().Contain("Хоче фільтр за ціною");
        post.Should().Contain("Хочу фільтрувати станції за грн/л у кошику");
        post.Should().Contain("android 1.2.3");
    }

    [Fact]
    public void TheStaffPostTruncatesAVeryLongPaste()
    {
        var post = FeedbackBotTexts.StaffPost(
            "Other",
            "Low",
            "анонім (Telegram)",
            "довге повідомлення",
            new string('я', 2000),
            null,
            null);

        post.Should().Contain("…");
        post.Should().NotContain(new string('я', 901));
    }

    [Fact]
    public void TheStaffPostOmitsTheClientLineWhenThereIsNoClientContext()
    {
        var post = FeedbackBotTexts.StaffPost(
            "Other",
            "Low",
            "анонім (Telegram)",
            "щось",
            "текст",
            null,
            null);

        post.Should().NotContain("Застосунок:");
    }
}