
using MediCare.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Application.Interfaces.Persistence;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Payment>> GetByInvoiceIdAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Payment payment,
        CancellationToken cancellationToken = default);

    Task<int> CountCompletedAsync(
        CancellationToken cancellationToken = default);

    Task<decimal> GetTotalRevenueAsync(
        CancellationToken cancellationToken = default);
}
