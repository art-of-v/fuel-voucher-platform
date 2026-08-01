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

        var service = new TwilioSmsService(options, NullLogger<TwilioSmsService>.Instance);

        service.Should().NotBeNull();
    }
}
