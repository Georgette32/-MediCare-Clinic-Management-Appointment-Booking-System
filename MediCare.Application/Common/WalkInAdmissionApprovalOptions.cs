using System.ComponentModel.DataAnnotations;

namespace MediCare.Application.Common;

public sealed class WalkInAdmissionApprovalOptions
{
    [Range(1, int.MaxValue)]
    public int PendingDurationMinutes { get; set; } = 30;
}
