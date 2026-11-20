
using MediCare.Application.DTOs.Finance;
using MediCare.Application.Interfaces.Persistence;
using MediCare.Application.Interfaces.Services;
using MediCare.Domain.Entities;
using MediCare.Domain.Enums;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediCare.Application.Common.Exceptions;

namespace MediCare.Application.Services;

public sealed class FinanceService : IFinanceService
{
    private readonly IInvoiceRepository _invoices;
    private readonly IPaymentRepository _payments;
    private readonly IUnitOfWork _unitOfWork;

    public FinanceService(
        IInvoiceRepository invoices,
        IPaymentRepository payments,
        IUnitOfWork unitOfWork)
    {
        _invoices = invoices;
        _payments = payments;
        _unitOfWork = unitOfWork;
    }


    public async Task<bool> CreateInvoiceAsync(
        Guid appointmentId,
        decimal totalAmount,
        CancellationToken cancellationToken = default)
    {
        if (appointmentId == Guid.Empty || totalAmount <= 0)
            return false;

        var existingInvoice = await _invoices.GetByAppointmentIdAsync(
            appointmentId,
            cancellationToken);

        if (existingInvoice is not null)
            return false;

        var invoice = new Invoice(appointmentId, totalAmount);

        try
        {
            await _invoices.AddAsync(invoice, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DuplicateInvoiceException)
        {
            return false;
        }
    }


    public async Task<bool> RecordPaymentAsync(
        Guid invoiceId,
        decimal amount,
        PaymentMethod method,
        CancellationToken cancellationToken = default)
    {
        if (invoiceId == Guid.Empty ||
            amount <= 0 ||
            !Enum.IsDefined(typeof(PaymentMethod), method))
        {
            return false;
        }

        var invoice = await _invoices.GetByIdAsync(
            invoiceId,
            cancellationToken);

        if (invoice is null ||
            invoice.Status == InvoiceStatus.Cancelled ||
            amount > invoice.RemainingAmount)
        {
            return false;
        }

        var payment = new Payment(invoiceId, amount, method);
        payment.Complete();

        invoice.RecordPayment(amount);

        try
        {
            await _payments.AddAsync(payment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (ConcurrencyConflictException)
        {
            return false;
        }
    }


    public async Task<FinanceSummaryDto> GetFinanceSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var totalInvoices = await _invoices.CountAsync(cancellationToken);

        var totalPayments = await _payments.CountCompletedAsync(
            cancellationToken);

        var totalRevenue = await _payments.GetTotalRevenueAsync(
            cancellationToken);

        var totalOutstanding = await _invoices.GetTotalOutstandingAsync(
            cancellationToken);

        return new FinanceSummaryDto
        {
            TotalInvoices = totalInvoices,
            TotalPayments = totalPayments,
            TotalRevenue = totalRevenue,
            TotalOutstanding = totalOutstanding
        };
    }

    public async Task<InvoiceDetailsDto?> GetInvoiceDetailsAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        if (invoiceId == Guid.Empty)
            return null;

        var invoice = await _invoices.GetByIdAsync(
            invoiceId,
            cancellationToken);

        if (invoice is null)
            return null;

        var payments = await _payments.GetByInvoiceIdAsync(
            invoiceId,
            cancellationToken);

        return new InvoiceDetailsDto
        {
            Id = invoice.Id,
            AppointmentId = invoice.AppointmentId,
            TotalAmount = invoice.TotalAmount,
            PaidAmount = invoice.PaidAmount,
            RemainingAmount = invoice.RemainingAmount,
            Status = invoice.Status,

            Payments = payments.Select(payment => new PaymentDetailsDto
            {
                Id = payment.Id,
                Amount = payment.Amount,
                Method = payment.Method,
                Status = payment.Status,
                PaidAtUtc = payment.PaidAtUtc,
                TransactionReference = payment.TransactionReference
            }).ToList()
        };
    }
}
