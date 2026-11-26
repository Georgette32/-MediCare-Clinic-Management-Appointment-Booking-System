using System.Text;
using MediCare.Infrastructure.Email;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Filters;
using Microsoft.AspNetCore.Http;

namespace MediCare.Tests;

public sealed class EmailVerificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationUser Pending() => new()
    {
        Id = Guid.NewGuid(),
        FullName = "Nour Mohamed",
        Email = "nour.mohamed@gmail.com",
        RequiresEmailVerification = true
    };

    [Fact]
    public void Issue_stores_only_a_hash_with_a_ten_minute_expiry()
    {
        var user = Pending();
        var code = EmailCodeRules.Issue(user, Now);

        Assert.Matches("^[0-9]{6}$", code);
        Assert.NotNull(user.EmailCodeHash);
        Assert.Equal(64, user.EmailCodeHash!.Length);
        Assert.DoesNotContain(code, user.EmailCodeHash);
        Assert.Equal(Now.AddMinutes(10), user.EmailCodeExpiresAtUtc);
        Assert.Equal(Now, user.EmailCodeSentAtUtc);
        Assert.Equal(0, user.EmailCodeFailedAttempts);
    }

    [Fact]
    public void Resend_waits_sixty_seconds()
    {
        var user = Pending();
        EmailCodeRules.Issue(user, Now);

        Assert.Equal(60, EmailCodeRules.CooldownSecondsLeft(user, Now));
        Assert.Equal(15, EmailCodeRules.CooldownSecondsLeft(user, Now.AddSeconds(45)));
        Assert.Equal(0, EmailCodeRules.CooldownSecondsLeft(user, Now.AddSeconds(60)));
    }

    [Fact]
    public void Correct_code_confirms_the_email_and_clears_the_code()
    {
        var user = Pending();
        var code = EmailCodeRules.Issue(user, Now);

        var result = EmailCodeRules.Check(user, code[..3] + " " + code[3..], Now.AddMinutes(9));

        Assert.Equal(EmailCodeCheckStatus.Confirmed, result.Status);
        Assert.True(user.EmailConfirmed);
        Assert.False(EmailCodeRules.IsPending(user));
        Assert.Null(user.EmailCodeHash);
        Assert.Null(user.EmailCodeExpiresAtUtc);
    }

    [Fact]
    public void Wrong_codes_count_down_and_the_fifth_ends_the_code_even_for_the_right_one_after()
    {
        var user = Pending();
        var code = EmailCodeRules.Issue(user, Now);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var left = 4; left >= 1; left--)
            Assert.Equal(new EmailCodeCheck(EmailCodeCheckStatus.Wrong, left), EmailCodeRules.Check(user, wrong, Now));
        Assert.Equal(EmailCodeCheckStatus.Locked, EmailCodeRules.Check(user, wrong, Now).Status);

        Assert.Equal(EmailCodeCheckStatus.Locked, EmailCodeRules.Check(user, code, Now).Status);
        Assert.False(user.EmailConfirmed);
        Assert.False(EmailCodeRules.HasLiveCode(user, Now));
    }

    [Fact]
    public void Code_expires_after_ten_minutes()
    {
        var user = Pending();
        var code = EmailCodeRules.Issue(user, Now);

        Assert.Equal(EmailCodeCheckStatus.Expired, EmailCodeRules.Check(user, code, Now.AddMinutes(10)).Status);
        Assert.False(user.EmailConfirmed);
    }

    [Fact]
    public void A_new_code_replaces_the_old_one()
    {
        var user = Pending();
        var first = EmailCodeRules.Issue(user, Now);
        string second;
        do second = EmailCodeRules.Issue(user, Now.AddMinutes(2)); while (second == first);

        Assert.Equal(EmailCodeCheckStatus.Wrong, EmailCodeRules.Check(user, first, Now.AddMinutes(3)).Status);
        Assert.Equal(EmailCodeCheckStatus.Confirmed, EmailCodeRules.Check(user, second, Now.AddMinutes(3)).Status);
    }

    [Fact]
    public void Accounts_that_existed_before_the_feature_are_never_pending()
    {
        // Demo and older accounts: flag false (column default), email unconfirmed.
        var existing = new ApplicationUser { Id = Guid.NewGuid(), Email = "old@example.com", EmailConfirmed = false };
        Assert.False(EmailCodeRules.IsPending(existing));
        Assert.Equal(EmailCodeCheckStatus.NotPending, EmailCodeRules.Check(existing, "123456", Now).Status);
    }

    [Theory]
    [InlineData("nour.mohamed@gmail.com", "nour.m••••@gmail.com")]
    [InlineData("ab@x.com", "a••••@x.com")]
    [InlineData("a@x.com", "a••••@x.com")]
    public void Email_is_masked_on_screen(string email, string masked) =>
        Assert.Equal(masked, EmailCodeRules.Mask(email));

    [Theory]
    [InlineData("name@example.com", true)]
    [InlineData("name@localhost", false)]
    [InlineData("not an email", false)]
    [InlineData("", false)]
    public void Only_real_looking_addresses_are_accepted(string email, bool valid) =>
        Assert.Equal(valid, EmailCodeRules.IsValidAddress(email));

    [Fact]
    public void Email_carries_the_code_expiry_and_inline_brand_images()
    {
        var message = VerificationEmail.Build("Nour Mohamed", "482913",
            new DateTimeOffset(2026, 11, 26, 14, 32, 0, TimeSpan.FromHours(2)), "nour@example.com", "https://medicare58.runasp.net/");

        Assert.Equal("482 913 is your MediCare verification code", message.Subject);
        Assert.Contains("482 913", message.TextBody);
        Assert.Contains("Hi Nour,", message.TextBody);
        foreach (var digit in "482913")
            Assert.Contains($">{digit}</td>", message.HtmlBody);
        Assert.Contains("at 2:32 PM Cairo time", message.HtmlBody);
        Assert.Contains("nour@example.com", message.HtmlBody);
        Assert.Contains("cid:" + VerificationEmail.MarkContentId, message.HtmlBody);
        Assert.Contains("medicare58.runasp.net</a>", message.HtmlBody);
        Assert.All(message.InlineImages, image => Assert.Equal(0x89, image.Content[0]));   // PNG signature
    }

    [Fact]
    public void Email_encodes_the_patient_name()
    {
        var message = VerificationEmail.Build("<b>Eve</b>", "123456", Now, "eve@example.com", "https://medicare58.runasp.net");
        Assert.DoesNotContain("<b>Eve</b>", message.HtmlBody);
        Assert.Contains("&lt;b&gt;Eve&lt;/b&gt;", message.HtmlBody);
    }

    [Fact]
    public async Task Temporary_password_email_is_sensitive_and_never_reaches_the_log()
    {
        var message = TemporaryPasswordEmail.Build("Nour Mohamed", "Ab3$-temp<pass>", "nour@example.com", "https://medicare58.runasp.net/");

        Assert.True(message.Sensitive);
        Assert.Contains("Ab3$-temp<pass>", message.TextBody);
        Assert.Contains("Ab3$-temp&lt;pass&gt;", message.HtmlBody);
        Assert.Contains("https://medicare58.runasp.net/Account/Login", message.TextBody);

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<LogEmailSender>.Instance;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LogEmailSender(logger).SendAsync(message, CancellationToken.None));
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/Appointments/Mine", true)]
    [InlineData("/Account/Profile", true)]
    [InlineData("/Account/VerifyEmail", false)]
    [InlineData("/Account/ResendCode", false)]
    [InlineData("/Account/ChangeEmail", false)]
    [InlineData("/Account/Logout", false)]
    [InlineData("/css/medicare.css", false)]
    [InlineData("/js/verify-email.js", false)]
    [InlineData("/img/register-family.svg", false)]
    [InlineData("/fonts/readex-pro-latin.woff2", false)]
    public void Pending_accounts_are_sent_to_the_verify_page_from_everywhere_else(string path, bool redirect)
    {
        Assert.Equal(redirect, EmailVerificationPolicy.ShouldRedirect(true, false, new PathString(path)));
        Assert.False(EmailVerificationPolicy.ShouldRedirect(true, true, new PathString(path)));
        Assert.False(EmailVerificationPolicy.ShouldRedirect(false, false, new PathString(path)));
    }
}
