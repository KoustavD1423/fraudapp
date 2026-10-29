using Microsoft.EntityFrameworkCore;
using Fraud.Domain;
using MassTransit;

namespace Fraud.Application.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Alert> Alerts => Set<Alert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
        modelBuilder.Entity<Transaction>(entity =>
    {
        // Matches the existing Transactions.Amount column.
        entity.Property(t => t.Amount)
            .HasPrecision(18, 2);

        entity.Property(t => t.ProcessingStatus)
            .HasConversion<int>()
            .HasDefaultValue(TransactionProcessingStatus.Pending);

        entity.Property(t => t.FraudReasons)
            .HasMaxLength(4000)
            .HasDefaultValue(string.Empty)
            .IsRequired();

        entity.Property(t => t.RowVersion)
            .IsRowVersion();
    });

        modelBuilder.Entity<Alert>()
            .HasIndex(a => a.TransactionId)
            .IsUnique();

    }
}