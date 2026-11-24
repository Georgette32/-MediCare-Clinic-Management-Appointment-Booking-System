using MediCare.Domain.Entities;
using MediCare.Domain.Enums;

namespace MediCare.Tests;

public sealed class WorkingHourExtensionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 12, 2);
    private static readonly Guid DoctorId = Guid.NewGuid();
    private static readonly Guid RequesterId = Guid.NewGuid();

    [Fact]
    public void Request_starts_pending_without_decision_and_trims_reason()
    {
        var extension = DoctorWorkingHourExtension.Request(DoctorId, Date, new TimeOnly(11, 0), new TimeOnly(11, 30), "  Overflow  ", RequesterId, Now);

        Assert.Equal(WorkingHourExtensionStatus.Pending, extension.Status);
        Assert.Null(extension.DecidedAtUtc);
        Assert.Equal("Overflow", extension.Reason);
        Assert.Equal(RequesterId, extension.RequestedByUserId);
    }

    [Fact]
    public void Created_by_doctor_is_approved_immediately_with_decision_time_equal_to_request_time()
    {
        var extension = DoctorWorkingHourExtension.CreateApprovedByDoctor(DoctorId, Date, new TimeOnly(11, 0), new TimeOnly(11, 30), "Overflow", RequesterId, Now);

        Assert.Equal(WorkingHourExtensionStatus.Approved, extension.Status);
        Assert.Equal(extension.RequestedAtUtc, extension.DecidedAtUtc);
    }

    [Theory]
    [InlineData("10:30", "10:30")]
    [InlineData("10:30", "10:00")]
    public void Extended_end_must_be_later_than_original_end(string original, string extended)
    {
        Assert.Throws<ArgumentException>(() =>
            DoctorWorkingHourExtension.Request(DoctorId, Date, TimeOnly.Parse(original), TimeOnly.Parse(extended), "Reason", RequesterId, Now));
    }

    [Fact]
    public void Reason_is_required_and_limited_to_500_characters()
    {
        Assert.Throws<ArgumentException>(() =>
            DoctorWorkingHourExtension.Request(DoctorId, Date, new TimeOnly(11, 0), new TimeOnly(11, 30), "   ", RequesterId, Now));
        Assert.Throws<ArgumentException>(() =>
            DoctorWorkingHourExtension.Request(DoctorId, Date, new TimeOnly(11, 0), new TimeOnly(11, 30), new string('x', 501), RequesterId, Now));

        var exact = DoctorWorkingHourExtension.Request(DoctorId, Date, new TimeOnly(11, 0), new TimeOnly(11, 30), new string('x', 500), RequesterId, Now);
        Assert.Equal(500, exact.Reason.Length);
    }

    [Fact]
    public void Approve_and_reject_are_allowed_only_from_pending_and_record_notes()
    {
        var approved = Pending();
        approved.Approve(Now.AddMinutes(5), "  Fine  ");
        Assert.Equal(WorkingHourExtensionStatus.Approved, approved.Status);
        Assert.Equal(Now.AddMinutes(5), approved.DecidedAtUtc);
        Assert.Equal("Fine", approved.DecisionNotes);
        Assert.Throws<InvalidOperationException>(() => approved.Reject(Now.AddMinutes(6)));
        Assert.Throws<InvalidOperationException>(() => approved.Approve(Now.AddMinutes(6)));

        var rejected = Pending();
        rejected.Reject(Now.AddMinutes(5), null);
        Assert.Equal(WorkingHourExtensionStatus.Rejected, rejected.Status);
        Assert.Null(rejected.DecisionNotes);
        Assert.Throws<InvalidOperationException>(() => rejected.Approve(Now.AddMinutes(6)));
    }

    [Fact]
    public void Decision_notes_are_limited_to_500_characters()
    {
        var extension = Pending();
        Assert.Throws<ArgumentException>(() => extension.Approve(Now.AddMinutes(1), new string('n', 501)));
        Assert.Equal(WorkingHourExtensionStatus.Pending, extension.Status);
    }

    [Fact]
    public void Cancel_is_allowed_from_pending_and_approved_but_not_from_decided_history()
    {
        var pending = Pending();
        pending.Cancel(Now.AddMinutes(1));
        Assert.Equal(WorkingHourExtensionStatus.Cancelled, pending.Status);
        Assert.Equal(Now.AddMinutes(1), pending.DecidedAtUtc);

        var approved = DoctorWorkingHourExtension.CreateApprovedByDoctor(DoctorId, Date, new TimeOnly(11, 0), new TimeOnly(11, 30), "Overflow", RequesterId, Now);
        approved.Cancel(Now.AddMinutes(1));
        Assert.Equal(WorkingHourExtensionStatus.Cancelled, approved.Status);

        var rejected = Pending();
        rejected.Reject(Now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => rejected.Cancel(Now.AddMinutes(2)));
    }

    [Fact]
    public void A_pending_request_dated_before_today_is_expired_and_an_approved_one_is_not()
    {
        var pending = Pending();
        Assert.False(pending.IsExpiredPending(Date));
        Assert.True(pending.IsExpiredPending(Date.AddDays(1)));

        var approved = DoctorWorkingHourExtension.CreateApprovedByDoctor(DoctorId, Date, new TimeOnly(11, 0), new TimeOnly(11, 30), "Overflow", RequesterId, Now);
        Assert.False(approved.IsExpiredPending(Date.AddDays(1)));
    }

    private static DoctorWorkingHourExtension Pending() =>
        DoctorWorkingHourExtension.Request(DoctorId, Date, new TimeOnly(11, 0), new TimeOnly(11, 30), "Overflow", RequesterId, Now);
}
