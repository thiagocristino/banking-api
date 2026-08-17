namespace BankingApi.DTOs;

public class AccountResponse
{
    public Guid Id { get; set; }

    public string AccountNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
