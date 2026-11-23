using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.ViewModels.Patient;

public sealed class BookAppointmentViewModel
{
    public BookAppointmentViewModel()
    {
        Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        StartTime = new TimeOnly(9, 0);
    }

    [Required]
    public Guid PatientProfileId { get; set; }

    [Required(ErrorMessage = "Choose a doctor.")]
    public Guid DoctorProfileId { get; set; }

    [Required(ErrorMessage = "Choose an appointment date.")]
    public DateOnly Date { get; set; }

    [Required(ErrorMessage = "Choose a start time.")]
    public TimeOnly StartTime { get; set; }

    public IReadOnlyList<DoctorOption> Doctors { get; set; } = Array.Empty<DoctorOption>();

    public IReadOnlyList<TimeOnly> TimeSlots { get; set; } = Array.Empty<TimeOnly>();
}

public sealed record DoctorOption(Guid Id, string Name, string Specialization);
