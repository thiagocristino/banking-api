namespace BankingApi.Domain;

public class Transfer
{
    public Guid Id { get; set; }

    public Guid SourceAccountId { get; set; }

    public Guid DestinationAccountId { get; set; }

    public decimal Amount { get; set; }

    public TransferStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ReversedAt { get; set; }
}
