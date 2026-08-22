using FluentAssertions;
using FuelFlow.Features.Auth.SendCode.Services;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Auth;

public class SmsServicesTests
{
    [Fact]
    public async Task FakeSmsService_SendVerificationCodeAsync_ShouldCompleteAndLogWarning()
    {
        var logger = new Mock<ILogger<FakeSmsService>>();
        var service = new FakeSmsService(logger.Object);

        await service.SendVerificationCodeAsync("+380991234567", "123456", CancellationToken.None);

        // A real random code logs only the code line; the 000000 hint is
        // suppressed so logs don't mislead.
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            Times.Once);
    }

    [Fact]
    public async Task FakeSmsService_ShouldLogDevBypassHint_WhenCodeIs000000()
    {
        var logger = new Mock<ILogger<FakeSmsService>>();
        var service = new FakeSmsService(logger.Object);

        await service.SendVerificationCodeAsync("+380991234567", "000000", CancellationToken.None);

        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            Times.Exactly(2));
    }

    [Fact]
    public void TwilioSmsService_Constructor_ShouldInitializeWithoutThrowing()
    {
        var options = Options.Create(new TwilioOptions
        {
            AccountSid = "ACTestSid",
            AuthToken = "test-auth-token",
            PhoneNumber = "+12345678901"
        });

        using var budget = new SmsBudgetGuard(options, NullLogger<SmsBudgetGuard>.Instance);
        var service = new TwilioSmsService(options, budget, NullLogger<TwilioSmsService>.Instance);

        service.Should().NotBeNull();
    }

    [Fact]
    public void SmsBudgetGuard_ShouldRefuseOnceDailyLimitIsReached()
    {
        var options = Options.Create(new TwilioOptions { DailySendLimit = 2 });
        using var budget = new SmsBudgetGuard(options, NullLogger<SmsBudgetGuard>.Instance);

        budget.TryConsume().Should().BeTrue();
        budget.TryConsume().Should().BeTrue();
        budget.TryConsume().Should().BeFalse("the daily SMS spend ceiling must fail closed");
    }

    [Fact]
    public void SmsBudgetGuard_ShouldFallBackToDefault_WhenLimitIsNotConfigured()
    {
        var options = Options.Create(new TwilioOptions { DailySendLimit = 0 });
        using var budget = new SmsBudgetGuard(options, NullLogger<SmsBudgetGuard>.Instance);

        // A misconfigured zero must not mean "no SMS at all", nor "unlimited".
        budget.TryConsume().Should().BeTrue();
    }
}
