using System.Text.Json;
using BankingApi.Data;
using BankingApi.Domain;
using BankingApi.DTOs;
using BankingApi.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BankingApi.Services;

public class AccountService
{
    private readonly BankingDbContext _db;

    public AccountService(BankingDbContext db)
    {
        _db = db;
    }

    public async Task<AccountResponse> CreateAsync(
        CreateAccountRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var existingAccount = await _db.Accounts
            .FirstOrDefaultAsync(x => x.Email == email);

        if (existingAccount != null)
        {
            throw new BusinessException(
                "EMAIL_ALREADY_EXISTS",
                "An account with this email already exists.",
                409);
        }

        var account = new Account
        {
            Id = Guid.NewGuid(),

            AccountNumber = await GenerateAccountNumberAsync(),

            Name = request.Name.Trim(),

            Email = email,

            PasswordHash = BCrypt.Net.BCrypt.HashPassword(
                request.Password),

            Balance = 0m,

            CreatedAt = DateTime.UtcNow
        };

        _db.Accounts.Add(account);

        await _db.SaveChangesAsync();

        return new AccountResponse
        {
            Id = account.Id,
            AccountNumber = account.AccountNumber,
            Name = account.Name,
            Email = account.Email
        };
    }

    private async Task<string> GenerateAccountNumberAsync()
    {
        while (true)
        {
            var number = Random.Shared
                .Next(10_000_000, 100_000_000)
                .ToString();

            var exists = await _db.Accounts
                .AnyAsync(x => x.AccountNumber == number);

            if (!exists)
            {
                return number;
            }
        }
    }

    public async Task<DepositResponse> DepositAsync(
        Guid accountId,
        DepositRequest request,
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
                "The deposit amount must be greater than zero.",
                400);
        }

        // 3. Gerar hash da requisição
        var requestHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    request.Amount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture))));

        // 4. Iniciar transação
        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        // 5. Verificar idempotência
        var existingRequest =
            await _db.IdempotencyRequests
                .FirstOrDefaultAsync(x =>
                    x.AccountId == accountId &&
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
                JsonSerializer.Deserialize<DepositResponse>(
                    existingRequest.ResponseBody);

            if (previousResponse is null)
            {
                throw new BusinessException(
                    "IDEMPOTENCY_RESPONSE_INVALID",
                    "The stored idempotency response is invalid.",
                    500);
            }

            await transaction.RollbackAsync();

            return previousResponse;
        }

        // 6. Buscar conta
        var account = await _db.Accounts
            .FirstOrDefaultAsync(x =>
                x.Id == accountId);

        if (account is null)
        {
            throw new BusinessException(
                "ACCOUNT_NOT_FOUND",
                "Account was not found.",
                404);
        }

        // 7. Atualizar saldo
        account.Balance += request.Amount;

        // 8. Atualizar versão para controle de concorrência
        account.Version++;

        // 9. Criar lançamento no Ledger
        var ledgerEntry = new LedgerEntry
        {
            Id = Guid.NewGuid(),

            AccountId = account.Id,

            Amount = request.Amount,

            BalanceAfter = account.Balance,

            Type = "DEPOSIT",

            TransferId = null,

            CreatedAtUtc = DateTime.UtcNow
        };

        _db.LedgerEntries.Add(ledgerEntry);

        // 10. Criar resposta
        var response = new DepositResponse
        {
            AccountId = account.Id,

            Amount = request.Amount,

            Balance = account.Balance,

            LedgerEntryId = ledgerEntry.Id,

            CreatedAtUtc = ledgerEntry.CreatedAtUtc
        };

        // 11. Registrar idempotência
        var idempotencyRequest = new IdempotencyRequest
        {
            Id = Guid.NewGuid(),

            Key = idempotencyKey,

            AccountId = account.Id,

            RequestHash = requestHash,

            ResponseStatusCode = 200,

            ResponseBody =
                JsonSerializer.Serialize(response),

            CreatedAt = DateTime.UtcNow
        };

        _db.IdempotencyRequests.Add(
            idempotencyRequest);

        // 12. Persistir tudo com controle de concorrência
        try
        {
            await _db.SaveChangesAsync();

            // 13. Confirmar transação
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();

            throw new BusinessException(
                "CONCURRENT_MODIFICATION",
                "The account was modified by another transaction. Please retry.",
                409);
        }

        // 14. Retornar resposta
        return response;
    }

    public async Task<AccountResponse?> GetByIdAsync(
        Guid accountId)
    {
        var account = await _db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == accountId);

        if (account is null)
        {
            return null;
        }

        return new AccountResponse
        {
            Id = account.Id,
            AccountNumber = account.AccountNumber,
            Name = account.Name,
            Email = account.Email
        };
    }
}
