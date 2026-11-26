using System.Net;
using System.Text.Json;

namespace MediCare.Tests.Integration;

/// <summary>Self sign-up with the emailed code, end to end through the real forms, JSON actions and session middleware.</summary>
[Collection(PageWorldCollection.Name)]
public sealed class EmailVerificationFlowTests(PageWorldFixture world)
{
    private const string Password = "PageTests-Signup-Pa55word!";
    private const string VerifyPath = "/Account/VerifyEmail";

    private async Task<(PageSession Session, string Email, string Phone)> RegisterAsync()
    {
        var (phone, _) = PageTestData.NewEgyptianPhone();
        var email = $"signup-{Guid.NewGuid():N}@example.invalid";
        var session = PageSession.Anonymous(world.Factory);
        var response = await session.PostFormAsync("/Account/Register", "/Account/Register",
        [
            new("FullName", "Salma Signup"), new("PhoneNumber", phone), new("Email", email),
            new("Password", Password), new("ConfirmPassword", Password)
        ]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(VerifyPath, PageSession.LocationPath(response));
        return (session, email, phone);
    }

    private static async Task<JsonElement> PostFetchAsync(PageSession session, string path, string? field = null, string? value = null)
    {
        var token = await session.AntiForgeryTokenFromAsync(VerifyPath);
        var fields = field is null ? [] : new[] { new KeyValuePair<string, string>(field, value ?? string.Empty) };
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("X-Requested-With", "fetch");
        request.Headers.Add("RequestVerificationToken", token);
        var response = await session.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Sign_up_lands_on_the_verify_page_and_the_right_code_opens_the_app_without_signing_in_again()
    {
        var (session, email, _) = await RegisterAsync();

        var html = await session.GetOkHtmlAsync(VerifyPath);
        Assert.Contains("Check your email", html);
        Assert.Contains("@example.invalid", html);                       // masked address
        Assert.DoesNotContain(email, html);
        Assert.Single(world.Factory.Emails.Sent, message => message.ToAddress == email);   // reload does not resend

        // Every other page is closed until the code is entered.
        var blocked = await session.GetAsync("/Appointments/Mine");
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Equal(VerifyPath, PageSession.LocationPath(blocked));

        var code = world.Factory.Emails.LatestCodeFor(email);
        var wrong = await PostFetchAsync(session, VerifyPath, "code", code == "000000" ? "111111" : "000000");
        Assert.Equal("wrong", wrong.GetProperty("status").GetString());
        Assert.Equal(4, wrong.GetProperty("triesLeft").GetInt32());

        var right = await PostFetchAsync(session, VerifyPath, "code", code);
        Assert.Equal("ok", right.GetProperty("status").GetString());

        // Same session, no second sign-in.
        var open = await session.GetAsync("/Appointments/Mine");
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var verifyAgain = await session.GetAsync(VerifyPath);
        Assert.Equal(HttpStatusCode.Redirect, verifyAgain.StatusCode);
    }

    [Fact]
    public async Task A_pending_account_that_signs_in_later_returns_to_the_verify_page()
    {
        var (_, _, phone) = await RegisterAsync();

        var later = await PageSession.SignInAsync(world.Factory, phone, Password);
        var home = await later.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.Equal(VerifyPath, PageSession.LocationPath(home));
    }

    [Fact]
    public async Task Resend_waits_a_minute_and_change_email_refuses_an_address_already_in_use()
    {
        var (session, email, _) = await RegisterAsync();

        var resend = await PostFetchAsync(session, "/Account/ResendCode");
        Assert.Equal("cooldown", resend.GetProperty("status").GetString());
        Assert.InRange(resend.GetProperty("cooldown").GetInt32(), 1, 60);

        var taken = await PostFetchAsync(session, "/Account/ChangeEmail", "email", MediCareWebApplicationFactory.AdminEmail);
        Assert.Equal("error", taken.GetProperty("status").GetString());
        Assert.Single(world.Factory.Emails.Sent, message => message.ToAddress == email);
    }

    [Fact]
    public async Task Patients_created_before_verification_existed_sign_in_as_before()
    {
        var patient = world.PatientWithoutAppointments;
        var session = await PageSession.SignInAsync(world.Factory, patient.PhoneNumber, patient.Password);
        var page = await session.GetAsync("/Appointments/Mine");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
    }

    [Fact]
    public async Task Sign_up_without_an_email_is_refused()
    {
        var (phone, _) = PageTestData.NewEgyptianPhone();
        var session = PageSession.Anonymous(world.Factory);
        var response = await session.PostFormAsync("/Account/Register", "/Account/Register",
        [
            new("FullName", "No Email"), new("PhoneNumber", phone), new("Email", ""),
            new("Password", Password), new("ConfirmPassword", Password)
        ]);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Enter your email address", await response.Content.ReadAsStringAsync());
    }
}
