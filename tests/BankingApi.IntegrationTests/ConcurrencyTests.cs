using BankingApi.Data;
using BankingApi.Domain;
using BankingApi.DTOs;
using BankingApi.Services;
using BankingApi.Exceptions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BankingApi.IntegrationTests;

public class ConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("banking")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private DbContextOptions<BankingDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _options =
            new DbContextOptionsBuilder<BankingDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options;

        await using var db =
            new BankingDbContext(_options);

        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentTransfers_ShouldNeverCreateNegativeBalance()
    {
        var sourceAccountId = Guid.NewGuid();
        var destinationAccountId = Guid.NewGuid();

        await CreateAccountsAsync(
            sourceAccountId,
            destinationAccountId,
            100m);

        var startGate =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable
            .Range(0, 50)
            .Select(async index =>
            {
                await startGate.Task;

                await using var db =
                    new BankingDbContext(_options);

                var service =
                    new TransferService(db);

                var request = new TransferRequest
                {
                    DestinationAccountNumber = "10000002",
                    Amount = 10m
                };

                try
                {
                    return await service.CreateAsync(
                        sourceAccountId,
                        request,
                        $"concurrency-test-{index}");
                }
                catch
                {
                    return null;
                }
            })
            .ToArray();

        startGate.SetResult();

        var results =
            await Task.WhenAll(tasks);

        var successfulTransfers =
            results
                .Where(x => x is not null)
                .ToList();

        await using var verificationDb =
            new BankingDbContext(_options);

        var sourceAccount =
            await verificationDb.Accounts
                .SingleAsync(x =>
                    x.Id == sourceAccountId);

        var destinationAccount =
            await verificationDb.Accounts
                .SingleAsync(x =>
                    x.Id == destinationAccountId);

        var sourceLedger =
            await verificationDb.LedgerEntries
                .Where(x =>
                    x.AccountId == sourceAccountId)
                .ToListAsync();

        var destinationLedger =
            await verificationDb.LedgerEntries
                .Where(x =>
                    x.AccountId == destinationAccountId)
                .ToListAsync();

        var transfers =
            await verificationDb.Transfers
                .Where(x =>
                    x.SourceAccountId == sourceAccountId)
                .ToListAsync();

        Assert.Equal(10, successfulTransfers.Count);
        Assert.Equal(0m, sourceAccount.Balance);
        Assert.Equal(100m, destinationAccount.Balance);

        Assert.True(sourceAccount.Balance >= 0m);

        Assert.Equal(10, transfers.Count);
        Assert.Equal(10, sourceLedger.Count);
        Assert.Equal(10, destinationLedger.Count);

        Assert.Equal(
            -100m,
            sourceLedger.Sum(x => x.Amount));

        Assert.Equal(
            100m,
            destinationLedger.Sum(x => x.Amount));

        Assert.Equal(
            sourceAccount.Balance,
            sourceLedger
                .OrderByDescending(x => x.CreatedAtUtc)
                .ThenByDescending(x => x.Id)
                .First()
                .BalanceAfter);

        Assert.Equal(
            destinationAccount.Balance,
            destinationLedger
                .OrderByDescending(x => x.CreatedAtUtc)
                .ThenByDescending(x => x.Id)
                .First()
                .BalanceAfter);
    }

    [Fact]
    public async Task SameIdempotencyKey_ConcurrentRequests_ShouldCreateOnlyOneTransfer()
    {
        var sourceAccountId = Guid.NewGuid();
        var destinationAccountId = Guid.NewGuid();

        await CreateAccountsAsync(
            sourceAccountId,
            destinationAccountId,
            100m);

        var request = new TransferRequest
        {
            DestinationAccountNumber = "10000002",
            Amount = 10m
        };

        const string idempotencyKey =
            "same-key-concurrent-test";

        var startGate =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable
            .Range(0, 50)
            .Select(async _ =>
            {
                await startGate.Task;

                await using var db =
                    new BankingDbContext(_options);

                var service =
                    new TransferService(db);

                try
                {
                    return await service.CreateAsync(
                        sourceAccountId,
                        request,
                        idempotencyKey);
                }
                catch
                {
                    return null;
                }
            })
            .ToArray();

        startGate.SetResult();

        var results =
            await Task.WhenAll(tasks);

        var successfulResults =
            results
                .Where(x => x is not null)
                .ToList();

        Assert.NotEmpty(successfulResults);

        var firstResult =
            successfulResults.First();

        Assert.All(
            successfulResults,
            result =>
            {
                Assert.Equal(
                    firstResult!.TransferId,
                    result!.TransferId);

                Assert.Equal(
                    firstResult.Amount,
                    result.Amount);
            });

        await using var verificationDb =
            new BankingDbContext(_options);

        var transfers =
            await verificationDb.Transfers
                .Where(x =>
                    x.SourceAccountId == sourceAccountId)
                .ToListAsync();

        var idempotencyRequests =
            await verificationDb.IdempotencyRequests
                .Where(x =>
                    x.AccountId == sourceAccountId &&
                    x.Key == idempotencyKey)
                .ToListAsync();

        var sourceAccount =
            await verificationDb.Accounts
                .SingleAsync(x =>
                    x.Id == sourceAccountId);

        var destinationAccount =
            await verificationDb.Accounts
                .SingleAsync(x =>
                    x.Id == destinationAccountId);

        Assert.Single(transfers);
        Assert.Single(idempotencyRequests);

        Assert.Equal(
            90m,
            sourceAccount.Balance);

        Assert.Equal(
            10m,
            destinationAccount.Balance);
    }

    [Fact]
    public async Task SameIdempotencyKey_WithDifferentBody_ShouldReturnConflict()
    {
        var sourceAccountId = Guid.NewGuid();
        var destinationAccountId = Guid.NewGuid();

        await CreateAccountsAsync(
            sourceAccountId,
            destinationAccountId,
            100m);

        const string idempotencyKey =
            "same-key-different-body";

        var firstRequest = new TransferRequest
        {
            DestinationAccountNumber = "10000002",
            Amount = 10m
        };

        var secondRequest = new TransferRequest
        {
            DestinationAccountNumber = "10000002",
            Amount = 20m
        };

        await using (var db = new BankingDbContext(_options))
        {
            var service = new TransferService(db);

            await service.CreateAsync(
                sourceAccountId,
                firstRequest,
                idempotencyKey);
        }

        await using var secondDb =
            new BankingDbContext(_options);

        var secondService =
            new TransferService(secondDb);

        var exception =
            await Assert.ThrowsAsync<BusinessException>(
                () =>
                    secondService.CreateAsync(
                        sourceAccountId,
                        secondRequest,
                        idempotencyKey));

        Assert.Equal(
  		"IDEMPOTENCY_KEY_REUSED",
  		exception.Code);
    }

    [Fact]
    public async Task DifferentIdempotencyKeys_WithSameBody_ShouldCreateTwoTransfers()
    {
        var sourceAccountId = Guid.NewGuid();
        var destinationAccountId = Guid.NewGuid();

        await CreateAccountsAsync(
            sourceAccountId,
            destinationAccountId,
            100m);

        var request = new TransferRequest
        {
            DestinationAccountNumber = "10000002",
            Amount = 10m
        };

        await using (var db = new BankingDbContext(_options))
        {
            var service = new TransferService(db);

            await service.CreateAsync(
                sourceAccountId,
                request,
                "different-key-1");
        }

        await using (var db = new BankingDbContext(_options))
        {
            var service = new TransferService(db);

            await service.CreateAsync(
                sourceAccountId,
                request,
                "different-key-2");
        }

        await using var verificationDb =
            new BankingDbContext(_options);

        var transfers =
            await verificationDb.Transfers
                .Where(x =>
                    x.SourceAccountId == sourceAccountId)
                .ToListAsync();

        var sourceAccount =
            await verificationDb.Accounts
                .SingleAsync(x =>
                    x.Id == sourceAccountId);

        var destinationAccount =
            await verificationDb.Accounts
                .SingleAsync(x =>
                    x.Id == destinationAccountId);

        Assert.Equal(2, transfers.Count);

        Assert.Equal(
            80m,
            sourceAccount.Balance);

        Assert.Equal(
            20m,
            destinationAccount.Balance);
    }

    private async Task CreateAccountsAsync(
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal sourceBalance)
    {
        await using var db =
            new BankingDbContext(_options);

        db.Accounts.AddRange(
            new Account
            {
                Id = sourceAccountId,
                AccountNumber = "10000001",
                Name = "Source Account",
                Email = $"{sourceAccountId}@test.local",
                PasswordHash = "test",
                Balance = sourceBalance,
                CreatedAt = DateTime.UtcNow,
                Version = 0
            },
            new Account
            {
                Id = destinationAccountId,
                AccountNumber = "10000002",
                Name = "Destination Account",
                Email = $"{destinationAccountId}@test.local",
                PasswordHash = "test",
                Balance = 0m,
                CreatedAt = DateTime.UtcNow,
                Version = 0
            });

        await db.SaveChangesAsync();
    }
}