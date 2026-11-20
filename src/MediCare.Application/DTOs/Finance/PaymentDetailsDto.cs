
using MediCare.Domain.Enums;
using System;

namespace MediCare.Application.DTOs.Finance;

public sealed class PaymentDetailsDto
{
    public Guid Id { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; }

    public PaymentStatus Status { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    public string? TransactionReference { get; set; }
}
