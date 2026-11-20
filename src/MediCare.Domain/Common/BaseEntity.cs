namespace MediCare.Domain.Common;

/// <summary>
/// Base type for every persisted domain entity. Audit fields are maintained by the
/// domain itself so that infrastructure (EF Core) never owns business state.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; private set; }

    public void MarkUpdated() => UpdatedAtUtc = DateTime.UtcNow;
}
