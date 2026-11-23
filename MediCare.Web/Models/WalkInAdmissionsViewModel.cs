using MediCare.Application.Services;

namespace MediCare.Web.Models;

public sealed record WalkInAdmissionsViewModel(IReadOnlyList<PendingWalkInAdmissionRequest> Requests);
