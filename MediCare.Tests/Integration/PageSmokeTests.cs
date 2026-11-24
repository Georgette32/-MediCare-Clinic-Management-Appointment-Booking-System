using System.Net;

namespace MediCare.Tests.Integration;

/// <summary>
/// Every main page for each role: 200 for an allowed role, a redirect to sign-in for anonymous visitors, and a redirect to
/// the access-denied page for a signed-in role that is not allowed. Placeholders are replaced from the shared world.
/// </summary>
[Collection(PageWorldCollection.Name)]
public sealed class PageSmokeTests(PageWorldFixture world)
{
    private const string LoginPath = "/Account/Login";
    private const string AccessDeniedPath = "/Account/AccessDenied";

    // Patient-only pages. Doctor is the signed-in role that is refused.
    [Theory]
    [InlineData("/Appointments/Mine")]
    [InlineData("/Appointments/Book?doctorId={doctor}&date={tomorrow}&time=09:00")]
    [InlineData("/Medical/History")]
    [InlineData("/Medical/ActivePrescriptions")]
    public async Task Patient_pages_render_for_patients_and_are_refused_to_doctors(string template)
    {
        var path = world.Route(template);
        var patient = await PageSession.SignInAsync(world.Factory, world.PatientWithAppointment.PhoneNumber, world.PatientWithAppointment.Password);
        await AssertOkAsync(patient, path);

        var doctor = await PageSession.SignInAsync(world.Factory, world.Doctor.UserName, world.Doctor.Password);
        await AssertRedirectAsync(doctor, path, AccessDeniedPath);
    }

    // Reception and Admin pages. Patient is the signed-in role that is refused.
    [Theory]
    [InlineData("/Reception")]
    [InlineData("/Reception/Queue?doctorId={doctor}")]
    [InlineData("/Reception/Appointments")]
    [InlineData("/Reception/Patients")]
    [InlineData("/Reception/PatientDetail/{patientA}")]
    [InlineData("/Reception/Extensions")]
    [InlineData("/Reception/Disruptions")]
    [InlineData("/Analytics/Live")]
    public async Task Reception_pages_render_for_receptionists_and_are_refused_to_patients(string template)
    {
        var path = world.Route(template);
        var reception = await PageSession.SignInAsync(world.Factory, world.Receptionist.UserName, world.Receptionist.Password);
        await AssertOkAsync(reception, path);

        var patient = await PageSession.SignInAsync(world.Factory, world.PatientWithAppointment.PhoneNumber, world.PatientWithAppointment.Password);
        await AssertRedirectAsync(patient, path, AccessDeniedPath);
    }

    // Doctor pages. Patient is the signed-in role that is refused.
    [Theory]
    [InlineData("/Doctor/Queue")]
    [InlineData("/Doctor/Schedule")]
    [InlineData("/Doctor/Extensions")]
    [InlineData("/Doctor/Statistics")]
    [InlineData("/Doctor/WalkInAdmissions")]
    [InlineData("/Doctor/Visit/{entry}")]
    [InlineData("/Doctor/Patient/{patientV}/History")]
    public async Task Doctor_pages_render_for_doctors_and_are_refused_to_patients(string template)
    {
        var path = world.Route(template);
        var doctor = await PageSession.SignInAsync(world.Factory, world.Doctor.UserName, world.Doctor.Password);
        await AssertOkAsync(doctor, path);

        var patient = await PageSession.SignInAsync(world.Factory, world.PatientWithAppointment.PhoneNumber, world.PatientWithAppointment.Password);
        await AssertRedirectAsync(patient, path, AccessDeniedPath);
    }

    // Admin pages. Doctor is the signed-in role that is refused.
    [Theory]
    [InlineData("/AdminAccounts")]
    [InlineData("/Admin/Staff")]
    [InlineData("/Admin/Specializations")]
    [InlineData("/Admin/Settings")]
    [InlineData("/Admin/DoctorSchedule/{doctor}")]
    [InlineData("/Analytics")]
    [InlineData("/Analytics/Doctor/{doctor}")]
    public async Task Admin_pages_render_for_admins_and_are_refused_to_doctors(string template)
    {
        var path = world.Route(template);
        var admin = await PageSession.SignInAsync(world.Factory, world.Admin.UserName, world.Admin.Password);
        await AssertOkAsync(admin, path);

        var doctor = await PageSession.SignInAsync(world.Factory, world.Doctor.UserName, world.Doctor.Password);
        await AssertRedirectAsync(doctor, path, AccessDeniedPath);
    }

    // Pages open to any signed-in role: a patient can see them, and anonymous visitors are sent to sign in.
    [Theory]
    [InlineData("/Doctors")]
    [InlineData("/Doctors/Details/{doctor}")]
    [InlineData("/Account/Profile")]
    [InlineData("/Notifications")]
    public async Task Signed_in_pages_render_for_any_role(string template)
    {
        var path = world.Route(template);
        var patient = await PageSession.SignInAsync(world.Factory, world.PatientWithAppointment.PhoneNumber, world.PatientWithAppointment.Password);
        await AssertOkAsync(patient, path);
    }

    [Theory]
    [InlineData("/Appointments/Mine")]
    [InlineData("/Medical/History")]
    [InlineData("/Reception/Queue")]
    [InlineData("/Doctor/Queue")]
    [InlineData("/Doctor/Visit/{entry}")]
    [InlineData("/Doctor/Patient/{patientV}/History")]
    [InlineData("/Admin/Settings")]
    [InlineData("/Analytics")]
    [InlineData("/Doctors")]
    [InlineData("/Notifications")]
    [InlineData("/Account/Profile")]
    public async Task Anonymous_visitors_are_sent_to_sign_in(string template)
    {
        var path = world.Route(template);
        await AssertRedirectAsync(PageSession.Anonymous(world.Factory), path, LoginPath);
    }

    [Fact]
    public async Task Home_page_is_public()
    {
        await AssertOkAsync(PageSession.Anonymous(world.Factory), "/");
    }

    private static async Task AssertOkAsync(PageSession session, string path)
    {
        var response = await session.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"GET {path} returned {(int)response.StatusCode} (location {PageSession.LocationPath(response)}): {Excerpt(body)}");
    }

    private static async Task AssertRedirectAsync(PageSession session, string path, string expectedPath)
    {
        var response = await session.GetAsync(path);
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found,
            $"GET {path} should redirect but returned {(int)response.StatusCode}.");
        var location = PageSession.LocationPath(response);
        Assert.StartsWith(expectedPath, location, StringComparison.Ordinal);
    }

    private static string Excerpt(string html)
    {
        var start = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        var text = start >= 0 ? html[start..] : html;
        return text.Length > 400 ? text[..400] : text;
    }
}
