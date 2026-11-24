using MediCare.Application.Services;

namespace MediCare.Web.Models;

/// <summary>Confirm page for deactivating a doctor: the counts come from the service, not the form.</summary>
public sealed class StaffDeactivationViewModel
{
    public Guid UserId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public DoctorDeactivationPreview Preview { get; init; } = null!;
}
