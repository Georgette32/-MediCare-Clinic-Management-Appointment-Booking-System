namespace MediCare.Domain.Entities;

public sealed class Patient
{
    public const int MaxFullNameLength = 200;

    private Patient() { }

    public Patient(Guid id, Guid applicationUserId, string fullName, string phoneNumber)
    {
        Id = id;
        ApplicationUserId = applicationUserId;
        FullName = fullName;
        PhoneNumber = phoneNumber;
    }

    public Guid Id { get; private set; }
    public Guid ApplicationUserId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;

    /// <summary>Changes the display name. Blank names and names longer than <see cref="MaxFullNameLength"/> are refused.</summary>
    public void ChangeFullName(string fullName)
    {
        var trimmed = fullName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("A full name is required.", nameof(fullName));
        if (trimmed.Length > MaxFullNameLength)
            throw new ArgumentException($"The full name cannot exceed {MaxFullNameLength} characters.", nameof(fullName));
        FullName = trimmed;
    }
}
