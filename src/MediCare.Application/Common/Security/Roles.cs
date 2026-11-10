namespace MediCare.Application.Common.Security;

/// <summary>
/// Canonical role names. Registration only ever grants Patient or Doctor; the Admin
/// role can only be granted through the administrative user-management use case.
/// </summary>
public static class Roles
{
    public const string Patient = "Patient";
    public const string Doctor = "Doctor";
    public const string Admin = "Admin";

    public static readonly string[] All = { Patient, Doctor, Admin };

    public static bool IsValid(string? role) =>
        !string.IsNullOrWhiteSpace(role) && All.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string role) =>
        All.First(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
    public const string ApprovedDoctor = "ApprovedDoctor";
    public const string PatientOnly = "PatientOnly";
}
