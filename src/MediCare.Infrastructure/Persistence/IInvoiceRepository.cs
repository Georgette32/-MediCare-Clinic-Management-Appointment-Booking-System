
using MediCare.Domain.Entities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Application.Interfaces.Persistence;

public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Invoice?> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Invoice invoice,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        CancellationToken cancellationToken = default);

    Task<decimal> GetTotalOutstandingAsync(
        CancellationToken cancellationToken = default);
}
