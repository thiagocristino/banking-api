namespace BankingApi.DTOs;

public class TransferResponse
{
    public Guid TransferId { get; set; }

    public Guid SourceAccountId { get; set; }

    public string SourceAccountNumber { get; set; } = string.Empty;

    public Guid DestinationAccountId { get; set; }

    public string DestinationAccountNumber { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public decimal SourceBalance { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
