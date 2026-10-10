using FluentAssertions;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.SharedKernel.Options;
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
                QaPhones = qaPhones.ToList()
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