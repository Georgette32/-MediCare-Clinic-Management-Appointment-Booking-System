
using MediCare.Domain.Enums;
using System;
using System.Collections.Generic;

namespace MediCare.Application.DTOs.Finance;

public sealed class InvoiceDetailsDto
{
	public Guid Id { get; set; }

	public Guid AppointmentId { get; set; }

	public decimal TotalAmount { get; set; }

	public decimal PaidAmount { get; set; }

	public decimal RemainingAmount { get; set; }

	public InvoiceStatus Status { get; set; }

	public List<PaymentDetailsDto> Payments { get; set; } = new();
}
