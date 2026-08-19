using BankingApi.Data;
using BankingApi.Domain;
using BankingApi.DTOs;
using BankingApi.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BankingApi.Services;

public class ReversalService
{
    private readonly BankingDbContext _db;

    public ReversalService(BankingDbContext db)
    {
        _db = db;
    }

    public async Task<ReversalResponse> ReverseAsync(
        Guid sourceAccountId,
        Guid transferId)
    {
        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        // 1. Buscar transferência
        var transfer = await _db.Transfers
            .FirstOrDefaultAsync(x =>
                x.Id == transferId);

        if (transfer is null)
        {
            await transaction.RollbackAsync();

            throw new BusinessException(
                "TRANSFER_NOT_FOUND",
                "The transfer was not found.",
                404);
        }

        // 2. Garantir que somente a conta de origem
        // possa solicitar o estorno
        if (transfer.SourceAccountId != sourceAccountId)
        {
            await transaction.RollbackAsync();

            throw new BusinessException(
                "TRANSFER_ACCESS_DENIED",
                "The authenticated account is not the source account of this transfer.",
                403);
        }

        // 3. Transferência só pode ser estornada uma vez
        if (transfer.Status == TransferStatus.Reversed)
        {
            await transaction.RollbackAsync();

            throw new BusinessException(
                "TRANSFER_ALREADY_REVERSED",
                "The transfer has already been reversed.",
                409);
        }

        // 4. Buscar conta origem
        var sourceAccount = await _db.Accounts
            .FirstOrDefaultAsync(x =>
                x.Id == transfer.SourceAccountId);

        if (sourceAccount is null)
        {
            await transaction.RollbackAsync();

            throw new BusinessException(
                "SOURCE_ACCOUNT_NOT_FOUND",
                "The source account was not found.",
                404);
        }

        // 5. Buscar conta destino
        var destinationAccount = await _db.Accounts
            .FirstOrDefaultAsync(x =>
                x.Id == transfer.DestinationAccountId);

        if (destinationAccount is null)
        {
            await transaction.RollbackAsync();

            throw new BusinessException(
                "DESTINATION_ACCOUNT_NOT_FOUND",
                "The destination account was not found.",
                404);
        }

        // 6. A conta destino precisa ter saldo
        // suficiente para devolver o dinheiro.
        if (destinationAccount.Balance < transfer.Amount)
        {
            await transaction.RollbackAsync();

            throw new BusinessException(
                "INSUFFICIENT_FUNDS_FOR_REVERSAL",
                "The destination account does not have sufficient funds for the reversal.",
                409);
        }

        var reversedAt = DateTime.UtcNow;

        // 7. Devolver o dinheiro para a conta origem
        sourceAccount.Balance += transfer.Amount;

        // 8. Retirar o dinheiro da conta destino
        destinationAccount.Balance -= transfer.Amount;

        // 9. Atualizar versões
        sourceAccount.Version++;
        destinationAccount.Version++;

        // 10. Alterar somente o status da transferência.
        // O lançamento original permanece intacto.
        transfer.Status = TransferStatus.Reversed;
        transfer.ReversedAt = reversedAt;

        // 11. Criar lançamento de crédito na origem
        var sourceLedger = new LedgerEntry
        {
            Id = Guid.NewGuid(),

            AccountId = sourceAccount.Id,

            Amount = transfer.Amount,

            BalanceAfter = sourceAccount.Balance,

            Type = "TRANSFER_REVERSAL_CREDIT",

            TransferId = transfer.Id,

            CreatedAtUtc = reversedAt
        };

        // 12. Criar lançamento de débito no destino
        var destinationLedger = new LedgerEntry
        {
            Id = Guid.NewGuid(),

            AccountId = destinationAccount.Id,

            Amount = -transfer.Amount,

            BalanceAfter = destinationAccount.Balance,

            Type = "TRANSFER_REVERSAL_DEBIT",

            TransferId = transfer.Id,

            CreatedAtUtc = reversedAt
        };

        _db.LedgerEntries.Add(sourceLedger);
        _db.LedgerEntries.Add(destinationLedger);

        // 13. Persistir tudo atomicamente
        try
        {
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch
            {
                // A transação pode já ter sido revertida
                // pelo banco.
            }

            throw new BusinessException(
                "CONCURRENT_MODIFICATION",
                "The transfer or one of the accounts was modified by another transaction. Please retry.",
                409);
        }

        // 14. Retornar resultado
        return new ReversalResponse
        {
            TransferId = transfer.Id,

            SourceAccountId =
                sourceAccount.Id,

            SourceAccountNumber =
                sourceAccount.AccountNumber,

            DestinationAccountId =
                destinationAccount.Id,

            DestinationAccountNumber =
                destinationAccount.AccountNumber,

            Amount = transfer.Amount,

            SourceBalance =
                sourceAccount.Balance,

            DestinationBalance =
                destinationAccount.Balance,

            Status =
                transfer.Status.ToString(),

            ReversedAt = reversedAt
        };
    }
}