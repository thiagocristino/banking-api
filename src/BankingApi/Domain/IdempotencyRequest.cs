namespace BankingApi.Domain;

public class IdempotencyRequest
{
    public Guid Id { get; set; }

    public string Key { get; set; } = null!;

    public Guid AccountId { get; set; }

    public string RequestHash { get; set; } = null!;

    public int ResponseStatusCode { get; set; }

    public string ResponseBody { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
