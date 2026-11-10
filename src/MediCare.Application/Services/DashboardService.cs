using MediCare.Application.DTOs.Dashboard;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Domain.Enums;

namespace MediCare.Application.Services;

public sealed class DashboardService : IDashboardService
{
    private readonly IDoctorRepository _doctors;
    private readonly IPatientRepository _patients;
    private readonly IAppointmentRepository _appointments;
    private readonly IIdentityService _identity;

    public DashboardService(
        IDoctorRepository doctors,
        IPatientRepository patients,
        IAppointmentRepository appointments,
        IIdentityService identity)
    {
        _doctors = doctors;
        _patients = patients;
        _appointments = appointments;
        _identity = identity;
    }

    public async Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken cancellationToken = default)
    {
        return new AdminDashboardDto
        {
            TotalDoctors = await _doctors.CountAsync(null, cancellationToken),
            PendingDoctors = await _doctors.CountAsync(DoctorApprovalStatus.Pending, cancellationToken),
            ApprovedDoctors = await _doctors.CountAsync(DoctorApprovalStatus.Approved, cancellationToken),
            RejectedDoctors = await _doctors.CountAsync(DoctorApprovalStatus.Rejected, cancellationToken),
            TotalPatients = await _patients.CountAsync(cancellationToken),
            TotalUsers = await _identity.CountUsersAsync(cancellationToken),
            ActiveUsers = await _identity.CountActiveUsersAsync(cancellationToken),
            TotalAppointments = await _appointments.CountAsync(null, cancellationToken),
            PendingAppointments = await _appointments.CountAsync(AppointmentStatus.Pending, cancellationToken),
            UpcomingAppointments = await _appointments.CountUpcomingAsync(cancellationToken)
        };
    }
}
