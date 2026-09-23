using FluentAssertions;
using FuelFlow.Features.Auth.ConfirmEmailChange;
using FuelFlow.Features.Auth.EmailChange;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.SharedKernel.Notifications.Email;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FuelFlow.UnitTests.Notifications;

/// <summary>
/// Covers the branded transactional-email upgrade (issue #11): every template renders as
/// multipart/alternative (branded HTML + the retained plain-text alternative) with the lion embedded
/// inline by <c>cid:</c>, dynamic values are HTML-encoded, the OTP code never leaks into the
/// inbox-preview preheader, and the SMTP layer sets a configurable From display name / optional
/// Reply-To while keeping the From address on the authenticated account.
/// </summary>
public sealed class BrandedEmailTests
{
    private static BrandedEmailLayout.Content SampleContent() => new()
    {
        Preheader = "Preview line",
        Heading = "Hello there",
        Paragraphs = ["First paragraph.", "Second paragraph."],
        Footnote = "Footer note.",
    };

    [Fact]
    public void BuildMessage_IsMultipart_WithHtmlText_AndInlineLion()
    {
        var message = BrandedEmailLayout.BuildMessage("Test subject", "Plain-text fallback body.", SampleContent());

        message.Subject.Should().Be("Test subject");
        message.TextBody.Should().Be("Plain-text fallback body.");
        message.HtmlBody.Should().NotBeNullOrWhiteSpace();

        // Branded shell markers: wordmark, dark canvas, neon accent, footer, and the caller's copy.
        message.HtmlBody.Should().Contain("FUELFLOW");
        message.HtmlBody.Should().Contain(EmailBrand.Canvas);
        message.HtmlBody.Should().Contain(EmailBrand.AccentBright);
        message.HtmlBody.Should().Contain("palne.shop");
        message.HtmlBody.Should().Contain("Hello there");
        message.HtmlBody.Should().Contain("First paragraph.");

        // Inline lion embedded and referenced by cid (renders without a "show images" prompt).
        message.InlineImages.Should().ContainSingle();
        var logo = message.InlineImages[0];
        logo.ContentId.Should().Be(EmailBrand.LogoContentId);
        logo.MediaType.Should().Be("image/png");
        logo.Content.Should().NotBeEmpty();
        message.HtmlBody.Should().Contain("cid:" + EmailBrand.LogoContentId);
    }

    [Fact]
    public void BuildMessage_HtmlEncodesDynamicValues()
    {
        var message = BrandedEmailLayout.BuildMessage(
            "s", "t",
            new BrandedEmailLayout.Content
            {
                Preheader = "p",
                Heading = "<script>alert('x')</script>",
                Paragraphs = ["a & b <tag>"],
            });

        message.HtmlBody.Should().NotContain("<script>");
        message.HtmlBody.Should().Contain("&lt;script&gt;");
        message.HtmlBody.Should().Contain("a &amp; b &lt;tag&gt;");
    }

    [Fact]
    public void LionLogo_LoadsEmbeddedAsset_UnderGmailClipLimit()
    {
        var logo = EmailBrand.LionLogo();

        logo.Should().NotBeNull("the lion PNG is an embedded resource of the API assembly");
        logo!.ContentId.Should().Be("fuelflow-lion");
        logo.MediaType.Should().Be("image/png");
        logo.Content.Length.Should().BeGreaterThan(0);
        logo.Content.Length.Should().BeLessThan(102_400, "the whole email must stay under Gmail's 102 KB clip limit");
    }

    [Fact]
    public void OtpEmail_PutsCodeInBody_ButNeverInPreheader()
    {
        const string code = "483920";
        var message = SendCodeCommandHandler.BuildAdminEmail(code);

        message.HtmlBody.Should().NotBeNullOrWhiteSpace();
        message.TextBody.Should().Contain(code);
        message.InlineImages.Should().ContainSingle(i => i.ContentId == "fuelflow-lion");

        // The code is shown in the visible HTML body ...
        message.HtmlBody.Should().Contain(code);

        // ... but must NOT ride in the hidden preheader (that would leak the OTP into an inbox preview).
        Preheader(message.HtmlBody).Should().NotContain(code);
    }

    [Fact]
    public void ConfirmationEmail_HasCta_AndRawFallbackLink()
    {
        const string url = "https://api.palne.shop/api/auth/email/confirm?token=EXAMPLETOKEN";
        var message = PendingEmailChange.BuildConfirmationEmail(url);

        message.Subject.Should().Be(PendingEmailChange.ConfirmSubject);
        message.HtmlBody.Should().Contain("Confirm email address"); // CTA label
        message.HtmlBody.Should().Contain(url);                     // bulletproof button + raw fallback link
        message.HtmlBody.Should().Contain(EmailBrand.Accent);       // brand-green button fill
        message.TextBody.Should().Contain(url);
        message.InlineImages.Should().ContainSingle(i => i.ContentId == "fuelflow-lion");
    }

    [Fact]
    public void EmailChangedNotice_NamesTheNewAddress()
    {
        var message = ConfirmEmailChangeCommandHandler.BuildEmailChangedNotice("new@palne.shop");

        message.HtmlBody.Should().Contain("changed");
        message.HtmlBody.Should().Contain("new@palne.shop");
        message.TextBody.Should().Contain("new@palne.shop");
        message.InlineImages.Should().ContainSingle(i => i.ContentId == "fuelflow-lion");
    }

    [Fact]
    public void SmtpEmailSender_BuildsMultipartMime_WithConfigurableFromName_AndReplyTo()
    {
        var sender = new SmtpEmailSender(
            Options.Create(new SupportMailOptions
            {
                Host = "smtp.test",
                Port = 587,
                Username = "sender@palne.shop",
                Password = "app-password",
                FromName = "FuelFlow",
                ReplyToEmail = "support@palne.shop",
            }),
            NullLogger<SmtpEmailSender>.Instance);

        var message = BrandedEmailLayout.BuildMessage("Subj", "text body", SampleContent());
        var mime = sender.BuildMimeMessage("dest@example.com", message);

        // From address stays the authenticated username (Gmail requirement); display name is configurable.
        var from = mime.From.OfType<MailboxAddress>().Single();
        from.Address.Should().Be("sender@palne.shop");
        from.Name.Should().Be("FuelFlow");
        mime.To.OfType<MailboxAddress>().Single().Address.Should().Be("dest@example.com");
        mime.ReplyTo.OfType<MailboxAddress>().Single().Address.Should().Be("support@palne.shop");
        mime.Subject.Should().Be("Subj");

        // multipart/alternative carries BOTH the HTML and the retained plain-text alternative ...
        var html = mime.BodyParts.OfType<TextPart>().Single(p => p.IsHtml);
        var text = mime.BodyParts.OfType<TextPart>().Single(p => p.ContentType.MimeType == "text/plain");
        html.Text.Should().Contain("FUELFLOW");
        text.Text.Should().Be("text body");

        // ... and the lion rides along as an inline linked resource addressable by cid.
        mime.BodyParts.OfType<MimePart>().Any(p => p.ContentId == "fuelflow-lion").Should().BeTrue();
    }

    [Fact]
    public void SmtpEmailSender_OmitsReplyTo_WhenNotConfigured()
    {
        var sender = new SmtpEmailSender(
            Options.Create(new SupportMailOptions
            {
                Host = "smtp.test",
                Port = 587,
                Username = "sender@palne.shop",
                Password = "app-password",
                FromName = "FuelFlow",
            }),
            NullLogger<SmtpEmailSender>.Instance);

        var mime = sender.BuildMimeMessage(
            "dest@example.com",
            BrandedEmailLayout.BuildMessage("s", "t", SampleContent()));

        mime.ReplyTo.Count.Should().Be(0);
    }

    /// <summary>Extracts the hidden preheader text from a rendered branded email so a test can assert
    /// what does - and does not - leak into the inbox preview.</summary>
    private static string Preheader(string html)
    {
        const string marker = "color:transparent;\">";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        start += marker.Length;
        var end = html.IndexOf("</div>", start, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }
}
