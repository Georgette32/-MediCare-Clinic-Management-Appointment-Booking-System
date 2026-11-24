using System.Net;
using System.Text.RegularExpressions;

namespace MediCare.Tests.Integration;

[Collection(PageWorldCollection.Name)]
public sealed partial class SecurityRenderingTests(PageWorldFixture world)
{
    [Fact]
    public async Task Pages_send_the_baseline_security_headers()
    {
        var anonymous = PageSession.Anonymous(world.Factory);
        var response = await anonymous.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var csp = Header(response, "Content-Security-Policy");
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
        Assert.Contains("camera=()", Header(response, "Permissions-Policy"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Security_headers_are_present_on_authenticated_pages_and_redirects()
    {
        var doctor = await PageSession.SignInAsync(world.Factory, world.Doctor.UserName, world.Doctor.Password);
        var page = await doctor.GetAsync("/Doctor/Schedule");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("nosniff", Header(page, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(page, "X-Frame-Options"));

        var redirect = await PageSession.Anonymous(world.Factory).GetAsync("/Doctor/Schedule");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("DENY", Header(redirect, "X-Frame-Options"));
    }

    [Fact]
    public async Task Rendered_pages_have_no_inline_script_blocks_or_inline_event_handlers()
    {
        var doctor = await PageSession.SignInAsync(world.Factory, world.Doctor.UserName, world.Doctor.Password);
        var receptionist = await PageSession.SignInAsync(world.Factory, world.Receptionist.UserName, world.Receptionist.Password);
        var patient = await PageSession.SignInAsync(world.Factory, world.PatientWithAppointment.PhoneNumber, world.PatientWithAppointment.Password);

        var pages = new (PageSession Session, string Path)[]
        {
            (PageSession.Anonymous(world.Factory), "/Account/Login"),
            (PageSession.Anonymous(world.Factory), "/Account/Register"),
            (PageSession.Anonymous(world.Factory), "/"),
            (doctor, "/Doctor/Schedule"),
            (doctor, world.Route("/Doctor/Visit/{entry}")),
            (receptionist, "/Reception/Queue"),
            (receptionist, world.Route("/Reception/PatientDetail/{patientA}")),
            (patient, "/Appointments/Mine"),
            (patient, "/Account/Profile")
        };

        foreach (var (session, path) in pages)
        {
            var html = await session.GetOkHtmlAsync(path);
            var inlineScripts = InlineScriptRegex().Matches(html)
                .Where(match => !match.Value.Contains("src=", StringComparison.OrdinalIgnoreCase))
                .Select(match => match.Value)
                .ToList();
            if (inlineScripts.Count > 0)
                Assert.Fail($"{path} contains an inline script block: {inlineScripts[0]}");

            var handlers = EventHandlerRegex().Matches(html).Select(match => match.Value).ToList();
            if (handlers.Count > 0)
                Assert.Fail($"{path} contains inline event handler attributes: {string.Join(", ", handlers)}");
        }
    }

    private static string Header(HttpResponseMessage response, string name)
    {
        Assert.True(response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values),
            $"Response is missing the {name} header.");
        return string.Join(",", values!);
    }

    [GeneratedRegex("<script\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScriptRegex();

    // An attribute named on<something> inside an opening tag, for example onclick="...".
    [GeneratedRegex("<[a-zA-Z][^>]*\\s(on[a-zA-Z]+)\\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlerRegex();
}
