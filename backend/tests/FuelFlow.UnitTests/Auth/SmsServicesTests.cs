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
        var smsOptions = Options.Create(new SmsOptions { DailySendLimit = 2 });
        var twilioOptions = Options.Create(new TwilioOptions
        {
            AccountSid = "ACTestSid",
            AuthToken = "test-auth-token",
            PhoneNumber = "+12345678901"
        });

        using var budget = new SmsBudgetGuard(smsOptions, NullLogger<SmsBudgetGuard>.Instance);
        var service = new TwilioSmsService(twilioOptions, budget, NullLogger<TwilioSmsService>.Instance);

        service.Should().NotBeNull();
    }

    [Fact]
    public void SmsBudgetGuard_ShouldRefuseOnceDailyLimitIsReached()
    {
        var options = Options.Create(new SmsOptions { DailySendLimit = 2 });
        using var budget = new SmsBudgetGuard(options, NullLogger<SmsBudgetGuard>.Instance);

        budget.TryConsume().Should().BeTrue();
        budget.TryConsume().Should().BeTrue();
        budget.TryConsume().Should().BeFalse("the daily SMS spend ceiling must fail closed");
    }

    [Fact]
    public void SmsBudgetGuard_ShouldFallBackToDefault_WhenLimitIsNotConfigured()
    {
        var options = Options.Create(new SmsOptions { DailySendLimit = 0 });
        using var budget = new SmsBudgetGuard(options, NullLogger<SmsBudgetGuard>.Instance);

        // A misconfigured zero must not mean "no SMS at all", nor "unlimited".
        budget.TryConsume().Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"success_request\":{\"info\":{\"106\":\"380989361131\"}}}", null)]
    [InlineData("{\"success_request\":{\"add_info\":{\"src_addr\":\"Sender name is incorrect\"}}}", "Sender name is incorrect")]
    [InlineData("{\"success_request\":{\"add_info\":{\"380989361130\":\"This number is in the black list\"}}}", "This number is in the black list")]
    [InlineData("not json", "Invalid response from SMS Club")]
    [InlineData("{\"unexpected\":{}}", null)]
    public void SmsClubSmsService_ParseResponse_ShouldHandleApiResponses(string body, string? expectedError)
    {
        var succeeded = SmsClubSmsService.ParseResponse(body, out var error);

        if (expectedError == null && body.Contains("\"info\""))
        {
            // Success payloads (info present) succeed.
            succeeded.Should().BeTrue();
            error.Should().BeNull();
        }
        else if (expectedError == null)
        {
            // A response with neither info nor add_info is a rejection.
            succeeded.Should().BeFalse();
            error.Should().BeNull();
        }
        else
        {
            succeeded.Should().BeFalse();
            error.Should().Be(expectedError);
        }
    }
}