using MediCare.Application.Abstractions;
using MediCare.Application.Enums;
using MediCare.Application.Services;
using MediCare.Domain.Entities;
using MediCare.Infrastructure.Notifications;
using MediCare.Infrastructure.Time;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCare.Tests;

public sealed class NotificationDomainTests
{
    [Fact]
    public void A_notification_needs_a_recipient_kind_title_and_message()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => Notification.Create(Guid.Empty, "K", "T", "M", null, null, null, now));
        Assert.Throws<ArgumentException>(() => Notification.Create(Guid.NewGuid(), " ", "T", "M", null, null, null, now));
        Assert.Throws<ArgumentException>(() => Notification.Create(Guid.NewGuid(), new string('k', 49), "T", "M", null, null, null, now));
        Assert.Throws<ArgumentException>(() => Notification.Create(Guid.NewGuid(), "K", new string('t', 151), "M", null, null, null, now));
        Assert.Throws<ArgumentException>(() => Notification.Create(Guid.NewGuid(), "K", "T", "   ", null, null, null, now));
    }

    [Fact]
    public void A_related_entity_needs_both_its_type_and_its_id_or_neither()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => Notification.Create(Guid.NewGuid(), "K", "T", "M", "Appointment", null, null, now));
        Assert.Throws<ArgumentException>(() => Notification.Create(Guid.NewGuid(), "K", "T", "M", null, Guid.NewGuid(), null, now));
        Assert.NotNull(Notification.Create(Guid.NewGuid(), "K", "T", "M", null, null, null, now));
    }

    [Fact]
    public void Over_long_messages_are_truncated_rather_than_refused()
    {
        var notification = Notification.Create(Guid.NewGuid(), "K", "T", new string('m', 1500), null, null, null, DateTimeOffset.UtcNow);

        Assert.Equal(Notification.MaxMessageLength, notification.Message.Length);
    }

    [Fact]
    public void Blank_dedup_keys_are_stored_as_null_and_marking_read_keeps_the_first_time()
    {
        var created = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
        var notification = Notification.Create(Guid.NewGuid(), "K", "T", "M", null, null, "  ", created);
        Assert.Null(notification.DedupKey);
        Assert.False(notification.IsRead);

        notification.MarkRead(created.AddMinutes(5));
        notification.MarkRead(created.AddMinutes(30));

        Assert.Equal(created.AddMinutes(5), notification.ReadAtUtc);
    }
}

public sealed class NotificationServiceTests
{
    private static readonly DateOnly Today = QueueTestData.Today;

    [Fact]
    public async Task Notifying_a_patient_writes_one_unread_row_for_the_patient_user()
    {
        var w = new World();
        var patientId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        w.Repository.PatientUsers[patientId] = userId;

        var created = await w.Service.NotifyPatientAsync(patientId, new NotificationDraft("BookingRequestConfirmed", "Booking confirmed", "Confirmed."), CancellationToken.None);

        Assert.True(created);
        var row = Assert.Single(w.Repository.Rows);
        Assert.Equal(userId, row.RecipientUserId);
        Assert.False(row.IsRead);
        Assert.Equal(1, await w.Service.CountUnreadAsync(userId, CancellationToken.None));
    }

    [Fact]
    public async Task A_repeated_dedup_key_is_delivered_once()
    {
        var w = new World();
        var userId = Guid.NewGuid();
        var draft = new NotificationDraft("AppointmentReminder", "Tomorrow", "Reminder.", "Appointment", Guid.NewGuid(), "reminder:abc");

        var first = await w.Service.NotifyUserAsync(userId, draft, CancellationToken.None);
        var second = await w.Service.NotifyUserAsync(userId, draft, CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
        Assert.Single(w.Repository.Rows);
    }

    [Fact]
    public async Task Notifying_an_unknown_patient_or_doctor_fails_clearly()
    {
        var w = new World();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            w.Service.NotifyPatientAsync(Guid.NewGuid(), new NotificationDraft("K", "T", "M"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            w.Service.NotifyDoctorAsync(Guid.NewGuid(), new NotificationDraft("K", "T", "M"), CancellationToken.None));
    }

    [Fact]
    public async Task A_user_can_mark_only_their_own_notification_read()
    {
        var w = new World();
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        await w.Service.NotifyUserAsync(owner, new NotificationDraft("K", "T", "M"), CancellationToken.None);
        var id = w.Repository.Rows.Single().Id;

        var refused = await w.Service.MarkReadAsync(id, stranger, CancellationToken.None);
        var accepted = await w.Service.MarkReadAsync(id, owner, CancellationToken.None);

        Assert.Null(refused);
        Assert.NotNull(accepted);
        Assert.True(accepted!.ReadAtUtc is not null);
        Assert.Equal(0, await w.Service.CountUnreadAsync(owner, CancellationToken.None));
        Assert.Equal(0, await w.Service.CountUnreadAsync(stranger, CancellationToken.None));
    }

    [Fact]
    public async Task Mark_all_read_changes_only_the_users_unread_rows_and_reports_the_count()
    {
        var w = new World();
        var user = Guid.NewGuid();
        var other = Guid.NewGuid();
        await w.Service.NotifyUserAsync(user, new NotificationDraft("K", "T1", "M"), CancellationToken.None);
        await w.Service.NotifyUserAsync(user, new NotificationDraft("K", "T2", "M"), CancellationToken.None);
        await w.Service.NotifyUserAsync(other, new NotificationDraft("K", "T3", "M"), CancellationToken.None);

        var changed = await w.Service.MarkAllReadAsync(user, CancellationToken.None);

        Assert.Equal(2, changed);
        Assert.Equal(0, await w.Service.CountUnreadAsync(user, CancellationToken.None));
        Assert.Equal(1, await w.Service.CountUnreadAsync(other, CancellationToken.None));
    }

    [Fact]
    public async Task The_list_is_newest_first_per_user_and_paged()
    {
        var w = new World();
        var user = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            w.Clock.UtcNow = new DateTimeOffset(2026, 12, 1, 9, i, 0, TimeSpan.Zero);
            await w.Service.NotifyUserAsync(user, new NotificationDraft("K", $"Item {i}", "M"), CancellationToken.None);
        }
        await w.Service.NotifyUserAsync(Guid.NewGuid(), new NotificationDraft("K", "Not mine", "M"), CancellationToken.None);

        var page = await w.Service.GetForUserAsync(user, 1, 2, CancellationToken.None);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(new[] { "Item 4", "Item 3" }, page.Items.Select(item => item.Title));
    }

    [Fact]
    public async Task A_failing_provider_is_logged_and_swallowed_by_the_patient_publisher()
    {
        var w = new World();
        var provider = new FailingNotificationProvider();
        var patientId = Guid.NewGuid();
        w.Repository.PatientUsers[patientId] = Guid.NewGuid();
        var publisher = new SafePatientNotificationPublisher(
            new NotificationService(w.Repository, provider, w.UnitOfWork, w.Clock),
            NullLogger<SafePatientNotificationPublisher>.Instance);

        await publisher.PublishAsync(new PatientNotification(patientId, PatientNotificationKind.AppointmentReminder, "T", "M", null), CancellationToken.None);

        Assert.Equal(1, provider.Attempts);
        Assert.Empty(w.Repository.Rows);
    }

    [Fact]
    public async Task The_reminder_job_run_twice_on_the_same_day_leaves_one_row_per_appointment()
    {
        var w = new World();
        var patientId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        w.Repository.PatientUsers[patientId] = userId;
        var appointmentId = Guid.NewGuid();
        var reminders = new FakeReminderRepository(new ReminderCandidate(appointmentId, patientId, Today.AddDays(1), new TimeOnly(10, 0), "Dr. Mona"));
        var service = new AppointmentReminderService(reminders, w.Publisher, w.Clock);

        await service.SendDueRemindersAsync(CancellationToken.None);
        await service.SendDueRemindersAsync(CancellationToken.None);

        var row = Assert.Single(w.Repository.Rows);
        Assert.Equal("reminder:" + appointmentId.ToString("D"), row.DedupKey);
        Assert.Equal(userId, row.RecipientUserId);
        Assert.Equal("Appointment", row.RelatedEntityType);
        Assert.Equal(appointmentId, row.RelatedEntityId);
    }

    [Fact]
    public async Task Reminders_target_the_next_clinic_day_only()
    {
        var w = new World();
        var reminders = new FakeReminderRepository();
        var service = new AppointmentReminderService(reminders, w.Publisher, w.Clock);

        await service.SendDueRemindersAsync(CancellationToken.None);

        Assert.Equal(new[] { Today.AddDays(1) }, reminders.RequestedDates);
    }

    [Theory]
    [InlineData("2026-12-01T21:30:00Z", "2026-12-02")]
    [InlineData("2026-12-01T22:10:00Z", "2026-12-03")]
    public async Task The_day_before_is_decided_in_clinic_local_time_across_midnight(string utc, string expectedAppointmentDate)
    {
        // Africa/Cairo is UTC+2 in December: 21:30Z is 23:30 on Dec 1 locally, and 22:10Z is 00:10 on Dec 2.
        var timeProvider = new FixedTimeProvider(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));
        var clock = new SystemClinicClock(timeProvider, ClinicTimeZoneResolver.Resolve("Africa/Cairo"));
        var reminders = new FakeReminderRepository();
        var service = new AppointmentReminderService(reminders, new RecordingPatientNotificationPublisher(), clock);

        await service.SendDueRemindersAsync(CancellationToken.None);

        Assert.Equal(DateOnly.Parse(expectedAppointmentDate, System.Globalization.CultureInfo.InvariantCulture), Assert.Single(reminders.RequestedDates));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeReminderRepository(params ReminderCandidate[] candidates) : IAppointmentReminderRepository
    {
        public List<DateOnly> RequestedDates { get; } = [];

        public Task<IReadOnlyList<ReminderCandidate>> GetConfirmedForDateAsync(DateOnly appointmentDate, CancellationToken cancellationToken)
        {
            RequestedDates.Add(appointmentDate);
            IReadOnlyList<ReminderCandidate> due = candidates.Where(item => item.AppointmentDate == appointmentDate).ToArray();
            return Task.FromResult(due);
        }
    }

    private sealed class World
    {
        public World()
        {
            Repository = new InMemoryNotificationRepository();
            UnitOfWork = new InMemoryUnitOfWork();
            Clock = new FixedClock(new DateTimeOffset(Today.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero), Today);
            Provider = new InAppNotificationProvider(Repository, UnitOfWork, Clock);
            Service = new NotificationService(Repository, Provider, UnitOfWork, Clock);
            Publisher = new SafePatientNotificationPublisher(Service, NullLogger<SafePatientNotificationPublisher>.Instance);
        }

        public InMemoryNotificationRepository Repository { get; }
        public InMemoryUnitOfWork UnitOfWork { get; }
        public FixedClock Clock { get; }
        public InAppNotificationProvider Provider { get; }
        public NotificationService Service { get; }
        public IPatientNotificationPublisher Publisher { get; }
    }
}
