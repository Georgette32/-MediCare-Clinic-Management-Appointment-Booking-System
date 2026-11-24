using MediCare.Infrastructure.Identity;


namespace MediCare.Tests.Integration;

/// <summary>
/// One real host and database shared by the page tests in the collection. Each test that changes state seeds its own
/// accounts; the accounts seeded here are only read by the smoke and rendering tests.
/// </summary>
public sealed class PageWorldFixture : IDisposable
{
    public PageWorldFixture()
    {
        Factory = new MediCareWebApplicationFactory();
        try
        {
            Data = new PageTestData(Factory);

            // Starting the host (through Services) runs Program, which seeds the bootstrap Admin and the roles.
            _ = Factory.Services;

            Doctor = Data.CreateDoctorAsync("Dr Shared Page").GetAwaiter().GetResult();
            Receptionist = Data.CreateReceptionistAsync().GetAwaiter().GetResult();
            PatientWithAppointment = Data.CreatePatientAsync("Shared Patient A").GetAwaiter().GetResult();
            PatientWithVisit = Data.CreatePatientAsync("Shared Patient V").GetAwaiter().GetResult();
            PatientWithoutAppointments = Data.CreatePatientAsync("Shared Patient B").GetAwaiter().GetResult();

            AppointmentOfPatientA = Data.CreateConfirmedAppointmentAsync(
                PatientWithAppointment.PatientId, Doctor.DoctorId, Data.ClinicTomorrow, new TimeOnly(10, 0), new TimeOnly(10, 20))
                .GetAwaiter().GetResult();
            InConsultationEntry = Data.CreateInConsultationEntryAsync(
                PatientWithVisit.PatientId, Doctor.DoctorId, new TimeOnly(9, 0))
                .GetAwaiter().GetResult();
        }
        catch
        {
            // xUnit does not dispose a fixture whose constructor failed, so the factory (and its database) is released here.
            Factory.Dispose();
            throw;
        }
    }

    public MediCareWebApplicationFactory Factory { get; }
    public PageTestData Data { get; private set; } = null!;

    public SeededUser Admin { get; } = new(Guid.Empty, MediCareWebApplicationFactory.AdminEmail, MediCareWebApplicationFactory.AdminPassword);
    public SeededDoctor Doctor { get; private set; } = null!;
    public SeededUser Receptionist { get; private set; } = null!;
    public SeededPatient PatientWithAppointment { get; private set; } = null!;
    public SeededPatient PatientWithVisit { get; private set; } = null!;
    public SeededPatient PatientWithoutAppointments { get; private set; } = null!;
    public Guid AppointmentOfPatientA { get; private set; }
    public Guid InConsultationEntry { get; private set; }

    /// <summary>Replaces the placeholders used in the route tables with ids and dates of the shared world.</summary>
    public string Route(string template) =>
        template
            .Replace("{doctor}", Doctor.DoctorId.ToString(), StringComparison.Ordinal)
            .Replace("{patientA}", PatientWithAppointment.PatientId.ToString(), StringComparison.Ordinal)
            .Replace("{patientV}", PatientWithVisit.PatientId.ToString(), StringComparison.Ordinal)
            .Replace("{entry}", InConsultationEntry.ToString(), StringComparison.Ordinal)
            .Replace("{appointmentA}", AppointmentOfPatientA.ToString(), StringComparison.Ordinal)
            .Replace("{tomorrow}", Data.ClinicTomorrow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    public void Dispose() => Factory.Dispose();
}

[CollectionDefinition(Name)]
public sealed class PageWorldCollection : ICollectionFixture<PageWorldFixture>
{
    public const string Name = "MediCare page tests";
}
