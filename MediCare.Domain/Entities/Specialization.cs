namespace MediCare.Domain.Entities;

public sealed class Specialization
{
    private Specialization() { }

    public Specialization(Guid id, string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A specialization name is required.", nameof(name));

        Id = id;
        Name = name;
        Description = description;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
}
