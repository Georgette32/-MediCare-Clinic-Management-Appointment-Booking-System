using System.Globalization;
using System.Net;
using System.Text;

namespace MediCare.Infrastructure.Email;

/// <summary>
/// The branded "verify your email" message. Table layout with inline styles so it renders the same in Gmail, Outlook
/// and Apple Mail; the logo and clock are inline PNG attachments (Gmail does not show SVG).
/// </summary>
public static class VerificationEmail
{
    public const string MarkContentId = "mc-mark";
    public const string ClockContentId = "mc-clock";

    private const string Font = "'Readex Pro','Segoe UI',Helvetica,Arial,sans-serif";

    public static EmailMessage Build(string fullName, string code, DateTimeOffset expiresAtClinicTime, string toAddress, string siteUrl)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
            throw new ArgumentException("The code must be 6 digits.", nameof(code));

        var firstName = FirstName(fullName);
        var expiry = expiresAtClinicTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        var site = siteUrl.TrimEnd('/');
        var siteLabel = new Uri(site).Host;
        var spaced = $"{code[..3]} {code[3..]}";

        var subject = $"{spaced} is your MediCare verification code";
        var text =
            $"Hi {firstName},\n\n" +
            $"Your MediCare verification code is: {spaced}\n\n" +
            $"Enter it on the MediCare page you just opened to finish creating your patient account. " +
            $"It expires in 10 minutes (at {expiry} Cairo time).\n\n" +
            "Keep this code to yourself. MediCare staff will never ask for it by phone, WhatsApp or email.\n" +
            "Didn't try to sign up? You can ignore this email. The account stays locked until someone enters this code.\n\n" +
            $"MediCare · {site}\n";

        var html = Html(Encode(firstName), code, expiry, Encode(toAddress), Encode(site), Encode(siteLabel), Encode(spaced));
        return new EmailMessage(toAddress, fullName, subject, html, text,
        [
            new EmailInlineImage(MarkContentId, "image/png", Asset("mark.png")),
            new EmailInlineImage(ClockContentId, "image/png", Asset("clock.png"))
        ]);
    }

    public static string FirstName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : "there";
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Html(string firstName, string code, string expiry, string toAddress, string site, string siteLabel, string spaced)
    {
        var digits = new StringBuilder();
        for (var index = 0; index < code.Length; index++)
        {
            if (index == 3)
                digits.Append("<td style=\"width:22px;text-align:center;color:#6a7fa0;font-size:22px;\">&ndash;</td>");
            else if (index > 0)
                digits.Append("<td style=\"width:8px;\"></td>");
            digits.Append($"<td style=\"width:46px;height:58px;background:#ffffff;border:1px solid #b9cdea;border-radius:10px;text-align:center;font-size:30px;line-height:58px;font-weight:600;color:#0a1f44;font-family:{Font};\">{code[index]}</td>");
        }

        return $$"""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="light only">
<title>Your MediCare verification code</title>
</head>
<body style="margin:0;padding:0;background:#eef3fa;">
<div style="display:none;max-height:0;overflow:hidden;opacity:0;">Your MediCare code is {{spaced}}. It expires in 10 minutes.</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#eef3fa;">
<tr><td align="center" style="padding:40px 16px;">
  <table role="presentation" width="560" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:560px;background:#ffffff;border:1px solid #d2dded;border-radius:16px;overflow:hidden;font-family:{{Font}};color:#0b1f3f;">
    <tr><td style="background:#0a1f44;padding:26px 40px;">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
        <td style="vertical-align:middle;">
          <img src="cid:{{MarkContentId}}" width="36" height="36" alt="MediCare" style="display:inline-block;vertical-align:middle;border:0;">
          <span style="display:inline-block;vertical-align:middle;margin-left:10px;font-size:22px;line-height:28px;font-weight:600;color:#ffffff;letter-spacing:-0.01em;">Medi<span style="color:#2ec4c6;">Care</span></span>
        </td>
        <td align="right" style="vertical-align:middle;font-size:12px;line-height:16px;color:#aebfdc;letter-spacing:0.04em;">Email verification</td>
      </tr></table>
    </td></tr>
    <tr><td style="height:4px;line-height:4px;font-size:0;background:#1456a8;background-image:linear-gradient(90deg,#1456a8,#2ec4c6);">&nbsp;</td></tr>
    <tr><td style="padding:40px 40px 8px;">
      <h1 style="margin:0 0 14px;font-size:26px;line-height:34px;font-weight:600;color:#0b1f3f;letter-spacing:-0.01em;">Here&rsquo;s your verification code</h1>
      <p style="margin:0;font-size:15px;line-height:24px;color:#4b5f80;">Hi {{firstName}}, enter this code on the MediCare page you just opened to finish creating your patient account.</p>
    </td></tr>
    <tr><td style="padding:28px 40px 0;">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#dce8fa;border-radius:12px;">
        <tr><td align="center" style="padding:26px 12px 22px;">
          <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>{{digits}}</tr></table>
          <p style="margin:18px 0 0;">
            <span style="display:inline-block;padding:6px 12px;border-radius:999px;background:#fdf0d3;color:#7d4d00;font-size:13px;line-height:18px;font-weight:600;"><img src="cid:{{ClockContentId}}" width="14" height="14" alt="" style="display:inline-block;vertical-align:-2px;border:0;">&nbsp; Expires in 10 minutes &middot; at {{expiry}} Cairo time</span>
          </p>
        </td></tr>
      </table>
    </td></tr>
    <tr><td style="padding:24px 40px 0;">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#f5f8fd;border:1px solid #d2dded;border-radius:10px;">
        <tr><td style="padding:14px 18px;font-size:13px;line-height:20px;color:#4b5f80;">
          <strong style="color:#0b1f3f;">Keep this code to yourself.</strong> MediCare staff will never ask for it by phone, WhatsApp or email.
        </td></tr>
      </table>
    </td></tr>
    <tr><td style="padding:24px 40px 36px;font-size:13px;line-height:20px;color:#4b5f80;">
      Didn&rsquo;t try to sign up? You can ignore this email. The account stays locked until someone enters this code.
    </td></tr>
    <tr><td style="border-top:1px solid #d2dded;padding:18px 40px;background:#fbfcfe;">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
        <td style="font-size:12px;line-height:18px;color:#4b5f80;">Sent to <span style="color:#0b1f3f;">{{toAddress}}</span></td>
        <td align="right" style="font-size:12px;line-height:18px;"><a href="{{site}}" style="color:#1456a8;text-decoration:none;font-weight:600;">{{siteLabel}}</a></td>
      </tr></table>
    </td></tr>
  </table>
  <p style="margin:20px 0 0;font-family:{{Font}};font-size:12px;line-height:18px;color:#6a7fa0;text-align:center;">MediCare &middot; Care that runs on time.<br>This is an automated message; replies aren&rsquo;t read.</p>
</td></tr>
</table>
</body>
</html>
""";
    }

    private static byte[] Asset(string name)
    {
        using var stream = typeof(VerificationEmail).Assembly.GetManifestResourceStream($"MediCare.Infrastructure.Email.Assets.{name}")
            ?? throw new InvalidOperationException($"Embedded email asset '{name}' is missing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
