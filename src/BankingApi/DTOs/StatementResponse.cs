namespace BankingApi.DTOs;

public class StatementResponse
{
    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public decimal OpeningBalance { get; set; }

    public decimal ClosingBalance { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalEntries { get; set; }

    public int TotalPages { get; set; }

    public List<StatementEntryResponse> Entries { get; set; }
        = new();
}

public class StatementEntryResponse
{
    public Guid Id { get; set; }

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public string Type { get; set; } = string.Empty;

    public Guid? TransferId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}