using System.Net;
using System.Net.Mail;
using MediCare.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Infrastructure.Email;

public sealed class EmailOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "no-reply@medicare.local";

    public string FromName { get; set; } = "MediCare";

    /// <summary>When no SMTP host is configured, messages are written to this folder for local development.</summary>
    public bool SaveToDisk { get; set; } = true;

    public string? PickupDirectory { get; set; }
}

public sealed class EmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailOptions> options, ILogger<EmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            await SaveToDiskAsync(to, subject, htmlBody, cancellationToken);
            _logger.LogInformation(
                "Email (development mode) to {Email}: {Subject}{NewLine}{Body}",
                to,
                subject,
                Environment.NewLine,
                htmlBody);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(to));

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl
        };

        if (!string.IsNullOrWhiteSpace(_options.UserName))
        {
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }

    private async Task SaveToDiskAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        if (!_options.SaveToDisk)
        {
            return;
        }

        try
        {
            var directory = _options.PickupDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "emails");
            }

            Directory.CreateDirectory(directory);
            var safeTo = string.Concat(to.Where(c => char.IsLetterOrDigit(c) || c is '@' or '.' or '-' or '_'));
            var fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{safeTo}_{Guid.NewGuid():N}.html";
            var content = $"<!-- To: {WebUtility.HtmlEncode(to)} | Subject: {WebUtility.HtmlEncode(subject)} -->{Environment.NewLine}{htmlBody}";
            await File.WriteAllTextAsync(Path.Combine(directory, fileName), content, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save the development email to disk.");
        }
    }
}
