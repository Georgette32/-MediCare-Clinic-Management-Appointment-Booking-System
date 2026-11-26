using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Infrastructure.Email;

/// <summary>SMTP settings from the <c>Email</c> section. The password belongs only in appsettings.Production.json or user secrets.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = "medicare58.runasp.net@gmail.com";
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "medicare58.runasp.net@gmail.com";
    public string FromName { get; set; } = "MediCare";

    /// <summary>True when a password is configured, so real email can be sent.</summary>
    public bool CanSend => !string.IsNullOrWhiteSpace(Password);
}

/// <summary>An image shown inside the HTML body and referenced as <c>cid:ContentId</c>.</summary>
public sealed record EmailInlineImage(string ContentId, string MediaType, byte[] Content);

public sealed record EmailMessage(
    string ToAddress,
    string ToName,
    string Subject,
    string HtmlBody,
    string TextBody,
    IReadOnlyList<EmailInlineImage> InlineImages,
    bool Sensitive = false);

public interface IEmailSender
{
    /// <summary>Sends the message. Throws when the mail server refuses it or cannot be reached.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Sends through SMTP with STARTTLS (Gmail: smtp.gmail.com:587 and an App Password).</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = message.Subject,
            SubjectEncoding = System.Text.Encoding.UTF8,
            BodyEncoding = System.Text.Encoding.UTF8
        };
        mail.To.Add(new MailAddress(message.ToAddress, message.ToName));

        // Plain text first, HTML last: mail apps show the last alternative they can render.
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.TextBody, System.Text.Encoding.UTF8, MediaTypeNames.Text.Plain));
        var html = AlternateView.CreateAlternateViewFromString(message.HtmlBody, System.Text.Encoding.UTF8, MediaTypeNames.Text.Html);
        foreach (var image in message.InlineImages)
        {
            var resource = new LinkedResource(new MemoryStream(image.Content), image.MediaType)
            {
                ContentId = image.ContentId,
                TransferEncoding = TransferEncoding.Base64
            };
            html.LinkedResources.Add(resource);
        }
        mail.AlternateViews.Add(html);

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(settings.UserName, settings.Password),
            Timeout = 20_000
        };
        await client.SendMailAsync(mail, cancellationToken);
    }
}

/// <summary>
/// Used when no SMTP password is configured (local development): writes the message to the log, so the code can be
/// read from the console instead of an inbox.
/// </summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        // A temporary password must never reach a log, so report the failure instead.
        if (message.Sensitive)
            throw new InvalidOperationException("Email is not configured; this message is not written to the log.");
        logger.LogWarning(
            "Email not sent (no Email:Password configured). To: {To}. Subject: {Subject}.\n{Body}",
            message.ToAddress, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}
