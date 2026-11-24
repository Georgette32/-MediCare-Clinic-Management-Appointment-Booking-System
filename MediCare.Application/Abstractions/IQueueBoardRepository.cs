using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

/// <summary>Read-only queries for the Reception and Doctor queue boards.</summary>
public interface IQueueBoardRepository
{
    /// <summary>All appointments of the doctor on the date, in any status, with the patient loaded.</summary>
    Task<IReadOnlyList<Appointment>> GetDoctorAppointmentsForDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>All attendance attempts of the doctor on the date, in any status, with patient and appointment loaded.</summary>
    Task<IReadOnlyList<QueueEntry>> GetDoctorQueueEntriesForDateAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken);

    Task<IReadOnlyList<Doctor>> GetActiveDoctorsAsync(CancellationToken cancellationToken);

    /// <summary>Looks up a patient by the canonical +20 phone number.</summary>
    Task<Guid?> FindPatientIdByPhoneAsync(string canonicalPhone, CancellationToken cancellationToken);
}
