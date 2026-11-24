using MediCare.Application.Services;
using MediCare.Domain.Enums;

namespace MediCare.Web.Models;

/// <summary>Display labels for queue states. Kept out of the Razor views so the wording is defined once.</summary>
public static class QueueBoardText
{
    public static string Scheduled(ScheduledBoardState state) => state switch
    {
        ScheduledBoardState.NotArrived => "Not arrived",
        ScheduledBoardState.GraceRunning => "Grace running",
        ScheduledBoardState.GraceExpired => "Grace expired (not checked in)",
        ScheduledBoardState.Waiting => "Waiting",
        ScheduledBoardState.Called => "Called",
        ScheduledBoardState.InConsultation => "In consultation",
        ScheduledBoardState.Completed => "Completed",
        ScheduledBoardState.NoShow => "No-show",
        _ => state.ToString()
    };

    public static string Approval(WalkInApprovalState state) => state switch
    {
        WalkInApprovalState.None => "No request",
        WalkInApprovalState.Pending => "Pending doctor decision",
        WalkInApprovalState.Approved => "Approved (not yet called)",
        WalkInApprovalState.Used => "Used (new approval needed)",
        WalkInApprovalState.Rejected => "Rejected",
        WalkInApprovalState.Expired => "Expired",
        _ => state.ToString()
    };

    public static string Walk(QueueEntryStatus status) => status switch
    {
        QueueEntryStatus.Waiting => "Waiting",
        QueueEntryStatus.Called => "Called",
        QueueEntryStatus.InConsultation => "In consultation",
        QueueEntryStatus.Completed => "Completed",
        QueueEntryStatus.Cancelled => "Cancelled",
        QueueEntryStatus.NoShow => "No-show",
        _ => status.ToString()
    };
}
