
using MediCare.Application.DTOs.Dashboard;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Domain.Enums;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Application.Services;

public sealed class DashboardService : IDashboardService
{
    private readonly IDoctorRepository _doctors;
    private readonly IPatientRepository _patients;
    private readonly IAppointmentRepository _appointments;
    private readonly IInvoiceRepository _invoices;
    private readonly IPaymentRepository _payments;
    private readonly IIdentityService _identity;

    public DashboardService(
        IDoctorRepository doctors,
        IPatientRepository patients,
        IAppointmentRepository appointments,
        IInvoiceRepository invoices,
        IPaymentRepository payments,
        IIdentityService identity)
    {
        _doctors = doctors;
        _patients = patients;
        _appointments = appointments;
        _invoices = invoices;
        _payments = payments;
        _identity = identity;
    }

    public async Task<AdminDashboardDto> GetAdminDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var totalAppointments =
            await _appointments.CountAsync(null, cancellationToken);

        var completed =
            await _appointments.CountAsync(
                AppointmentStatus.Completed, cancellationToken);

        var cancelled =
            await _appointments.CountAsync(
                AppointmentStatus.Cancelled, cancellationToken);

        var noShow =
            await _appointments.CountAsync(
                AppointmentStatus.NoShow, cancellationToken);

        return new AdminDashboardDto
        {
            TotalDoctors = await _doctors.CountAsync(null, cancellationToken),
            PendingDoctors = await _doctors.CountAsync(
                DoctorApprovalStatus.Pending, cancellationToken),
            ApprovedDoctors = await _doctors.CountAsync(
                DoctorApprovalStatus.Approved, cancellationToken),
            RejectedDoctors = await _doctors.CountAsync(
                DoctorApprovalStatus.Rejected, cancellationToken),

            TotalPatients = await _patients.CountAsync(cancellationToken),
            TotalUsers = await _identity.CountUsersAsync(cancellationToken),
            ActiveUsers = await _identity.CountActiveUsersAsync(cancellationToken),

            TotalAppointments = totalAppointments,
            PendingAppointments = await _appointments.CountAsync(
                AppointmentStatus.Pending, cancellationToken),
            UpcomingAppointments = await _appointments.CountUpcomingAsync(
                cancellationToken),
            CompletedAppointments = completed,
            CancelledAppointments = cancelled,
            NoShowAppointments = noShow,

            TotalInvoices = await _invoices.CountAsync(cancellationToken),
            TotalRevenue = await _payments.GetTotalRevenueAsync(cancellationToken),
            TotalOutstanding = await _invoices.GetTotalOutstandingAsync(cancellationToken),
            TotalPayments = await _payments.CountCompletedAsync(cancellationToken),

            CompletionRate = CalculateRate(completed, totalAppointments),
            CancellationRate = CalculateRate(cancelled, totalAppointments),
            NoShowRate = CalculateRate(noShow, totalAppointments)
        };
    }

    private static double CalculateRate(int count, int total)
    {
        return total == 0
            ? 0
            : Math.Round((double)count / total * 100, 2);
    }
}