using FluentAssertions;
using FuelFlow.Features.Vouchers.Import;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// The fingerprint of a redeemable payload, and the decoder's log line.
/// </summary>
/// <remarks>
/// The QR payload is the bearer instrument: whoever holds it can have the voucher redeemed at
/// the pump, and nothing can revoke it afterwards. So the log gets the payload's fingerprint -
/// its length and a short digest - and never the payload itself.
/// </remarks>
public sealed class QrPayloadFingerprintTests
{
    private const string Payload = "9018$2000$;99999600000020368126=4507101299?";

    [Fact]
    public void The_Fingerprint_Should_Not_Contain_The_Payload()
    {
        var fingerprint = QrPayloadFingerprint.Describe(Payload);

        fingerprint.Should().NotContain(Payload,
            "the fingerprint exists so the payload can be identified without being carried "
            + "anywhere - a fingerprint that contains the payload is the payload");
        fingerprint.Should().NotContain(Payload.Split('$')[1],
            "not even a part of it: a fragment of a payload is a payload");
    }

    [Fact]
    public void The_Fingerprint_Should_Tell_Two_Payloads_Apart()
    {
        QrPayloadFingerprint.Describe(Payload)
            .Should().NotBe(QrPayloadFingerprint.Describe("9018$2000$;99999600000020368127=4507101299?"),
                "an operator has to be able to tell two failing rows apart");

        QrPayloadFingerprint.Describe(Payload)
            .Should().Be(QrPayloadFingerprint.Describe(Payload),
                "the same payload has to look the same every time, or a re-import cannot be "
                + "confirmed against the row it failed on");
    }

    [Fact]
    public void The_Fingerprint_Should_Say_How_Long_The_Payload_Was()
    {
        QrPayloadFingerprint.Describe(Payload).Should().Contain($"{Payload.Length} chars",
            "the length is what tells a truncated payload from a whole one");
    }

    [Fact]
    public void An_Empty_Payload_Should_Be_Described_As_Empty()
    {
        QrPayloadFingerprint.Describe(null).Should().Be("<empty>");
        QrPayloadFingerprint.Describe(string.Empty).Should().Be("<empty>");
    }

    [Fact]
    public void Decoding_A_Qr_Should_Not_Write_The_Payload_Into_The_Log()
    {
        var logger = new CapturingLogger<QrDecoder>();

        var base64 = new QrGeneratorV2().GenerateQrCode(Payload, 120, 120, "L");
        using var image = Image.Load<Rgba32>(Convert.FromBase64String(base64));

        var decoded = new QrDecoder(logger).Decode(image);

        decoded.Text.Should().Be(Payload, "the decoder still returns the payload - that is its job");
        logger.Messages.Should().NotBeEmpty("the decoder does log what it decoded");
        logger.Messages.Should().NotContain(message => message.Contains(Payload),
            "the payload is the redeemable instrument: a log line that carries it is a leaked "
            + "voucher, and a leaked fuel voucher cannot be revoked");
        logger.Messages.Should().Contain(message => message.Contains(QrPayloadFingerprint.Describe(Payload)),
            "what the log gets instead is the fingerprint - enough to identify the payload, "
            + "not enough to spend it");
    }

    /// <summary>
    /// A logger that keeps what it was told, so a test can read the log line the way an operator
    /// would - through the sink, not through the call site.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
