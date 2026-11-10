using MediCare.Application.DTOs.Doctors;

namespace MediCare.Web.ViewModels.Doctor;

public sealed class DoctorDashboardViewModel
{
    public DoctorDetailsDto Doctor { get; set; } = new();

    public string FullName { get; set; } = string.Empty;
}
