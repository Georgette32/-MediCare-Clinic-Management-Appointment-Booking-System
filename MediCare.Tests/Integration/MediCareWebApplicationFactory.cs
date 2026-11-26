using MediCare.Application.Abstractions;
using MediCare.Infrastructure.Persistence;
using MediCare.Infrastructure.Storage;
using MediCare.Web.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MediCare.Tests.Integration;

/// <summary>
/// Runs the real MediCare.Web application in-process against a disposable LocalDB database.
/// Configuration overrides are in memory only; the configured MediCare database is never touched.
/// The host runs as "Development" with an https base address, so the Secure auth cookie is sent back by the test client.
/// </summary>
public sealed class MediCareWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "page-tests-admin@example.invalid";
    public const string AdminPassword = "PageTests-Admin-Pa55word!";
    public const string AdminFullName = "Page Test Administrator";
    public const string ClinicTimeZoneId = "Africa/Cairo";
    public static readonly Uri BaseAddress = new("https://localhost");

    private readonly IntegrationDatabase _database;
    private readonly string _storagePath;
    private readonly int _authPermitLimit;
    private readonly int _authWindowSeconds;

    /// <param name="authPermitLimit">Sign-in and password-change POSTs allowed per client per window. Large by default.</param>
    /// <param name="authWindowSeconds">Length of the sign-in rate-limit window.</param>
    public MediCareWebApplicationFactory(int authPermitLimit = 100_000, int authWindowSeconds = 60)
    {
        _authPermitLimit = authPermitLimit;
        _authWindowSeconds = authWindowSeconds;
        _storagePath = Path.Combine(Path.GetTempPath(), "medicare-page-tests-" + Guid.NewGuid().ToString("N"));
        // Created before the host starts: Program seeds the bootstrap Admin at startup and that needs the schema.
        _database = IntegrationDatabase.CreateAndMigrate();
    }

    public string DatabaseName => _database.DatabaseName;

    /// <summary>Every email the app sent through this host.</summary>
    public CapturingEmailSender Emails { get; } = new();

    /// <summary>Client that keeps its own cookie jar and never follows redirects, so tests can assert each redirect.</summary>
    public HttpClient CreateSessionClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = BaseAddress
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseContentRoot(FindWebProjectDirectory());

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MediCare"] = _database.ConnectionString,
                ["Clinic:TimeZoneId"] = ClinicTimeZoneId,
                ["BootstrapAdmin:Email"] = AdminEmail,
                ["BootstrapAdmin:Password"] = AdminPassword,
                ["BootstrapAdmin:FullName"] = AdminFullName,
                ["MedicalFiles:StoragePath"] = _storagePath
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Program reads the connection string and the rate-limit values while it registers its services, before these
            // in-memory overrides are visible to it. Both registrations are therefore replaced here, so the host can only
            // reach the disposable database and uses the test limits.
            services.RemoveAll<DbContextOptions<MediCareDbContext>>();
            services.AddDbContext<MediCareDbContext>(options => options.UseSqlServer(_database.ConnectionString));

            // Medical files are stored in a temp folder for this host only, never under the web project.
            services.RemoveAll<IMedicalFileStore>();
            services.AddSingleton<IMedicalFileStore>(new FileSystemMedicalFileStore(_storagePath));
            // The policies are registered once by Program; drop that registration so the test limits can be registered instead.
            services.RemoveAll<Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>();
            services.AddMediCareRateLimiting(new RateLimitingOptions
            {
                Auth = new RateLimitRule { PermitLimit = _authPermitLimit, WindowSeconds = _authWindowSeconds },
                Register = new RateLimitRule { PermitLimit = 100_000, WindowSeconds = 3600 },
                Otp = new RateLimitRule { PermitLimit = 100_000, WindowSeconds = 600 }
            });

            // Verification emails are captured in memory so tests can read the code.
            services.RemoveAll<MediCare.Infrastructure.Email.IEmailSender>();
            services.AddSingleton<MediCare.Infrastructure.Email.IEmailSender>(Emails);

            // The disruption maintenance loop would run timers against the test database; the tests do not need it.
            services.RemoveAll<IHostedService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing)
            {
                _database.Dispose();
                TryDeleteStorage();
            }
        }
    }

    private void TryDeleteStorage()
    {
        try
        {
            if (Directory.Exists(_storagePath))
                Directory.Delete(_storagePath, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open by a finished request must not fail the test run; the temp folder is outside the repo.
        }
    }

    private static string FindWebProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MediCare.slnx")))
                return Path.Combine(directory.FullName, "MediCare.Web");
        }
        throw new InvalidOperationException("The solution folder (MediCare.slnx) could not be found above the test output folder.");
    }
}

/// <summary>Keeps sent emails in memory instead of sending them.</summary>
public sealed class CapturingEmailSender : MediCare.Infrastructure.Email.IEmailSender
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<MediCare.Infrastructure.Email.EmailMessage> _sent = new();

    public IReadOnlyList<MediCare.Infrastructure.Email.EmailMessage> Sent => _sent.ToArray();

    public Task SendAsync(MediCare.Infrastructure.Email.EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>The 6-digit code from the latest email to <paramref name="address"/>.</summary>
    public string LatestCodeFor(string address)
    {
        var message = Sent.Last(item => string.Equals(item.ToAddress, address, StringComparison.OrdinalIgnoreCase));
        var match = System.Text.RegularExpressions.Regex.Match(message.Subject, @"^(\d{3}) (\d{3})");
        return match.Groups[1].Value + match.Groups[2].Value;
    }
}
