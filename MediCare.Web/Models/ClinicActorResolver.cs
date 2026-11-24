using System.Security.Claims;
using MediCare.Application.Enums;
using MediCare.Infrastructure.Identity;

namespace MediCare.Web.Models;

/// <summary>
/// Maps the signed-in role to the actor recorded on clinic cancellations and reschedules. The actor is never read from a
/// form. Admin wins when an account somehow holds both roles, so the recorded actor is the higher-privilege one.
/// </summary>
public static class ClinicActorResolver
{
    public static ClinicCancellationActor From(ClaimsPrincipal user)
    {
        if (user.IsInRole(MediCareRoles.Admin))
            return ClinicCancellationActor.Admin;
        if (user.IsInRole(MediCareRoles.Receptionist))
            return ClinicCancellationActor.Reception;
        throw new UnauthorizedAccessException("Only Reception or Admin can perform clinic appointment actions.");
    }
}
