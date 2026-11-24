namespace MediCare.Application.Services;

public interface IReceptionQueueService
{
    /// <summary>Calls a Waiting scheduled attendance attempt. Throws InvalidOperationException with the refusal reason.</summary>
    Task<string> CallScheduledAsync(Guid queueEntryId, CancellationToken cancellationToken);

    /// <summary>Picks the next patient for the doctor today: earliest callable scheduled patient, otherwise the walk-in FIFO head.</summary>
    Task<CallNextResult> CallNextAsync(Guid doctorId, CancellationToken cancellationToken);

    /// <summary>Called -> Waiting. A walk-in's consumed approval stays used, so a new approval is needed to call it again.</summary>
    Task<string> ReturnToWaitingAsync(Guid queueEntryId, CancellationToken cancellationToken);

    /// <summary>Waiting -> Cancelled ("patient left"). The appointment, if any, stays Confirmed for a later check-in.</summary>
    Task<string> CancelQueueEntryAsync(Guid queueEntryId, CancellationToken cancellationToken);

    Task<PatientLookupResult> FindPatientByPhoneAsync(string input, CancellationToken cancellationToken);
}

public sealed record CallNextResult(bool Called, Guid? QueueEntryId, string Message);

public sealed record PatientLookupResult(Guid? PatientId, string? Error, bool NotRegistered);
