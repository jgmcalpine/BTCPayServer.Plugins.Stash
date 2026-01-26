using BTCPayServer.Plugins.Stash.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Stash.Data;

public class PluginDbContext : DbContext
{
    public const string DefaultPluginSchema = "BTCPayServer.Plugins.Stash";

    public PluginDbContext(DbContextOptions<PluginDbContext> options, bool designTime = false) : base(options)
    {
    }

    public DbSet<StashSettings> StashSettings { get; set; }
    public DbSet<PendingAllocation> PendingAllocations { get; set; }
    public DbSet<ExecutedBatch> ExecutedBatches { get; set; }
    public DbSet<BoltzSwap> BoltzSwaps { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(DefaultPluginSchema);

        // Configure StashSettings
        modelBuilder.Entity<StashSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId).IsUnique();
            entity.Property(e => e.AllocationPercentage).HasPrecision(5, 2);
            entity.Property(e => e.BatchThresholdFiat).HasPrecision(18, 2);
        });

        // Configure PendingAllocation
        modelBuilder.Entity<PendingAllocation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => e.InvoiceId);
            entity.HasIndex(e => e.IsExecuted);
            entity.HasIndex(e => new { e.StoreId, e.IsExecuted });
            entity.Property(e => e.FiatValueAtReceipt).HasPrecision(18, 2);
            entity.Property(e => e.ExchangeRateAtReceipt).HasPrecision(18, 8);

            // Configure relationship
            entity.HasOne(e => e.ExecutedBatch)
                .WithMany(e => e.Allocations)
                .HasForeignKey(e => e.ExecutedBatchId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure ExecutedBatch
        modelBuilder.Entity<ExecutedBatch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.InitiatedAt);
            entity.Property(e => e.FiatValueAtExecution).HasPrecision(18, 2);
            entity.Property(e => e.ExchangeRateAtExecution).HasPrecision(18, 8);
            entity.Property(e => e.WeightedAverageCostBasis).HasPrecision(18, 8);
            entity.Property(e => e.UsdtReceived).HasPrecision(18, 2);
        });

        // Configure BoltzSwap
        modelBuilder.Entity<BoltzSwap>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => e.BatchId);
            entity.HasIndex(e => e.BoltzSwapId).IsUnique();
            entity.HasIndex(e => e.State);
            entity.HasIndex(e => new { e.StoreId, e.State });

            // Configure relationship with batch
            entity.HasOne(e => e.Batch)
                .WithMany()
                .HasForeignKey(e => e.BatchId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}

