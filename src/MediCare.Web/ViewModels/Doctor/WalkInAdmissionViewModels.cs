using MediCare.Application.Interfaces.Services;

namespace MediCare.Web.ViewModels.Doctor;

public sealed record WalkInAdmissionsViewModel(
    IReadOnlyList<PendingWalkInAdmissionRequest> Requests);

public sealed class WalkInAdmissionDecisionViewModel
{
    public Guid ApprovalId { get; set; }

    public bool Approve { get; set; }

    public string? Notes { get; set; }
}
