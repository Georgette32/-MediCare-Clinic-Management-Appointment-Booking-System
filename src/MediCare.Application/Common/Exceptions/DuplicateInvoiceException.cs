using System;

namespace MediCare.Application.Common.Exceptions;

public sealed class DuplicateInvoiceException : Exception
{
    public DuplicateInvoiceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}