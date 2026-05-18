using Microsoft.EntityFrameworkCore;
using Fraud.Domain;

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

        // Enforce idempotency: Only one Alert can be created per TransactionId.
        // If MassTransit processes the same message twice, the second DB insert will fail 
        // with a unique constraint violation, naturally preventing duplicate alerts.
        modelBuilder.Entity<Alert>()
            .HasIndex(a => a.TransactionId)
            .IsUnique();
    }
}