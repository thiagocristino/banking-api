namespace BankingApi.Domain;

public enum LedgerEntryType
{
    Deposit = 1,

    TransferDebit = 2,

    TransferCredit = 3,

    ReversalDebit = 4,

    ReversalCredit = 5
}
