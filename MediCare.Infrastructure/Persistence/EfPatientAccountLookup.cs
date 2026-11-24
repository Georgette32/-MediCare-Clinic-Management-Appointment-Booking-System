using MediCare.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Infrastructure.Persistence;

public sealed class EfPatientAccountLookup(MediCareDbContext dbContext) : IPatientAccountLookup
{
    public async Task<Guid?> FindApplicationUserIdAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var userId = await dbContext.Patients.AsNoTracking()
            .Where(patient => patient.Id == patientId)
            .Select(patient => (Guid?)patient.ApplicationUserId)
            .SingleOrDefaultAsync(cancellationToken);
        return userId;
    }
}
