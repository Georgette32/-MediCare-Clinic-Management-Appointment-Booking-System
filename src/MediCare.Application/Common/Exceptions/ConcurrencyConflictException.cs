
using System;

namespace MediCare.Application.Common.Exceptions;

public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
