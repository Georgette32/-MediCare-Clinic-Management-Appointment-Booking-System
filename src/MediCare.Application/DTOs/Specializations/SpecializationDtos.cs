namespace MediCare.Application.DTOs.Specializations;

public sealed class SpecializationDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public int DoctorCount { get; set; }
}

public sealed class CreateSpecializationRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}

public sealed class UpdateSpecializationRequest
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
