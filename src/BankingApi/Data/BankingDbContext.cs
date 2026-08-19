using BankingApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace BankingApi.Data;

public class BankingDbContext : DbContext
{
    public BankingDbContext(
        DbContextOptions<BankingDbContext> options)
        : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<Transfer> Transfers => Set<Transfer>();

    public DbSet<IdempotencyRequest> IdempotencyRequests => Set<IdempotencyRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.AccountNumber)
                .HasMaxLength(20)
                .IsRequired();

            entity.HasIndex(x => x.AccountNumber)
                .IsUnique();

            entity.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Email)
                .HasMaxLength(320)
                .IsRequired();

            entity.HasIndex(x => x.Email)
                .IsUnique();

            entity.Property(x => x.PasswordHash)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(x => x.Balance)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.Version)
                .IsConcurrencyToken()
                .IsRequired();
        });

        modelBuilder.Entity<Transfer>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Amount)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(x => x.Status)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.ReversedAt)
                .IsRequired(false);

            entity.HasIndex(x => x.SourceAccountId);

            entity.HasIndex(x => x.DestinationAccountId);

            entity.HasIndex(x => x.CreatedAt);

            entity.HasIndex(x => new
            {
                x.SourceAccountId,
                x.CreatedAt
            });

            entity.HasIndex(x => new
            {
                x.DestinationAccountId,
                x.CreatedAt
            });
        });

        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Amount)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(x => x.BalanceAfter)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(x => x.Type)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.CreatedAtUtc)
                .IsRequired();

            entity.HasIndex(x => new
            {
                x.AccountId,
                x.CreatedAtUtc,
                x.Id
            });

            entity.HasIndex(x => x.TransferId);

            entity.HasOne(x => x.Account)
                .WithMany(x => x.LedgerEntries)
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IdempotencyRequest>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Key)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.AccountId)
                .IsRequired();

            entity.Property(x => x.RequestHash)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(x => x.ResponseBody)
                .IsRequired();

            entity.Property(x => x.ResponseStatusCode)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasIndex(x => new
            {
                x.AccountId,
                x.Key
            })
            .IsUnique();
        });

        modelBuilder.Entity<Transfer>()
            .HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.SourceAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Transfer>()
            .HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.DestinationAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}