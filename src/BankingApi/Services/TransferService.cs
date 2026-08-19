using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BankingApi.Data;
using BankingApi.Domain;
using BankingApi.DTOs;
using BankingApi.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BankingApi.Services;

public class TransferService
{
    private const int MaxConcurrencyRetries = 10;

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

        // 4. Gerar hash determinístico do request.
        //
        // A chave de idempotência identifica a operação.
        // O hash identifica o conteúdo enviado junto da chave.
        var requestHash = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    $"{destinationAccountNumber}|{request.Amount.ToString(
                        CultureInfo.InvariantCulture)}")));

        // 5. Retry de concorrência.
        //
        // Cada tentativa utiliza uma nova transação e limpa o
        // ChangeTracker após conflito. Isso é importante porque,
        // depois de uma DbUpdateConcurrencyException, as entidades
        // rastreadas pelo DbContext podem conter valores antigos.
        for (var attempt = 1;
             attempt <= MaxConcurrencyRetries;
             attempt++)
        {
            try
            {
                var result =
                    await ExecuteTransferAttemptAsync(
                        sourceAccountId,
                        request,
                        destinationAccountNumber,
                        requestHash,
                        idempotencyKey);

                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.ChangeTracker.Clear();

                if (attempt == MaxConcurrencyRetries)
                {
                    throw new BusinessException(
                        "CONCURRENT_MODIFICATION",
                        "The transfer could not be completed because the account was modified concurrently. Please retry.",
                        409);
                }

                // Pequeno atraso progressivo para evitar que várias
                // requisições concorrentes entrem novamente juntas.
                var delayMilliseconds =
                    Math.Min(25 * attempt, 250);

                await Task.Delay(delayMilliseconds);
            }
            catch (DbUpdateException ex)
                when (IsUniqueConstraintViolation(ex))
            {
                /*
                 * Duas requisições podem chegar simultaneamente com
                 * a mesma Idempotency-Key.
                 *
                 * Ambas podem inicialmente não encontrar o registro.
                 * Uma delas grava primeiro e a outra recebe violação
                 * da constraint UNIQUE.
                 *
                 * Neste caso, devemos consultar o registro que acabou
                 * de ser criado e retornar exatamente a mesma resposta.
                 */
                _db.ChangeTracker.Clear();

                var existingRequest =
                    await _db.IdempotencyRequests
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.AccountId == sourceAccountId &&
                            x.Key == idempotencyKey);

                if (existingRequest is null)
                {
                    throw;
                }

                if (existingRequest.RequestHash != requestHash)
                {
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
                    throw new BusinessException(
                        "IDEMPOTENCY_RESPONSE_INVALID",
                        "The stored idempotency response is invalid.",
                        500);
                }

                return previousResponse;
            }
        }

        // Tecnicamente inalcançável.
        throw new BusinessException(
            "CONCURRENT_MODIFICATION",
            "The transfer could not be completed because the account was modified concurrently.",
            409);
    }

    private async Task<TransferResponse> ExecuteTransferAttemptAsync(
        Guid sourceAccountId,
        TransferRequest request,
        string destinationAccountNumber,
        string requestHash,
        string idempotencyKey)
    {
        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        // ============================================================
        // 1. IDEMPOTÊNCIA
        // ============================================================

        var existingRequest =
            await _db.IdempotencyRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.AccountId == sourceAccountId &&
                    x.Key == idempotencyKey);

        if (existingRequest is not null)
        {
            if (existingRequest.RequestHash != requestHash)
            {
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
                throw new BusinessException(
                    "IDEMPOTENCY_RESPONSE_INVALID",
                    "The stored idempotency response is invalid.",
                    500);
            }

            return previousResponse;
        }

        // ============================================================
        // 2. BUSCAR CONTA DE ORIGEM
        // ============================================================

        var sourceAccount =
            await _db.Accounts
                .FirstOrDefaultAsync(x =>
                    x.Id == sourceAccountId);

        if (sourceAccount is null)
        {
            throw new BusinessException(
                "SOURCE_ACCOUNT_NOT_FOUND",
                "The source account was not found.",
                404);
        }

        // ============================================================
        // 3. BUSCAR CONTA DE DESTINO
        // ============================================================

        var destinationAccount =
            await _db.Accounts
                .FirstOrDefaultAsync(x =>
                    x.AccountNumber == destinationAccountNumber);

        if (destinationAccount is null)
        {
            throw new BusinessException(
                "DESTINATION_ACCOUNT_NOT_FOUND",
                "The destination account was not found.",
                404);
        }

        // ============================================================
        // 4. VALIDAR TRANSFERÊNCIA PARA A PRÓPRIA CONTA
        // ============================================================

        if (sourceAccount.Id == destinationAccount.Id)
        {
            throw new BusinessException(
                "SELF_TRANSFER_NOT_ALLOWED",
                "Transfers to the same account are not allowed.",
                400);
        }

        // ============================================================
        // 5. VALIDAR SALDO
        // ============================================================

        if (sourceAccount.Balance < request.Amount)
        {
            throw new BusinessException(
                "INSUFFICIENT_FUNDS",
                "Insufficient funds.",
                422);
        }

        // ============================================================
        // 6. ATUALIZAR SALDOS
        // ============================================================

        sourceAccount.Balance -= request.Amount;
        destinationAccount.Balance += request.Amount;

        // O campo Version é utilizado pelo EF Core como token
        // de concorrência.
        //
        // Se outra transação modificar a mesma conta entre o SELECT
        // e o UPDATE, o UPDATE afetará zero linhas e o EF lançará
        // DbUpdateConcurrencyException.
        sourceAccount.Version++;
        destinationAccount.Version++;

        var transferCreatedAt =
            DateTime.UtcNow;

        // ============================================================
        // 7. CRIAR TRANSFERÊNCIA
        // ============================================================

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),

            SourceAccountId =
                sourceAccount.Id,

            DestinationAccountId =
                destinationAccount.Id,

            Amount =
                request.Amount,

            Status =
                TransferStatus.Completed,

            CreatedAt =
                transferCreatedAt
        };

        _db.Transfers.Add(transfer);

        // ============================================================
        // 8. LEDGER - DÉBITO
        // ============================================================

        var debitLedger = new LedgerEntry
        {
            Id = Guid.NewGuid(),

            AccountId =
                sourceAccount.Id,

            Amount =
                -request.Amount,

            BalanceAfter =
                sourceAccount.Balance,

            Type =
                "TRANSFER_DEBIT",

            TransferId =
                transfer.Id,

            CreatedAtUtc =
                transferCreatedAt
        };

        // ============================================================
        // 9. LEDGER - CRÉDITO
        // ============================================================

        var creditLedger = new LedgerEntry
        {
            Id = Guid.NewGuid(),

            AccountId =
                destinationAccount.Id,

            Amount =
                request.Amount,

            BalanceAfter =
                destinationAccount.Balance,

            Type =
                "TRANSFER_CREDIT",

            TransferId =
                transfer.Id,

            CreatedAtUtc =
                transferCreatedAt
        };

        _db.LedgerEntries.Add(debitLedger);
        _db.LedgerEntries.Add(creditLedger);

        // ============================================================
        // 10. RESPOSTA
        // ============================================================

        var response = new TransferResponse
        {
            TransferId =
                transfer.Id,

            SourceAccountId =
                sourceAccount.Id,

            SourceAccountNumber =
                sourceAccount.AccountNumber,

            DestinationAccountId =
                destinationAccount.Id,

            DestinationAccountNumber =
                destinationAccount.AccountNumber,

            Amount =
                request.Amount,

            SourceBalance =
                sourceAccount.Balance,

            Status =
                transfer.Status.ToString(),

            CreatedAt =
                transfer.CreatedAt
        };

        // ============================================================
        // 11. REGISTRO DE IDEMPOTÊNCIA
        // ============================================================

        var idempotencyRequest = new IdempotencyRequest
        {
            Id =
                Guid.NewGuid(),

            Key =
                idempotencyKey,

            AccountId =
                sourceAccount.Id,

            RequestHash =
                requestHash,

            ResponseStatusCode =
                200,

            ResponseBody =
                JsonSerializer.Serialize(response),

            CreatedAt =
                DateTime.UtcNow
        };

        _db.IdempotencyRequests.Add(
            idempotencyRequest);

        // ============================================================
        // 12. PERSISTÊNCIA ATÔMICA
        // ============================================================

        await _db.SaveChangesAsync();

        await transaction.CommitAsync();

        return response;
    }

    private static bool IsUniqueConstraintViolation(
        DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
    }
}