using BankingApi.Domain;

namespace BankingApi.Models;

public class LedgerEntry
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public string Type { get; set; } = string.Empty;

    public Guid? TransferId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Account Account { get; set; } = null!;
}