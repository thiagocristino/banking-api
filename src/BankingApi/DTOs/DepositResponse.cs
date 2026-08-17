namespace BankingApi.DTOs;

public class DepositResponse
{
    public Guid AccountId { get; set; }

    public decimal Amount { get; set; }

    public decimal Balance { get; set; }

    public Guid LedgerEntryId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}