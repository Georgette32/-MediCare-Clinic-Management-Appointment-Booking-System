
using MediCare.Application.DTOs.Finance;
using MediCare.Domain.Enums;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Application.Interfaces.Services;

public interface IFinanceService
{
    Task<bool> CreateInvoiceAsync(
        Guid appointmentId,
        decimal totalAmount,
        CancellationToken cancellationToken = default);

    Task<bool> RecordPaymentAsync(
        Guid invoiceId,
        decimal amount,
        PaymentMethod method,
        CancellationToken cancellationToken = default);

    Task<FinanceSummaryDto> GetFinanceSummaryAsync(
        CancellationToken cancellationToken = default);

    Task<InvoiceDetailsDto?> GetInvoiceDetailsAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);
}
