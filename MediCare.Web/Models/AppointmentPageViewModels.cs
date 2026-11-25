using System.ComponentModel.DataAnnotations;
using MediCare.Application.Services;

namespace MediCare.Web.Models;

/// <summary>Posted by the patient booking confirmation form. The patient is never taken from the form.</summary>
public sealed class BookingForm
{
    [Required]
    public Guid DoctorId { get; set; }

    [Required]
    public DateOnly Date { get; set; }

    [Required]
    public TimeOnly Time { get; set; }
}

/// <summary>Confirmation page for a booking request (patient) or a confirmed booking (Reception/Admin).</summary>
public sealed class BookingConfirmationModel
{
    public DoctorProfileView Profile { get; init; } = null!;
    public DateOnly Date { get; init; }
    public TimeOnly Time { get; init; }
    public TimeOnly EndTime { get; init; }
    public bool IsSlotFree { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<TimeOnly> Alternatives { get; init; } = [];
}

/// <summary>Posted by the patient reschedule form.</summary>
public sealed class RescheduleForm
{
    [Required]
    public Guid AppointmentId { get; set; }

    [Required]
    public Guid DoctorId { get; set; }

    [Required]
    public DateOnly Date { get; set; }

    [Required]
    public TimeOnly Time { get; set; }
}

/// <summary>Patient reschedule page: the original appointment, the doctor/date choice and the free slots.</summary>
public sealed class RescheduleModel
{
    public Guid AppointmentId { get; init; }
    public string OriginalDoctorName { get; init; } = string.Empty;
    public DateOnly OriginalDate { get; init; }
    public TimeOnly OriginalStartTime { get; init; }
    public IReadOnlyList<MediCare.Application.Services.DoctorDirectoryItem> Doctors { get; init; } = [];
    public Guid DoctorId { get; init; }
    public DateOnly Date { get; init; }
    public DateOnly Today { get; init; }
    public IReadOnlyList<TimeOnly> Slots { get; init; } = [];
    public string? Error { get; init; }
    public IReadOnlyList<TimeOnly> Alternatives { get; init; } = [];
}

/// <summary>Posted by the patient cancel form. Ownership is checked again on the server.</summary>
public sealed class PatientCancelForm
{
    [Required]
    public Guid AppointmentId { get; set; }

    [StringLength(500)]
    [Display(Name = "Reason (optional)")]
    public string? Reason { get; set; }
}

/// <summary>Reception/Admin search box for patients.</summary>
public sealed class PatientSearchModel
{
    [Display(Name = "Phone or name")]
    public string? Query { get; set; }

    /// <summary>Filled by the controller; not bound from the request.</summary>
    public IReadOnlyList<MediCare.Application.Abstractions.PatientSearchRecord> Results { get; set; } = [];

    /// <summary>What is wrong with the query (shown under the field), or null. Filled by the controller.</summary>
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public string? Problem { get; set; }

    /// <summary>True when the query is part of a phone number (searched as "contains these digits").</summary>
    public bool IsPartialPhone =>
        Problem is null && !string.IsNullOrWhiteSpace(Query) && InputChecks.LooksLikePhone(Query.Trim()) &&
        !MediCare.Application.Services.EgyptianMobileNumber.TryNormalize(Query, out _, out _);
}

/// <summary>Reception/Admin patient detail: contact details, appointments and the booking panel.</summary>
public sealed class PatientDetailModel
{
    public MediCare.Application.Abstractions.PatientSearchRecord Patient { get; init; } = null!;
    public IReadOnlyList<AppointmentRow> Appointments { get; init; } = [];
    public IReadOnlyList<MediCare.Application.Services.DoctorChoice> Doctors { get; init; } = [];
    public Guid? DoctorId { get; init; }
    public DateOnly Date { get; init; }
    public DateOnly Today { get; init; }
    public IReadOnlyList<TimeOnly> Slots { get; init; } = [];
}

/// <summary>Posted by the Reception/Admin book-for-patient form.</summary>
public sealed class ClinicBookingForm
{
    [Required]
    public Guid PatientId { get; set; }

    [Required]
    public Guid DoctorId { get; set; }

    [Required]
    public DateOnly Date { get; set; }

    [Required]
    public TimeOnly Time { get; set; }
}

/// <summary>Reception/Admin confirmation page for booking a patient.</summary>
public sealed class ClinicBookingConfirmationModel
{
    public MediCare.Application.Abstractions.PatientSearchRecord Patient { get; init; } = null!;
    public BookingConfirmationModel Booking { get; init; } = null!;
}

/// <summary>Reception/Admin day view of appointments.</summary>
public sealed class AppointmentDayModel
{
    public DateOnly Date { get; init; }
    public Guid? DoctorId { get; init; }
    public DateOnly Today { get; init; }
    public IReadOnlyList<MediCare.Application.Services.DoctorChoice> Doctors { get; init; } = [];
    public IReadOnlyList<AppointmentRow> Rows { get; init; } = [];
}

/// <summary>Posted by the Reception/Admin cancel form. Reason is required; the actor comes from the signed-in role.</summary>
public sealed class ClinicCancelForm
{
    [Required]
    public Guid AppointmentId { get; set; }

    [Required, StringLength(500)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = string.Empty;

    public DateOnly Date { get; set; }
    public Guid? DoctorId { get; set; }
}

/// <summary>Posted by the Reception/Admin reschedule form.</summary>
public sealed class ClinicRescheduleForm
{
    [Required]
    public Guid AppointmentId { get; set; }

    [Required]
    public Guid DoctorId { get; set; }

    [Required]
    public DateOnly Date { get; set; }

    [Required]
    public TimeOnly Time { get; set; }
}

/// <summary>Reception/Admin reschedule page for one appointment.</summary>
public sealed class ClinicRescheduleModel
{
    public AppointmentRow Original { get; init; } = null!;
    public IReadOnlyList<MediCare.Application.Services.DoctorChoice> Doctors { get; init; } = [];
    public Guid DoctorId { get; init; }
    public DateOnly Date { get; init; }
    public DateOnly Today { get; init; }
    public IReadOnlyList<TimeOnly> Slots { get; init; } = [];
    public string? Error { get; init; }
    public IReadOnlyList<TimeOnly> Alternatives { get; init; } = [];
}
