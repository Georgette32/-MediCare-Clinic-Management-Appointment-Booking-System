using MediCare.Application.Common;
using MediCare.Application.DTOs.Patients;

namespace MediCare.Application.Interfaces.Services;

public interface IPatientProfileService
{
    Task<Result<PatientProfileDto>> GetMyProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> UpdateMyProfileAsync(Guid userId, UpdatePatientProfileRequest request, CancellationToken cancellationToken = default);
}
