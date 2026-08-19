using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new BusinessException(
                "IDEMPOTENCY_KEY_REQUIRED",
                "The Idempotency-Key header is required.",
                400);
        }

        if (request.Amount <= 0)
        {
            throw new BusinessException(
                "INVALID_AMOUNT",
                "The deposit amount must be greater than zero.",
                400);
        }

        var requestHash = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    request.Amount.ToString(
                        CultureInfo.InvariantCulture))));

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var existingRequest =
            await _db.IdempotencyRequests
                .FirstOrDefaultAsync(x =>
                    x.AccountId == accountId &&
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
                JsonSerializer.Deserialize<DepositResponse>(
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

        var account = await _db.Accounts
            .FirstOrDefaultAsync(x =>
                x.Id == accountId);

        if (account is null)
        {
            await transaction.RollbackAsync();

            throw new BusinessException(
                "ACCOUNT_NOT_FOUND",
                "Account was not found.",
                404);
        }

        account.Balance += request.Amount;

        account.Version++;

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

        var response = new DepositResponse
        {
            AccountId = account.Id,

            Amount = request.Amount,

            Balance = account.Balance,

            LedgerEntryId = ledgerEntry.Id,

            CreatedAtUtc = ledgerEntry.CreatedAtUtc
        };

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
                // Transaction may already have been rolled back.
            }

            throw new BusinessException(
                "CONCURRENT_MODIFICATION",
                "The account was modified by another transaction. Please retry.",
                409);
        }
        catch (DbUpdateException)
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch
            {
                // Transaction may already have been rolled back.
            }

            throw;
        }

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

    public async Task<StatementResponse> GetStatementAsync(
        Guid accountId,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize < 1)
        {
            pageSize = 50;
        }

        if (pageSize > 100)
        {
            pageSize = 100;
        }

        var accountExists = await _db.Accounts
            .AsNoTracking()
            .AnyAsync(x => x.Id == accountId);

        if (!accountExists)
        {
            throw new BusinessException(
                "ACCOUNT_NOT_FOUND",
                "Account was not found.",
                404);
        }

        var start = startDate?.ToUniversalTime()
            ?? DateTime.UnixEpoch;

        var end = endDate?.ToUniversalTime()
            ?? DateTime.UtcNow;

        if (end <= start)
        {
            throw new BusinessException(
                "INVALID_STATEMENT_PERIOD",
                "The statement end date must be greater than the start date.",
                400);
        }

        var entriesQuery = _db.LedgerEntries
            .AsNoTracking()
            .Where(x =>
                x.AccountId == accountId &&
                x.CreatedAtUtc >= start &&
                x.CreatedAtUtc < end);

        var totalEntries =
            await entriesQuery.CountAsync();

        var totalPages = (int)Math.Ceiling(
            totalEntries / (double)pageSize);

        var openingEntry = await _db.LedgerEntries
            .AsNoTracking()
            .Where(x =>
                x.AccountId == accountId &&
                x.CreatedAtUtc < start)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        var openingBalance =
            openingEntry?.BalanceAfter ?? 0m;

        var entries = await entriesQuery
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new StatementEntryResponse
            {
                Id = x.Id,

                Amount = x.Amount,

                BalanceAfter = x.BalanceAfter,

                Type = x.Type,

                TransferId = x.TransferId,

                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync();

        var closingEntry = await _db.LedgerEntries
            .AsNoTracking()
            .Where(x =>
                x.AccountId == accountId &&
                x.CreatedAtUtc < end)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        var closingBalance =
            closingEntry?.BalanceAfter ?? 0m;

        return new StatementResponse
        {
            StartDate = start,

            EndDate = end,

            OpeningBalance = openingBalance,

            ClosingBalance = closingBalance,

            Page = page,

            PageSize = pageSize,

            TotalEntries = totalEntries,

            TotalPages = totalPages,

            Entries = entries
        };
    }
}
