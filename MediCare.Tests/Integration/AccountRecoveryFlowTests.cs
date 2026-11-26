using System.Net;
using System.Text.RegularExpressions;

namespace MediCare.Tests.Integration;

/// <summary>Staff password reset of a patient, end to end, through the real forms and the security-stamp middleware.</summary>
[Collection(PageWorldCollection.Name)]
public sealed partial class AccountRecoveryFlowTests(PageWorldFixture world)
{
    private const string NewPassword = "PageTests-Changed-Pa55word!";

    [Fact]
    public async Task Reset_by_reception_signs_the_patient_out_and_forces_a_password_change_at_next_sign_in()
    {
        var patient = await world.Data.CreatePatientAsync();
        var sessionBeforeReset = await PageSession.SignInAsync(world.Factory, patient.PhoneNumber, patient.Password);
        var before = await sessionBeforeReset.GetAsync("/Appointments/Mine");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var reception = await PageSession.SignInAsync(world.Factory, world.Receptionist.UserName, world.Receptionist.Password);
        var confirmPath = $"/Reception/ConfirmResetPatientPassword/{patient.PatientId}";
        await reception.GetOkHtmlAsync(confirmPath);
        var resetResponse = await reception.PostFormAsync($"/Reception/ResetPatientPassword/{patient.PatientId}", confirmPath, []);
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        var temporaryPassword = WebUtility.HtmlDecode(TemporaryPasswordRegex().Match(await resetResponse.Content.ReadAsStringAsync()).Groups[1].Value);
        Assert.NotEmpty(temporaryPassword);

        // This seeded patient may have no email; when one is on file the same password is mailed to it.
        var resetPage = await resetResponse.Content.ReadAsStringAsync();
        Assert.True(resetPage.Contains("nothing was emailed", StringComparison.Ordinal) || resetPage.Contains("We also emailed it to", StringComparison.Ordinal));

        // The security stamp changed, so the earlier session is signed out on its next request.
        var staleSession = await sessionBeforeReset.GetAsync("/Appointments/Mine");
        Assert.Equal(HttpStatusCode.Redirect, staleSession.StatusCode);
        Assert.StartsWith("/Account/Login", PageSession.LocationPath(staleSession), StringComparison.Ordinal);

        // Signing in with the temporary password works, but every page then redirects to the change-password page.
        var newSession = await PageSession.SignInAsync(world.Factory, patient.PhoneNumber, temporaryPassword);
        var forced = await newSession.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, forced.StatusCode);
        Assert.Equal("/Account/ChangePassword", PageSession.LocationPath(forced));

        var changePath = "/Account/ChangePassword";
        await newSession.GetOkHtmlAsync(changePath);
        var changed = await newSession.PostFormAsync(
            changePath,
            changePath,
            [new("CurrentPassword", temporaryPassword), new("NewPassword", NewPassword), new("ConfirmPassword", NewPassword)]);
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Equal("/", PageSession.LocationPath(changed));

        var afterChange = await newSession.GetAsync("/Appointments/Mine");
        Assert.Equal(HttpStatusCode.OK, afterChange.StatusCode);

        // The new password is the one that works from now on; the temporary one no longer does.
        var again = await PageSession.SignInAsync(world.Factory, patient.PhoneNumber, NewPassword);
        var page = await again.GetAsync("/Appointments/Mine");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
    }

    [GeneratedRegex(@"Temporary password</dt>\s*<dd[^>]*><code>([^<]+)</code>")]
    private static partial Regex TemporaryPasswordRegex();
}
