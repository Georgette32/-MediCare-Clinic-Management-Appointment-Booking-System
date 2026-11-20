
using MediCare.Application.Common.Security;
using MediCare.Application.Interfaces.Services;
using MediCare.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediCare.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.AdminOnly)]
public class FinanceController : Controller
{
    private readonly IFinanceService _finance;

    public FinanceController(IFinanceService finance)
    {
        _finance = finance;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var summary = await _finance.GetFinanceSummaryAsync(
            cancellationToken);

        return Ok(summary);
    }

    [HttpGet]
    public async Task<IActionResult> InvoiceDetails(
        Guid id,
        CancellationToken cancellationToken)
    {
        var invoice = await _finance.GetInvoiceDetailsAsync(
            id,
            cancellationToken);

        if (invoice is null)
            return NotFound();

        return Ok(invoice);
    }

    [HttpPost]
    public async Task<IActionResult> RecordPayment(
        Guid invoiceId,
        decimal amount,
        PaymentMethod method,
        CancellationToken cancellationToken)
    {
        var succeeded = await _finance.RecordPaymentAsync(
            invoiceId,
            amount,
            method,
            cancellationToken);

        if (!succeeded)
        {
            return BadRequest(new
            {
                success = false,
                message = "Unable to record payment."
            });
        }

        return Ok(new
        {
            success = true,
            message = "Payment recorded successfully."
        });
    }
}
