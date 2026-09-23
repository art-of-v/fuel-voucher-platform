using System.Net;
using System.Text;

namespace FuelFlow.SharedKernel.Notifications.Email;

/// <summary>
/// The single reusable branded email layout: a dark, ~600px, table-based, fully inline-styled shell
/// (email-client-safe — no external CSS, no web fonts) with the neon-green cyber-lion header, an
/// optional code block or CTA button, and a small footer. Every transactional template renders
/// through <see cref="BuildMessage"/>, which pairs the HTML with the caller's retained plain-text
/// fallback and embeds the lion inline (<c>cid:</c>).
/// </summary>
internal static class BrandedEmailLayout
{
    /// <summary>Content slots a template fills. Dynamic values are HTML-encoded on render, so callers
    /// pass plain text (and a raw URL) here.</summary>
    internal sealed record Content
    {
        /// <summary>Hidden inbox-preview text.</summary>
        public required string Preheader { get; init; }
        public required string Heading { get; init; }
        public IReadOnlyList<string> Paragraphs { get; init; } = [];
        /// <summary>A large, spaced code block (e.g. the OTP). Null = none.</summary>
        public string? Code { get; init; }
        public string? CtaLabel { get; init; }
        public string? CtaUrl { get; init; }
        /// <summary>Copy shown above the raw fallback link when a CTA is present.</summary>
        public string? CtaHint { get; init; }
        /// <summary>Quiet closing note above the footer.</summary>
        public string? Footnote { get; init; }
    }

    public static EmailMessage BuildMessage(string subject, string textBody, Content content)
    {
        var logo = EmailBrand.LionLogo();
        return new EmailMessage
        {
            Subject = subject,
            HtmlBody = RenderHtml(content, hasLogo: logo is not null),
            TextBody = textBody,
            InlineImages = logo is null ? [] : [logo],
        };
    }

    private static string RenderHtml(Content c, bool hasLogo)
    {
        static string Enc(string value) => WebUtility.HtmlEncode(value);

        var sb = new StringBuilder(2048);
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
          .Append("<meta name=\"color-scheme\" content=\"dark light\">")
          .Append("<meta name=\"supported-color-schemes\" content=\"dark light\">")
          .Append("<title>").Append(Enc(c.Heading)).Append("</title></head>")
          .Append("<body style=\"margin:0;padding:0;background:").Append(EmailBrand.Canvas).Append(";\">");

        // Hidden preheader (+ zero-width padding so body copy doesn't bleed into the preview).
        sb.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;\">")
          .Append(Enc(c.Preheader))
          .Append(string.Concat(Enumerable.Repeat("&#847;&zwnj;&nbsp;", 20)))
          .Append("</div>");

        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background:")
          .Append(EmailBrand.Canvas).Append(";\"><tr><td align=\"center\" style=\"padding:28px 12px;\">")
          .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;background:")
          .Append(EmailBrand.Surface).Append(";border:1px solid ").Append(EmailBrand.Border)
          .Append(";border-radius:16px;overflow:hidden;\">");

        // Header: inline lion (falls back to the wordmark alone when the asset is unavailable).
        if (hasLogo)
        {
            sb.Append("<tr><td align=\"center\" style=\"padding:36px 32px 0;\">")
              .Append("<img src=\"cid:").Append(EmailBrand.LogoContentId)
              .Append("\" width=\"88\" height=\"88\" alt=\"FuelFlow\" style=\"display:block;width:88px;height:88px;border:0;outline:none;text-decoration:none;\"></td></tr>");
        }
        sb.Append("<tr><td align=\"center\" style=\"padding:14px 32px 0;\"><span style=\"font-family:")
          .Append(EmailBrand.FontStack).Append(";font-size:13px;font-weight:700;letter-spacing:0.42em;color:")
          .Append(EmailBrand.AccentBright).Append(";\">FUELFLOW</span></td></tr>");

        // Body card.
        sb.Append("<tr><td style=\"padding:28px 32px 8px;\">")
          .Append("<h1 style=\"margin:0 0 14px;font-family:").Append(EmailBrand.FontStack)
          .Append(";font-size:22px;line-height:1.3;font-weight:700;color:").Append(EmailBrand.TextPrimary)
          .Append(";\">").Append(Enc(c.Heading)).Append("</h1>");

        foreach (var p in c.Paragraphs)
        {
            sb.Append("<p style=\"margin:0 0 14px;font-family:").Append(EmailBrand.FontStack)
              .Append(";font-size:15px;line-height:1.6;color:").Append(EmailBrand.TextSecondary)
              .Append(";\">").Append(Enc(p)).Append("</p>");
        }

        if (!string.IsNullOrEmpty(c.Code))
        {
            sb.Append("<div style=\"margin:20px 0 8px;padding:18px;background:").Append(EmailBrand.SurfaceInset)
              .Append(";border:1px solid ").Append(EmailBrand.Border)
              .Append(";border-radius:12px;text-align:center;\">")
              .Append("<span style=\"font-family:'SFMono-Regular',Consolas,'Liberation Mono',Menlo,monospace;")
              .Append("font-size:34px;font-weight:700;letter-spacing:0.32em;text-indent:0.32em;color:")
              .Append(EmailBrand.AccentBright).Append(";\">").Append(Enc(c.Code)).Append("</span></div>");
        }

        if (!string.IsNullOrEmpty(c.CtaLabel) && !string.IsNullOrEmpty(c.CtaUrl))
        {
            var url = Enc(c.CtaUrl);
            sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:22px auto 6px;\"><tr>")
              .Append("<td align=\"center\" bgcolor=\"").Append(EmailBrand.Accent).Append("\" style=\"border-radius:10px;\">")
              .Append("<a href=\"").Append(url).Append("\" target=\"_blank\" style=\"display:inline-block;padding:14px 30px;font-family:")
              .Append(EmailBrand.FontStack).Append(";font-size:15px;font-weight:700;color:").Append(EmailBrand.OnAccent)
              .Append(";text-decoration:none;border-radius:10px;\">").Append(Enc(c.CtaLabel)).Append("</a></td></tr></table>");

            if (!string.IsNullOrEmpty(c.CtaHint))
            {
                sb.Append("<p style=\"margin:14px 0 0;font-family:").Append(EmailBrand.FontStack)
                  .Append(";font-size:13px;line-height:1.6;color:").Append(EmailBrand.TextMuted).Append(";\">")
                  .Append(Enc(c.CtaHint)).Append("<br><a href=\"").Append(url).Append("\" target=\"_blank\" style=\"color:")
                  .Append(EmailBrand.Accent).Append(";text-decoration:underline;word-break:break-all;\">")
                  .Append(url).Append("</a></p>");
            }
        }
        sb.Append("</td></tr>");

        // Footer.
        sb.Append("<tr><td style=\"padding:22px 32px 30px;border-top:1px solid ").Append(EmailBrand.Border).Append(";\">");
        if (!string.IsNullOrEmpty(c.Footnote))
        {
            sb.Append("<p style=\"margin:0 0 8px;font-family:").Append(EmailBrand.FontStack)
              .Append(";font-size:12px;line-height:1.5;color:").Append(EmailBrand.TextMuted)
              .Append(";text-align:center;\">").Append(Enc(c.Footnote)).Append("</p>");
        }
        sb.Append("<p style=\"margin:0;font-family:").Append(EmailBrand.FontStack)
          .Append(";font-size:12px;line-height:1.5;color:").Append(EmailBrand.TextMuted)
          .Append(";text-align:center;\">FuelFlow &middot; palne.shop</p>")
          .Append("</td></tr></table></td></tr></table></body></html>");

        return sb.ToString();
    }
}
