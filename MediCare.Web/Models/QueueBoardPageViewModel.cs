using MediCare.Application.Services;

namespace MediCare.Web.Models;

/// <summary>Shared page model for the Reception and Doctor queue boards. Formats UTC instants in the clinic zone.</summary>
public sealed class QueueBoardPageViewModel
{
    public IReadOnlyList<DoctorChoice> Doctors { get; init; } = [];
    public Guid? SelectedDoctorId { get; init; }
    public DoctorQueueBoard? Board { get; init; }
    public DateOnly ClinicDate { get; init; }
    public TimeZoneInfo ClinicZone { get; init; } = TimeZoneInfo.Utc;

    public string Local(DateTimeOffset? utc) =>
        utc is null ? "-" : TimeZoneInfo.ConvertTime(utc.Value, ClinicZone).ToString(Clock.Format, Clock.Culture);

    public static string Time(TimeOnly? time) => time is null ? "-" : time.Value.ToString(Clock.Format, Clock.Culture);
}
