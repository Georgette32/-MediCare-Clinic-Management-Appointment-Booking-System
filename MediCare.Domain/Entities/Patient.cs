namespace MediCare.Domain.Entities;

public sealed class Patient
{
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
}
