using FluentAssertions;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests.Monobank;

/// <summary>
/// Which Monobank merchant a checkout is allowed to touch. Two merchants exist so production can
/// take real money while QA exercises the full payment path, and the whole point of the exercise
/// is that the two can never be confused: a QA account must have no route to the live token, and a
/// real customer must never be routed to the sandbox (their money would vanish with no report).
/// </summary>
public class MonobankMerchantResolverTests
{
    private const string QaPhone = "+380501234567";

    private static MonobankMerchantResolver CreateResolver(
        string liveToken = "live-token",
        string sandboxToken = "sandbox-token",
        params string[] qaPhones) =>
        new(
            Options.Create(new MonobankOptions
            {
                Enabled = true,
                Token = liveToken,
                SandboxToken = sandboxToken,
                QaPhones = string.Join(',', qaPhones)
            }),
            NullLogger<MonobankMerchantResolver>.Instance);

    [Fact]
    public void Resolve_AllowlistedPhone_RoutesToSandbox()
    {
        var resolver = CreateResolver(qaPhones: QaPhone);

        resolver.Resolve(QaPhone, isQaAccount: false).Should().Be(MonobankMerchant.Sandbox);
    }

    [Fact]
    public void Resolve_FlaggedAccountNotOnAllowlist_RoutesToSandbox()
    {
        // The flag is admin-set (the same signal that routes test stock) and is honoured as a
        // second routing signal, so a flagged QA account cannot pay live money just because
        // someone forgot its phone in Monobank:QaPhones.
        var resolver = CreateResolver();

        resolver.Resolve("+380999999999", isQaAccount: true).Should().Be(MonobankMerchant.Sandbox);
    }

    [Fact]
    public void Resolve_RegularCustomer_RoutesToLive_EvenWhenAllowlistedPhonesExist()
    {
        var resolver = CreateResolver(qaPhones: QaPhone);

        resolver.Resolve("+380999999999", isQaAccount: false).Should().Be(MonobankMerchant.Live);
    }

    [Fact]
    public void Resolve_RegularCustomer_IgnoresMissingSandboxConfiguration()
    {
        // A real customer must never be refused because the QA sandbox is misconfigured - that
        // would take an entire production checkout offline over an unrelated setting.
        var resolver = CreateResolver(sandboxToken: string.Empty, qaPhones: QaPhone);

        resolver.Resolve("+380999999999", isQaAccount: false).Should().Be(MonobankMerchant.Live);
    }

    [Fact]
    public void Resolve_UnknownPhoneWithoutFlag_RoutesToLive()
    {
        var resolver = CreateResolver();

        resolver.Resolve(phoneNumber: null, isQaAccount: false).Should().Be(MonobankMerchant.Live);
    }

    [Fact]
    public void Resolve_AllowlistWithSeveralPhonesAndStrayWhitespace_MatchesAnyOfThem()
    {
        // The allowlist arrives as one comma-separated environment variable, so the separators and
        // the spaces people type around them have to be tolerated. An entry that fails to match
        // because of a stray space is a QA account quietly routed to the live merchant.
        var resolver = CreateResolver(qaPhones: " +380501111111, +380502222222 ,");

        resolver.Resolve("+380502222222", isQaAccount: false).Should().Be(MonobankMerchant.Sandbox);
        resolver.Resolve("+380501111111", isQaAccount: false).Should().Be(MonobankMerchant.Sandbox);
        resolver.Resolve("+380503333333", isQaAccount: false).Should().Be(MonobankMerchant.Live);
    }

    [Fact]
    public void Resolve_EmptyAllowlist_RoutesEveryoneButFlaggedAccountsToLive()
    {
        // An unset allowlist must read as "nobody on it", never as "everyone on it".
        var resolver = CreateResolver();

        resolver.Resolve("+380999999999", isQaAccount: false).Should().Be(MonobankMerchant.Live);

        new MonobankOptions().QaPhoneList.Should().BeEmpty();
    }

    [Fact]
    public void QaPhones_BoundFromEnvironmentStyleKeys_ParsesIntoTheAllowlist()
    {
        // This is the shape Docker/compose supplies: one flat scalar per key, exactly as an
        // environment variable arrives. It is why QaPhones is a comma-separated string and not a
        // list - the binder silently produces an EMPTY list from such a value, and an allowlist
        // that silently contains nobody sends QA accounts to the live merchant.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Monobank:QaPhones"] = " +380501111111 , +380502222222 ,",
                ["Monobank:Enabled"] = "true",
            })
            .Build();

        var options = configuration.GetSection(MonobankOptions.SectionName).Get<MonobankOptions>()!;

        options.QaPhoneList.Should().Equal("+380501111111", "+380502222222");
    }

    [Fact]
    public void Resolve_AllowlistedAccountWithNoSandboxConfigured_Refuses()
    {
        // The one direction that is never safe to fall back from: sending a test account to the
        // live merchant takes real money from someone who is testing. Refuse instead.
        var resolver = CreateResolver(sandboxToken: string.Empty, qaPhones: QaPhone);

        var act = () => resolver.Resolve(QaPhone, isQaAccount: false);

        act.Should().Throw<MonobankMerchantUnavailableException>()
            .Which.Merchant.Should().Be(MonobankMerchant.Sandbox);
    }

    [Fact]
    public void Resolve_FlaggedAccountWithNoSandboxConfigured_Refuses()
    {
        var resolver = CreateResolver(sandboxToken: string.Empty);

        var act = () => resolver.Resolve("+380999999999", isQaAccount: true);

        act.Should().Throw<MonobankMerchantUnavailableException>()
            .Which.Merchant.Should().Be(MonobankMerchant.Sandbox);
    }

    [Fact]
    public void Resolve_WhitespaceSandboxToken_CountsAsUnconfigured()
    {
        // A blank-but-present env var must not read as "configured" - it would send QA checkouts
        // to a token that cannot authenticate, and the failure would look like a payment problem.
        var resolver = CreateResolver(sandboxToken: "   ", qaPhones: QaPhone);

        var act = () => resolver.Resolve(QaPhone, isQaAccount: false);

        act.Should().Throw<MonobankMerchantUnavailableException>();
    }
}