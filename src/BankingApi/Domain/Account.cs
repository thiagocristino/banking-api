namespace BankingApi.Domain;

public class Account
{
    public Guid Id { get; set; }

    public string AccountNumber { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public decimal Balance { get; set; }

    public DateTime CreatedAt { get; set; }

    public uint Version { get; set; }

    public ICollection<LedgerEntry> LedgerEntries { get; set; }
    = new List<LedgerEntry>();
}