namespace MediCare.Web.Models;

public sealed class WalkInAdmissionDecisionViewModel
{
    public Guid ApprovalId { get; set; }
    public bool Approve { get; set; }
    public string? Notes { get; set; }
}
