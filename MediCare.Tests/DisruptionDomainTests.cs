using System.Reflection;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Tests;

public sealed class DisruptionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Clinic_unavailable_reason_is_the_exact_stored_text()
    {
        Assert.Equal("clinic unavailable", AppointmentCancellationReasons.ClinicUnavailable);
    }

    [Fact]
    public void Disruption_requires_a_trimmed_reason_within_the_length_limit()
    {
        var doctorId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => DoctorDisruption.Record(doctorId, new DateOnly(2026, 12, 1), new TimeOnly(11, 0), "   ", userId, Now));
        Assert.Throws<ArgumentException>(() => DoctorDisruption.Record(doctorId, new DateOnly(2026, 12, 1), new TimeOnly(11, 0), new string('x', DoctorDisruption.MaxReasonLength + 1), userId, Now));
        Assert.Throws<ArgumentException>(() => DoctorDisruption.Record(Guid.Empty, new DateOnly(2026, 12, 1), new TimeOnly(11, 0), "Emergency", userId, Now));

        var disruption = DoctorDisruption.Record(doctorId, new DateOnly(2026, 12, 1), new TimeOnly(11, 0), "  Emergency  ", userId, Now);
        Assert.Equal("Emergency", disruption.Reason);
        Assert.False(disruption.IsDayEndRecorded);
    }

    [Fact]
    public void Day_end_can_be_recorded_only_once()
    {
        var disruption = DoctorDisruption.Record(Guid.NewGuid(), new DateOnly(2026, 12, 1), new TimeOnly(11, 0), "Emergency", Guid.NewGuid(), Now);

        disruption.RecordDayEnd(Guid.NewGuid(), Now.AddHours(1));

        Assert.True(disruption.IsDayEndRecorded);
        Assert.Throws<InvalidOperationException>(() => disruption.RecordDayEnd(Guid.NewGuid(), Now.AddHours(2)));
    }

    [Fact]
    public void Worklist_item_needs_an_appointment_or_queue_entry_and_a_bounded_note()
    {
        Assert.Throws<ArgumentException>(() => DisruptionWorklistItem.CreateForAppointment(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, null, Now));
        Assert.Throws<ArgumentException>(() => DisruptionWorklistItem.CreateForQueueEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Now));

        var item = DisruptionWorklistItem.CreateForQueueEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        Assert.Equal(DisruptionOutcome.Pending, item.Outcome);
        Assert.True(item.IsOpen);
        Assert.Throws<ArgumentException>(() => item.LogContact(Guid.NewGuid(), new string('n', DisruptionWorklistItem.MaxNotesLength + 1), Now));
    }

    [Fact]
    public void Logging_contact_keeps_the_item_open_and_records_history()
    {
        var item = DisruptionWorklistItem.CreateForAppointment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now);
        var actor = Guid.NewGuid();

        var attempt = item.LogContact(actor, "  Called, no answer  ", Now.AddMinutes(5));

        Assert.Equal(DisruptionOutcome.Contacted, item.Outcome);
        Assert.True(item.IsOpen);
        Assert.Equal(actor, item.LastContactedByUserId);
        Assert.Equal("Called, no answer", item.Notes);
        Assert.Equal(DisruptionOutcome.Contacted, attempt.Outcome);
        Assert.Equal(item.Id, attempt.WorklistItemId);
    }

    [Fact]
    public void Closing_an_item_is_final_and_only_allows_rescheduled_or_cancelled()
    {
        var item = DisruptionWorklistItem.CreateForAppointment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now);

        Assert.Throws<ArgumentOutOfRangeException>(() => item.Resolve(DisruptionOutcome.Contacted, Guid.NewGuid(), null, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.ResolveAutomatically(DisruptionOutcome.Pending, Now));

        var attempt = item.Resolve(DisruptionOutcome.Cancelled, Guid.NewGuid(), "Unreachable", Now);

        Assert.Equal(DisruptionOutcome.Cancelled, item.Outcome);
        Assert.False(item.IsOpen);
        Assert.Equal(DisruptionOutcome.Cancelled, attempt.Outcome);
        Assert.Throws<InvalidOperationException>(() => item.LogContact(Guid.NewGuid(), null, Now));
        Assert.Throws<InvalidOperationException>(() => item.Resolve(DisruptionOutcome.Rescheduled, Guid.NewGuid(), null, Now));
    }

    [Fact]
    public void Automatic_resolution_writes_no_contact_history_fields()
    {
        var item = DisruptionWorklistItem.CreateForAppointment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now);

        item.ResolveAutomatically(DisruptionOutcome.Cancelled, Now.AddHours(1));

        Assert.Equal(DisruptionOutcome.Cancelled, item.Outcome);
        Assert.Null(item.LastContactedByUserId);
        Assert.Null(item.LastContactedAtUtc);
    }

    [Fact]
    public void Contact_attempt_requires_the_acting_user()
    {
        Assert.Throws<ArgumentException>(() => DisruptionContactAttempt.Create(Guid.NewGuid(), Guid.Empty, DisruptionOutcome.Contacted, null, Now));
    }

    [Fact]
    public void Disruption_breaks_are_identifiable_and_existing_breaks_are_unchanged()
    {
        var disruptionId = Guid.NewGuid();
        var disruptionBreak = DoctorBreak.CreateForDisruption(Guid.NewGuid(), new DateOnly(2026, 12, 1), new TimeOnly(11, 0), new TimeOnly(13, 0), disruptionId);
        var manualBreak = DoctorBreak.Create(Guid.NewGuid(), new DateOnly(2026, 12, 1), new TimeOnly(12, 0), new TimeOnly(12, 30));

        Assert.Equal(disruptionId, disruptionBreak.DisruptionId);
        Assert.Null(manualBreak.DisruptionId);
        Assert.Throws<ArgumentException>(() => DoctorBreak.CreateForDisruption(Guid.NewGuid(), new DateOnly(2026, 12, 1), new TimeOnly(11, 0), new TimeOnly(13, 0), Guid.Empty));
        Assert.Throws<ArgumentException>(() => DoctorBreak.CreateForDisruption(Guid.NewGuid(), new DateOnly(2026, 12, 1), new TimeOnly(13, 0), new TimeOnly(11, 0), disruptionId));
    }
}

public sealed class DisruptionAuthorizationTests
{
    private static readonly string[] DisruptionPostActions =
    [
        nameof(ReceptionController.RecordDisruption),
        nameof(ReceptionController.LogDisruptionContact),
        nameof(ReceptionController.RescheduleDisruptionItem),
        nameof(ReceptionController.CancelDisruptionItem),
        nameof(ReceptionController.RecordDisruptionDayEnd)
    ];

    [Fact]
    public void Disruption_pages_are_limited_to_receptionist_and_admin()
    {
        var authorization = typeof(ReceptionController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        var roles = authorization!.Roles!.Split(',');
        Assert.Contains(MediCareRoles.Receptionist, roles);
        Assert.Contains(MediCareRoles.Admin, roles);
        Assert.DoesNotContain(MediCareRoles.Doctor, roles);
    }

    [Theory]
    [InlineData(nameof(ReceptionController.RecordDisruption))]
    [InlineData(nameof(ReceptionController.LogDisruptionContact))]
    [InlineData(nameof(ReceptionController.RescheduleDisruptionItem))]
    [InlineData(nameof(ReceptionController.CancelDisruptionItem))]
    [InlineData(nameof(ReceptionController.RecordDisruptionDayEnd))]
    public void Disruption_changes_are_posts_with_antiforgery(string actionName)
    {
        var action = typeof(ReceptionController).GetMethods().Single(method => method.Name == actionName);
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(nameof(ReceptionController.Disruptions))]
    [InlineData(nameof(ReceptionController.Disruption))]
    public void Disruption_pages_are_not_anonymous(string actionName)
    {
        var action = typeof(ReceptionController).GetMethods().Single(method => method.Name == actionName);
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void Doctor_controller_has_no_clinic_disruption_actions()
    {
        var methods = typeof(DoctorController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(methods, method => method.Name.Contains("Disruption", StringComparison.OrdinalIgnoreCase));
        Assert.All(DisruptionPostActions, name => Assert.DoesNotContain(methods, method => method.Name == name));
    }
}
