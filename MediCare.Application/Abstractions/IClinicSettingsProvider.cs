using MediCare.Domain.Entities;

namespace MediCare.Application.Abstractions;

/// <summary>Read access to clinic-wide settings used by the scheduling engine.</summary>
public interface IClinicSettingsProvider
{
    /// <summary>The configured minimum consultation sample size, or null when unset (no hardcoded default).</summary>
    Task<int?> GetMinimumConsultationSampleSizeAsync(CancellationToken cancellationToken);
}

/// <summary>Persistence for the single ClinicSettings row. The row is created on first save.</summary>
public interface IClinicSettingsRepository
{
    Task<ClinicSettings?> GetAsync(CancellationToken cancellationToken);
    Task AddAsync(ClinicSettings settings, CancellationToken cancellationToken);
}
