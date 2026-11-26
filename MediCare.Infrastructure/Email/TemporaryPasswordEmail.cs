using System.Net;

namespace MediCare.Infrastructure.Email;

/// <summary>
/// The branded "your temporary password" message sent after reception resets a patient password. It carries no username
/// or phone number, so the password alone is not enough to sign in.
/// </summary>
public static class TemporaryPasswordEmail
{
    private const string Font = "'Readex Pro','Segoe UI',Helvetica,Arial,sans-serif";

    public static EmailMessage Build(string fullName, string temporaryPassword, string toAddress, string siteUrl)
    {
        var firstName = VerificationEmail.FirstName(fullName);
        var site = siteUrl.TrimEnd('/');
        var signIn = site + "/Account/Login";

        var text =
            $"Hi {firstName},\n\n" +
            "The clinic reception reset your MediCare password. Your temporary password is:\n\n" +
            $"{temporaryPassword}\n\n" +
            $"Sign in with your mobile number at {signIn}. You will be asked to choose a new password straight away.\n\n" +
            "Didn't ask for this? Contact the clinic reception.\n\nMediCare\n";

        var html = $$"""
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="color-scheme" content="light only"><title>Your temporary password</title></head>
<body style="margin:0;padding:0;background:#eef3fa;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#eef3fa;"><tr><td align="center" style="padding:40px 16px;">
  <table role="presentation" width="560" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:560px;background:#ffffff;border:1px solid #d2dded;border-radius:16px;overflow:hidden;font-family:{{Font}};color:#0b1f3f;">
    <tr><td style="background:#0a1f44;padding:26px 40px;">
      <img src="cid:{{VerificationEmail.MarkContentId}}" width="36" height="36" alt="MediCare" style="display:inline-block;vertical-align:middle;border:0;">
      <span style="display:inline-block;vertical-align:middle;margin-left:10px;font-size:22px;line-height:28px;font-weight:600;color:#ffffff;">Medi<span style="color:#2ec4c6;">Care</span></span>
    </td></tr>
    <tr><td style="height:4px;line-height:4px;font-size:0;background:#1456a8;background-image:linear-gradient(90deg,#1456a8,#2ec4c6);">&nbsp;</td></tr>
    <tr><td style="padding:40px 40px 0;">
      <h1 style="margin:0 0 14px;font-size:26px;line-height:34px;font-weight:600;">Your temporary password</h1>
      <p style="margin:0;font-size:15px;line-height:24px;color:#4b5f80;">Hi {{Encode(firstName)}}, the clinic reception reset your MediCare password.</p>
    </td></tr>
    <tr><td style="padding:24px 40px 0;">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#dce8fa;border-radius:12px;"><tr><td align="center" style="padding:22px 12px;font-family:Consolas,'Courier New',monospace;font-size:20px;line-height:28px;font-weight:700;color:#0a1f44;word-break:break-all;">{{Encode(temporaryPassword)}}</td></tr></table>
    </td></tr>
    <tr><td style="padding:24px 40px 0;font-size:15px;line-height:24px;color:#4b5f80;">Sign in with your mobile number. You will be asked to choose a new password straight away.</td></tr>
    <tr><td style="padding:22px 40px 0;"><a href="{{Encode(signIn)}}" style="display:inline-block;background:#1456a8;color:#ffffff;text-decoration:none;font-weight:600;font-size:15px;padding:12px 22px;border-radius:10px;">Sign in to MediCare</a></td></tr>
    <tr><td style="padding:24px 40px 36px;font-size:13px;line-height:20px;color:#4b5f80;">Didn&rsquo;t ask for this? Contact the clinic reception.</td></tr>
  </table>
  <p style="margin:20px 0 0;font-family:{{Font}};font-size:12px;line-height:18px;color:#6a7fa0;">MediCare &middot; This is an automated message; replies aren&rsquo;t read.</p>
</td></tr></table>
</body>
</html>
""";

        return new EmailMessage(toAddress, fullName, "Your MediCare temporary password", html, text,
            [new EmailInlineImage(VerificationEmail.MarkContentId, "image/png", VerificationEmail.Asset("mark.png"))],
            Sensitive: true);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
