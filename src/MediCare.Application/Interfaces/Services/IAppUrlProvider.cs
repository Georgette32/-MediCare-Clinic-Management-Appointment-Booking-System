namespace MediCare.Application.Interfaces.Services;

/// <summary>
/// Builds absolute links for emails. Implemented in the Web layer because only the
/// presentation host knows its public scheme/host.
/// </summary>
public interface IAppUrlProvider
{
    string EmailConfirmationUrl(Guid userId, string token);

    string PasswordResetUrl(Guid userId, string token);

    string LoginUrl();
}
