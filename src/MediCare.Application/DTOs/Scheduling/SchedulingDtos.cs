namespace MediCare.Application.DTOs.Scheduling;

public sealed record PendingAppointmentDto(
    Guid Id,
    Guid PatientProfileId,
    Guid DoctorProfileId,
    string PatientName,
    string DoctorName,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime);
