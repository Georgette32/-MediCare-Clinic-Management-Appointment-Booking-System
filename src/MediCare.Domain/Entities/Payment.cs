
using MediCare.Domain.Enums;
using System;

namespace MediCare.Domain.Entities;

public class Payment : BaseEntity
{
    public Guid InvoiceId { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentMethod Method { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTime? PaidAtUtc { get; private set; }

    public string? TransactionReference { get; private set; }

    private Payment()
    {
    }

    public Payment(
        Guid invoiceId,
        decimal amount,
        PaymentMethod method)
    {
        if (invoiceId == Guid.Empty)
            throw new ArgumentException("Invoice ID is required.");

        if (amount <= 0)
            throw new ArgumentException("Payment amount must be greater than zero.");

        InvoiceId = invoiceId;
        Amount = amount;
        Method = method;
        Status = PaymentStatus.Pending;
    }

    public void Complete(string? transactionReference = null)
    {
        if (Status != PaymentStatus.Pending)
            throw new InvalidOperationException(
                "Only pending payments can be completed.");

        Status = PaymentStatus.Completed;
        PaidAtUtc = DateTime.UtcNow;
        TransactionReference = transactionReference;

        MarkUpdated();
    }

    public void Fail()
    {
        if (Status != PaymentStatus.Pending)
            throw new InvalidOperationException(
                "Only pending payments can be failed.");

        Status = PaymentStatus.Failed;
        MarkUpdated();
    }

    public void Refund()
    {
        if (Status != PaymentStatus.Completed)
            throw new InvalidOperationException(
                "Only completed payments can be refunded.");

        Status = PaymentStatus.Refunded;
        MarkUpdated();
    }
}
