
using MediCare.Domain.Common;
using MediCare.Domain.Enums;
using System;

namespace MediCare.Domain.Entities;

public class Invoice : BaseEntity
{
    public Guid AppointmentId { get; private set; }

    public decimal TotalAmount { get; private set; }

    public decimal PaidAmount { get; private set; }

    public decimal RemainingAmount => TotalAmount - PaidAmount;

    public InvoiceStatus Status { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();
    private Invoice()
    {
    }

    public Invoice(Guid appointmentId, decimal totalAmount)
    {
        if (appointmentId == Guid.Empty)
            throw new ArgumentException("Appointment ID is required.");

        if (totalAmount <= 0)
            throw new ArgumentException("Invoice amount must be greater than zero.");

        AppointmentId = appointmentId;
        TotalAmount = totalAmount;
        PaidAmount = 0;
        Status = InvoiceStatus.Unpaid;
    }

    public void RecordPayment(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Payment amount must be greater than zero.");

        if (Status == InvoiceStatus.Cancelled)
            throw new InvalidOperationException("Cannot pay a cancelled invoice.");

        if (amount > RemainingAmount)
            throw new InvalidOperationException(
                "Payment amount cannot exceed the remaining balance.");

        PaidAmount += amount;

        Status = PaidAmount == TotalAmount
            ? InvoiceStatus.Paid
            : InvoiceStatus.PartiallyPaid;

        MarkUpdated();
    }

    public void Cancel()
    {
        if (Status == InvoiceStatus.Cancelled)
            throw new InvalidOperationException(
                "Invoice is already cancelled.");

        if (PaidAmount > 0)
            throw new InvalidOperationException(
                "Cannot cancel an invoice with recorded payments.");

        Status = InvoiceStatus.Cancelled;
        MarkUpdated();
    }
}
