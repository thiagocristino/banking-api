namespace BankingApi.DTOs;

public class TransferRequest
{
    public string DestinationAccountNumber { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}