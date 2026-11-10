using MediCare.Application.Common;
using MediCare.Application.DTOs.Auth;
using MediCare.Application.Interfaces.Identity;

namespace MediCare.Application.Interfaces.Services;

public interface IAuthService
{
    bool RequiresEmailConfirmation { get; }

    Task<Result> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken cancellationToken = default);

    Task<Result> RegisterDoctorAsync(RegisterDoctorRequest request, CancellationToken cancellationToken = default);

    Task<SignInOutcome> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task LogoutAsync();

    Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);

    Task<Result> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);

    Task<Result<ProfileDto>> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
}
