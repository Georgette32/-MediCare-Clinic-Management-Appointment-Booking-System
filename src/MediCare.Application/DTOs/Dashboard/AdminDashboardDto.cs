
namespace MediCare.Application.DTOs.Dashboard;

public sealed class AdminDashboardDto
{
    // Doctors
    public int TotalDoctors { get; set; }
    public int PendingDoctors { get; set; }
    public int ApprovedDoctors { get; set; }
    public int RejectedDoctors { get; set; }

    // Patients and Users
    public int TotalPatients { get; set; }
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }

    // Appointments
    public int TotalAppointments { get; set; }
    public int PendingAppointments { get; set; }
    public int UpcomingAppointments { get; set; }
    public int CompletedAppointments { get; set; }
    public int CancelledAppointments { get; set; }
    public int NoShowAppointments { get; set; }

    // Finance
    public int TotalInvoices { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalOutstanding { get; set; }
    public int TotalPayments { get; set; }

    // Analytics
    public double CompletionRate { get; set; }
    public double CancellationRate { get; set; }
    public double NoShowRate { get; set; }
}