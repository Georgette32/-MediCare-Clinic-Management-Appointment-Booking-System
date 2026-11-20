
namespace MediCare.Application.DTOs.Finance;

public sealed class FinanceSummaryDto
{
    public int TotalInvoices { get; set; }

    public int TotalPayments { get; set; }

    public decimal TotalRevenue { get; set; }

    public decimal TotalOutstanding { get; set; }
}
