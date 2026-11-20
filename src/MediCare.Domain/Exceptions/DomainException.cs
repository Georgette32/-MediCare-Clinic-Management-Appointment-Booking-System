namespace MediCare.Domain.Exceptions;

/// <summary>
/// Raised when an operation would violate a domain invariant. Infrastructure maps
/// this to a validation problem; it should never be used for flow control.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
