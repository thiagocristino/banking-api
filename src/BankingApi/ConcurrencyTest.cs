using BankingApi.Data;
using Microsoft.EntityFrameworkCore;

namespace BankingApi;

public class ConcurrencyTest
{
    public static async Task RunAsync(
        DbContextOptions<BankingDbContext> options)
    {
        await using var db1 =
            new BankingDbContext(options);

        await using var db2 =
            new BankingDbContext(options);

        var accountId = Guid.Parse(
            "38cea372-c76c-463e-9ad1-024d07e49370");

        var account1 = await db1.Accounts
            .FirstAsync(x => x.Id == accountId);

        var account2 = await db2.Accounts
            .FirstAsync(x => x.Id == accountId);

        Console.WriteLine(
            $"DB1 -> Balance: {account1.Balance}, Version: {account1.Version}");

        Console.WriteLine(
            $"DB2 -> Balance: {account2.Balance}, Version: {account2.Version}");

        account1.Balance += 1;
        account1.Version++;

        await db1.SaveChangesAsync();

        Console.WriteLine(
            $"DB1 -> SaveChanges OK. New Version: {account1.Version}");

        account2.Balance += 1;
        account2.Version++;

        try
        {
            await db2.SaveChangesAsync();

            Console.WriteLine(
                "ERRO: DB2 também conseguiu salvar.");
        }
        catch (DbUpdateConcurrencyException)
        {
            Console.WriteLine(
                "SUCESSO: DbUpdateConcurrencyException detectada.");
        }
    }
}