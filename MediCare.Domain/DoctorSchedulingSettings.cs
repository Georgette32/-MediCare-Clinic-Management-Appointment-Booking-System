namespace MediCare.Domain;

public sealed class DoctorSchedulingSettings
{
    public string DoctorId { get; set; } = "";
    public string? PublicDisplayName { get; set; }
    public string? Specialization { get; set; }
    public decimal? ConsultationFee { get; set; }
    public int DefaultConsultationDurationMinutes { get; set; } = 20;
    public decimal GracePeriodMultiplier { get; set; } = 2m;
    public int CallTimeoutMinutes { get; set; } = 5;
    public int WaitingRoomCapacity { get; set; } = 3;
    public decimal SafetyBufferMultiplier { get; set; } = 0.5m;
    public int MinimumHistorySamples { get; set; } = 5;
    public int BookedCapacityReservation { get; set; } = 1;
    public byte[] RowVersion { get; set; } = [];
}
