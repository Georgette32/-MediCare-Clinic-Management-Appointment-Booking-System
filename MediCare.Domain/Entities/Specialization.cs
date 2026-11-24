namespace MediCare.Domain.Entities;

public sealed class Specialization
{
    public const int MaxNameLength = 120;

    private Specialization() { }

    public Specialization(Guid id, string name, string? description = null)
    {
        Id = id;
        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    public void Update(string name, string? description)
    {
        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
    }

    private static string NormalizeName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("A specialization name is required.", nameof(name));
        if (trimmed.Length > MaxNameLength)
            throw new ArgumentException($"A specialization name cannot exceed {MaxNameLength} characters.", nameof(name));
        return trimmed;
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
