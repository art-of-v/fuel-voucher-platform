using FluentAssertions;
using FuelFlow.API.Extensions;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests.Settlement;

public class TelegramSettlementOptionsTests
{
    [Theory]
    [InlineData("101", new long[] { 101 })]
    [InlineData("101,202", new long[] { 101, 202 })]
    [InlineData(" 101 ,\t202 \r\n", new long[] { 101, 202 })]
    [InlineData("2147483648,9223372036854775807", new long[] { 2147483648, long.MaxValue })]
    public void The_scalar_binds_and_only_the_listed_senders_are_accepted(string csv, long[] expected)
    {
        var options = Bind(csv);

        options.AllowedUserIds.Should().Equal(expected);
        options.IsConfigured.Should().BeTrue();
        foreach (var id in expected)
            options.MayAccept(id).Should().BeTrue();
        options.MayAccept(999).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public void A_missing_or_blank_list_rejects_everyone(string? csv)
    {
        var options = Bind(csv);

        options.AllowedUserIds.Should().BeEmpty();
        options.IsConfigured.Should().BeFalse();
        options.MayAccept(101).Should().BeFalse();
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("101,not-an-id,202")]
    [InlineData(",101")]
    [InlineData("101,")]
    [InlineData("101,,202")]
    [InlineData("101, ,202")]
    [InlineData("101,0")]
    [InlineData("101,-202")]
    [InlineData("101,+202")]
    [InlineData("101,2 02")]
    [InlineData("101,202.0")]
    [InlineData("101,2e2")]
    [InlineData("101;202")]
    [InlineData("101,9223372036854775808")]
    public void One_bad_entry_rejects_all_options_without_disclosing_the_contents(string csv)
    {
        // A previously bound typed list is not a fallback for malformed CSV.
        var act = () => Bind(csv, indexedId: "999");

        var error = act.Should().Throw<OptionsValidationException>().Which;
        error.OptionsType.Should().Be(typeof(TelegramSettlementOptions));
        error.Message.Should().Be(
            "Telegram:Settlement:AllowedUserIdsCsv must contain only comma-separated positive Int64 user ids.");
        error.InnerException.Should().BeNull();
        error.ToString().Should().NotContain(csv);
    }

    [Theory]
    [InlineData("", new long[] { })]
    [InlineData(" \t ", new long[] { })]
    [InlineData("101,202", new long[] { 101, 202 })]
    public void An_explicit_scalar_replaces_rather_than_extends_an_indexed_list(string csv, long[] expected)
    {
        var options = Bind(csv, indexedId: "999");

        options.AllowedUserIds.Should().Equal(expected);
        options.MayAccept(999).Should().BeFalse();
    }

    [Fact]
    public void Without_a_scalar_the_existing_typed_list_still_binds()
    {
        var options = Bind(null, indexedId: "101");

        options.AllowedUserIds.Should().Equal(101L);
        options.MayAccept(101).Should().BeTrue();
    }

    [Fact]
    public void Direct_typed_options_used_by_handler_tests_remain_supported()
    {
        var options = Options.Create(new TelegramSettlementOptions
        {
            Enabled = true,
            BotToken = "test-token",
            WebhookSecret = "test-secret",
            AllowedChatId = -100,
            AllowedUserIds = [101]
        }).Value;

        options.MayAccept(101).Should().BeTrue();
        options.MayAccept(999).Should().BeFalse();
    }

    [Fact]
    public void A_bound_list_does_not_enable_the_bot_by_default()
    {
        var options = Bind("101,202", enabled: null);

        options.Enabled.Should().BeFalse();
        options.IsConfigured.Should().BeFalse();
        options.MayAccept(101).Should().BeFalse();
    }

    private static TelegramSettlementOptions Bind(string? csv, string? indexedId = null, string? enabled = "true")
    {
        var settings = new Dictionary<string, string?>
        {
            ["Telegram:Settlement:BotToken"] = "test-token",
            ["Telegram:Settlement:WebhookSecret"] = "test-secret",
            ["Telegram:Settlement:AllowedChatId"] = "-100"
        };
        if (enabled is not null)
            settings["Telegram:Settlement:Enabled"] = enabled;
        if (csv is not null)
            settings["Telegram:Settlement:AllowedUserIdsCsv"] = csv;
        if (indexedId is not null)
            settings["Telegram:Settlement:AllowedUserIds:0"] = indexedId;

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        // Exercise the application's binding AND post-configuration, not a copy of its parser.
        ServiceSetup.AddSettlementServices(services, config);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<TelegramSettlementOptions>>().Value;
    }
}
