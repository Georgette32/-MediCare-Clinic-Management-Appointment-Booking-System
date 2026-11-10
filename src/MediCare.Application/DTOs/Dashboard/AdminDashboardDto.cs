namespace MediCare.Application.DTOs.Dashboard;

public sealed class AdminDashboardDto
{
    public int TotalDoctors { get; set; }

    public int PendingDoctors { get; set; }

    public int ApprovedDoctors { get; set; }

    public int RejectedDoctors { get; set; }

    public int TotalPatients { get; set; }

    public int TotalUsers { get; set; }

    public int ActiveUsers { get; set; }

    public int TotalAppointments { get; set; }

    public int PendingAppointments { get; set; }

    public int UpcomingAppointments { get; set; }
}
