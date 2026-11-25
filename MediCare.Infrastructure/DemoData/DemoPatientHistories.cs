using System.Text.Json;
using MediCare.Domain.Entities;

namespace MediCare.Infrastructure.DemoData;

/// <summary>One long-running problem of a patient, seen by one specialization, as visits in chronological order.</summary>
internal sealed record DemoCondition(string Specialization, DemoClinicalCase[] Visits);

/// <summary>
/// The medical file of one demo patient: who they are, their past history before joining the clinic, and the
/// conditions each specialization follows. Profiles are matched to patients by creation order (<see cref="Index"/>).
/// </summary>
internal sealed record DemoPatientProfile(int Index, int Age, string Sex, bool Child, string Background, DemoCondition[] Conditions)
{
    public DemoCondition? ConditionFor(string specialization) =>
        Conditions.FirstOrDefault(item => string.Equals(item.Specialization, specialization, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Patient medical files and acute visit pools for the demo data set, read from the embedded
/// <c>patient-histories.json</c>. The content was drafted with Gemini and checked against the domain limits by the tests.
/// </summary>
internal static class DemoPatientHistories
{
    private const string ResourceName = "MediCare.Infrastructure.DemoData.patient-histories.json";

    private static readonly Lazy<Data> Loaded = new(Load);

    public static IReadOnlyList<DemoPatientProfile> Patients => Loaded.Value.Patients;

    /// <summary>Self-limited visits per specialization, used when the patient's file has nothing for that specialization.</summary>
    public static IReadOnlyDictionary<string, DemoClinicalCase[]> Acute => Loaded.Value.Acute;

    private static Data Load()
    {
        using var stream = typeof(DemoPatientHistories).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = JsonSerializer.Deserialize<Data>(stream, options)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is empty.");
        return data with
        {
            Acute = new Dictionary<string, DemoClinicalCase[]>(data.Acute, StringComparer.OrdinalIgnoreCase),
            Patients = data.Patients.OrderBy(item => item.Index).ToArray()
        };
    }

    private sealed record Data(DemoPatientProfile[] Patients, Dictionary<string, DemoClinicalCase[]> Acute);
}
