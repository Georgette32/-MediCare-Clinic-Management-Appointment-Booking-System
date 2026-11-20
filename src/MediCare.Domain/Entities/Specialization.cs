using MediCare.Domain.Common;
using MediCare.Domain.Exceptions;

namespace MediCare.Domain.Entities;

public sealed class Specialization : BaseEntity
{
    private readonly List<DoctorProfile> _doctors = new();

    private Specialization()
    {
        // Required by EF Core.
    }

    public Specialization(string name, string? description = null)
    {
        SetName(name);
        Description = Normalize(description);
    }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<DoctorProfile> Doctors => _doctors;

    public void Update(string name, string? description)
    {
        SetName(name);
        Description = Normalize(description);
        MarkUpdated();
    }

    public void Activate()
    {
        IsActive = true;
        MarkUpdated();
    }

    public void Deactivate()
    {
        IsActive = false;
        MarkUpdated();
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Specialization name is required.");
        }

        if (name.Trim().Length > 120)
        {
            throw new DomainException("Specialization name cannot exceed 120 characters.");
        }

        Name = name.Trim();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
