using System.Text.Json;
using BankingApi.Data;
using BankingApi.Domain;
using BankingApi.DTOs;
using BankingApi.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BankingApi.Services;

public class TransferService
{
    private readonly BankingDbContext _db;

    public TransferService(BankingDbContext db)
    {
        _db = db;
    }

    public async Task<TransferResponse> CreateAsync(
        Guid sourceAccountId,
        TransferRequest request,
        string idempotencyKey)
    {
        // 1. Validar Idempotency-Key
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BusinessException(
                "IDEMPOTENCY_KEY_REQUIRED",
                "The Idempotency-Key header is required.",
                400);
        }

        // 2. Validar valor
        if (request.Amount <= 0)
        {
            throw new BusinessException(
                "INVALID_AMOUNT",
                "The transfer amount must be greater than zero.",
                400);
        }

        // 3. Validar conta destino
        if (string.IsNullOrWhiteSpace(
            request.DestinationAccountNumber))
        {
            throw new BusinessException(
                "DESTINATION_ACCOUNT_REQUIRED",
                "The destination account number is required.",
                400);
        }

        var destinationAccountNumber =
            request.DestinationAccountNumber.Trim();

        // 4. Gerar hash da requisição
        var requestHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    $"{destinationAccountNumber}|{request.Amount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)}")));

        // 5. Iniciar transação
        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        // 6. Verificar idempotência
        var existingRequest =
            await _db.IdempotencyRequests
                .FirstOrDefaultAsync(x =>
                    x.AccountId == sourceAccountId &&
                    x.Key == idempotencyKey);

        if (existingRequest is not null)
        {
            if (existingRequest.RequestHash != requestHash)
            {
                await transaction.RollbackAsync();

                throw new BusinessException(
                    "IDEMPOTENCY_KEY_REUSED",
                    "The Idempotency-Key was already used with a different request.",
                    409);
            }

            var previousResponse =
                JsonSerializer.Deserialize<TransferResponse>(
                    existingRequest.ResponseBody);

            if (previousResponse is null)
            {
                await transaction.RollbackAsync();

                throw new BusinessException(
                    "IDEMPOTENCY_RESPONSE_INVALID",
                    "The stored idempotency response is invalid.",
                    500);
            }

            await transaction.RollbackAsync();

            return previousResponse;
        }

        // 7. Buscar conta origem
        var sourceAccount = await _db.Accounts
            .FirstOrDefaultAsync(x =>
                x.Id == sourceAccountId);

        if (sourceAccount is null)
        {
            await transaction.RollbackAsync();

            throw new BusinessException(
                "SOURCE_ACCOUNT_NOT_FOUND",
                "The source account was not found.",
                404);
        }

        // 8. Buscar conta destino
        var destinationAccount = await _db.Accounts
            .FirstOrDefaultAsync(x =>
                x.AccountNumber == destinationAccountNumber);

        if (destinationAccount is null)
        {
            await transaction.RollbackAsync();

            throw new BusinessException(
                "DESTINATION_ACCOUNT_NOT_FOUND",
                "The destination account was not found.",
                404);
        }

        // 9. Não permitir transferência para a própria conta
        if (sourceAccount.Id == destinationAccount.Id)
        {
            await transaction.RollbackAsync();

            throw new BusinessException(
                "SELF_TRANSFER_NOT_ALLOWED",
                "Transfers to the same account are not allowed.",
                400);
        }

        // 10. Verificar saldo
        if (sourceAccount.Balance < request.Amount)
        {
            await transaction.RollbackAsync();

            throw new BusinessException(
                "INSUFFICIENT_FUNDS",
                "Insufficient funds.",
                422);
        }

        // 11. Débito da conta origem
        sourceAccount.Balance -= request.Amount;

        // 12. Crédito da conta destino
        destinationAccount.Balance += request.Amount;

        // 13. Atualizar versão para controle de concorrência
        sourceAccount.Version++;
        destinationAccount.Version++;

        // 14. Criar transferência
        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),

            SourceAccountId = sourceAccount.Id,

            DestinationAccountId = destinationAccount.Id,

            Amount = request.Amount,

            Status = TransferStatus.Completed,

            CreatedAt = DateTime.UtcNow
        };

        _db.Transfers.Add(transfer);

        // 15. Criar Ledger da conta origem
        var debitLedger = new LedgerEntry
        {
            Id = Guid.NewGuid(),

            AccountId = sourceAccount.Id,

            Amount = -request.Amount,

            BalanceAfter = sourceAccount.Balance,

            Type = "TRANSFER_DEBIT",

            TransferId = transfer.Id,

            CreatedAtUtc = DateTime.UtcNow
        };

        // 16. Criar Ledger da conta destino
        var creditLedger = new LedgerEntry
        {
            Id = Guid.NewGuid(),

            AccountId = destinationAccount.Id,

            Amount = request.Amount,

            BalanceAfter = destinationAccount.Balance,

            Type = "TRANSFER_CREDIT",

            TransferId = transfer.Id,

            CreatedAtUtc = DateTime.UtcNow
        };

        _db.LedgerEntries.Add(debitLedger);
        _db.LedgerEntries.Add(creditLedger);

        // 17. Criar resposta
        var response = new TransferResponse
        {
            TransferId = transfer.Id,

            SourceAccountId = sourceAccount.Id,

            SourceAccountNumber =
                sourceAccount.AccountNumber,

            DestinationAccountId =
                destinationAccount.Id,

            DestinationAccountNumber =
                destinationAccount.AccountNumber,

            Amount = request.Amount,

            SourceBalance = sourceAccount.Balance,

            Status = transfer.Status.ToString(),

            CreatedAt = transfer.CreatedAt
        };

        // 18. Registrar idempotência
        var idempotencyRequest = new IdempotencyRequest
        {
            Id = Guid.NewGuid(),

            Key = idempotencyKey,

            AccountId = sourceAccount.Id,

            RequestHash = requestHash,

            ResponseStatusCode = 200,

            ResponseBody =
                JsonSerializer.Serialize(response),

            CreatedAt = DateTime.UtcNow
        };

        _db.IdempotencyRequests.Add(
            idempotencyRequest);

        // 19. Persistir tudo com controle de concorrência
        try
        {
            await _db.SaveChangesAsync();

            // 20. Confirmar transação
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
                // A transação pode já ter sido
                // revertida pelo banco de dados.
            }

            // Deixa o ConcurrencyExceptionHandler
            // tratar a exceção e retornar HTTP 409.
            throw;
        }

        // 21. Retornar resposta
        return response;
    }
}