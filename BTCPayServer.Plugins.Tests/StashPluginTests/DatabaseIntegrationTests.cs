#nullable enable
using System.Data.Common;
using BTCPayServer.Plugins.Stash.Data;
using BTCPayServer.Plugins.Stash.Data.Models;
using BTCPayServer.Plugins.Stash.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BTCPayServer.Plugins.Tests.StashPluginTests;

/// <summary>
/// Integration tests that verify database operations work correctly.
/// Uses SQLite in-memory database for fast, isolated testing.
/// </summary>
public class DatabaseIntegrationTests : IDisposable
{
    private readonly DbConnection _connection;
    private readonly DbContextOptions<PluginDbContext> _contextOptions;
    private readonly TestablePluginDbContextFactory _dbContextFactory;

    public DatabaseIntegrationTests()
    {
        // Create and open a SQLite connection for in-memory testing
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _contextOptions = new DbContextOptionsBuilder<PluginDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Create the schema
        using var context = new PluginDbContext(_contextOptions);
        context.Database.EnsureCreated();

        _dbContextFactory = new TestablePluginDbContextFactory(_contextOptions);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    #region Allocation Service Tests

    [Fact]
    public async Task CreateAllocationAsync_CreatesAllocation()
    {
        // Arrange
        var logger = Mock.Of<ILogger<AllocationService>>();
        var service = new AllocationService(_dbContextFactory, logger);

        // Act
        var allocation = await service.CreateAllocationAsync(
            storeId: "store-1",
            invoiceId: "invoice-1",
            paymentMethod: "BTC-LN",
            totalReceivedSats: 100000,
            allocationPercentage: 20m,
            exchangeRate: 50000m,
            fiatCurrency: "USD");

        // Assert
        Assert.NotNull(allocation);
        Assert.Equal("store-1", allocation.StoreId);
        Assert.Equal("invoice-1", allocation.InvoiceId);
        Assert.Equal(20000, allocation.AllocatedSats); // 20% of 100000
        Assert.Equal(10m, allocation.FiatValueAtReceipt, 2); // 20000 sats at $50k/BTC = $10
    }

    [Fact]
    public async Task CreateAllocationAsync_DuplicateInvoice_ReturnsExisting()
    {
        // Arrange
        var logger = Mock.Of<ILogger<AllocationService>>();
        var service = new AllocationService(_dbContextFactory, logger);

        // Create first allocation
        var first = await service.CreateAllocationAsync(
            "store-1", "invoice-dup", "BTC-LN", 100000, 20m, 50000m, "USD");

        // Act - try to create duplicate
        var second = await service.CreateAllocationAsync(
            "store-1", "invoice-dup", "BTC-LN", 200000, 30m, 60000m, "USD");

        // Assert - should return the first allocation, not create a new one
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.AllocatedSats, second.AllocatedSats);
    }

    [Fact]
    public async Task GetPendingTotalsAsync_ReturnsSumOfPendingAllocations()
    {
        // Arrange
        var logger = Mock.Of<ILogger<AllocationService>>();
        var service = new AllocationService(_dbContextFactory, logger);

        await service.CreateAllocationAsync(
            "store-totals", "invoice-t1", "BTC-LN", 100000, 20m, 50000m, "USD");
        await service.CreateAllocationAsync(
            "store-totals", "invoice-t2", "BTC-LN", 200000, 20m, 50000m, "USD");

        // Act
        var (totalSats, totalFiat, count) = await service.GetPendingTotalsAsync("store-totals");

        // Assert
        Assert.Equal(60000, totalSats); // 20000 + 40000
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CreateAllocationAsync_InvalidExchangeRate_SanitizesValue()
    {
        // Arrange
        var logger = Mock.Of<ILogger<AllocationService>>();
        var service = new AllocationService(_dbContextFactory, logger);

        // Act - pass a negative exchange rate
        var allocation = await service.CreateAllocationAsync(
            storeId: "store-invalid-rate",
            invoiceId: "invoice-invalid-rate",
            paymentMethod: "BTC-LN",
            totalReceivedSats: 100000,
            allocationPercentage: 20m,
            exchangeRate: -50000m, // Invalid negative rate
            fiatCurrency: "USD");

        // Assert - should sanitize to 0
        Assert.Equal(0m, allocation.ExchangeRateAtReceipt);
        Assert.Equal(0m, allocation.FiatValueAtReceipt);
    }

    #endregion

    #region Batch Creation Tests

    [Fact]
    public async Task ExecutedBatch_CanBeCreatedAndRetrieved()
    {
        // Arrange
        await using var db = _dbContextFactory.CreateContext();
        
        var batch = new ExecutedBatch
        {
            StoreId = "store-batch-1",
            ExecutionType = BatchExecutionType.ColdStorage,
            Status = BatchStatus.Pending,
            TotalSats = 100000,
            FeeSats = 500,
            NetSats = 99500,
            FiatValueAtExecution = 50m,
            FiatCurrency = "USD",
            ExchangeRateAtExecution = 50000m,
            WeightedAverageCostBasis = 48000m,
            AllocationCount = 5,
            InitiatedAt = DateTimeOffset.UtcNow
        };

        // Act
        db.ExecutedBatches.Add(batch);
        await db.SaveChangesAsync();

        // Retrieve
        var retrieved = await db.ExecutedBatches.FindAsync(batch.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("store-batch-1", retrieved.StoreId);
        Assert.Equal(BatchStatus.Pending, retrieved.Status);
        Assert.Equal(100000, retrieved.TotalSats);
    }

    [Fact]
    public async Task Allocation_BatchRelationship_WorksCorrectly()
    {
        // Arrange
        await using var db = _dbContextFactory.CreateContext();
        
        var batch = new ExecutedBatch
        {
            StoreId = "store-rel",
            ExecutionType = BatchExecutionType.LiquidSwap,
            Status = BatchStatus.Completed,
            TotalSats = 50000,
            AllocationCount = 2
        };
        db.ExecutedBatches.Add(batch);
        await db.SaveChangesAsync();

        var allocation = new PendingAllocation
        {
            StoreId = "store-rel",
            InvoiceId = "invoice-rel-1",
            TotalReceivedSats = 100000,
            AllocatedSats = 25000,
            ExecutedBatchId = batch.Id,
            IsExecuted = true
        };
        db.PendingAllocations.Add(allocation);
        await db.SaveChangesAsync();

        // Act
        var batchWithAllocations = await db.ExecutedBatches
            .Include(b => b.Allocations)
            .FirstOrDefaultAsync(b => b.Id == batch.Id);

        // Assert
        Assert.NotNull(batchWithAllocations);
        Assert.Single(batchWithAllocations.Allocations);
        Assert.Equal(allocation.Id, batchWithAllocations.Allocations.First().Id);
    }

    #endregion

    #region BoltzSwap Tests

    [Fact]
    public async Task BoltzSwap_CanBeCreatedWithAllFields()
    {
        // Arrange
        await using var db = _dbContextFactory.CreateContext();

        var swap = new BoltzSwap
        {
            StoreId = "store-swap-1",
            BoltzSwapId = "boltz-abc123",
            State = BoltzSwapState.Created,
            BoltzStatus = BoltzSwapStatus.Created,
            Invoice = "lnbc1...",
            InvoiceAmountSats = 100000,
            OnchainAmountSats = 99000,
            MinerFeeSats = 500,
            ServiceFeeSats = 500,
            DestinationAddress = "tex1q...",
            ClaimPublicKey = "02abc...",
            ClaimPrivateKey = "abc123...", // New field we added
            TimeoutBlockHeight = 12345
        };

        // Act
        db.BoltzSwaps.Add(swap);
        await db.SaveChangesAsync();

        var retrieved = await db.BoltzSwaps.FindAsync(swap.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("boltz-abc123", retrieved.BoltzSwapId);
        Assert.Equal("abc123...", retrieved.ClaimPrivateKey);
        Assert.Equal(BoltzSwapState.Created, retrieved.State);
    }

    [Fact]
    public async Task BoltzSwap_UniqueConstraintOnBoltzSwapId()
    {
        // Arrange
        await using var db = _dbContextFactory.CreateContext();

        var swap1 = new BoltzSwap
        {
            StoreId = "store-unique",
            BoltzSwapId = "unique-swap-id",
            State = BoltzSwapState.Created
        };
        db.BoltzSwaps.Add(swap1);
        await db.SaveChangesAsync();

        var swap2 = new BoltzSwap
        {
            StoreId = "store-unique",
            BoltzSwapId = "unique-swap-id", // Same ID
            State = BoltzSwapState.Created
        };
        db.BoltzSwaps.Add(swap2);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(
            async () => await db.SaveChangesAsync());
    }

    #endregion

    #region Settings Tests

    [Fact]
    public async Task StashSettings_UniqueConstraintOnStoreId()
    {
        // Arrange
        await using var db = _dbContextFactory.CreateContext();

        var settings1 = new StashSettings
        {
            StoreId = "unique-store",
            AllocationPercentage = 20m,
            BatchThresholdFiat = 100m
        };
        db.StashSettings.Add(settings1);
        await db.SaveChangesAsync();

        var settings2 = new StashSettings
        {
            StoreId = "unique-store", // Same store
            AllocationPercentage = 30m,
            BatchThresholdFiat = 200m
        };
        db.StashSettings.Add(settings2);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(
            async () => await db.SaveChangesAsync());
    }

    #endregion
}

/// <summary>
/// Testable factory that creates contexts with the test options.
/// This is a minimal implementation that bypasses the need for DatabaseOptions.
/// </summary>
public class TestablePluginDbContextFactory : PluginDbContextFactory
{
    private readonly DbContextOptions<PluginDbContext> _options;

    public TestablePluginDbContextFactory(DbContextOptions<PluginDbContext> options) 
        : base(Microsoft.Extensions.Options.Options.Create(new BTCPayServer.Abstractions.Models.DatabaseOptions()))
    {
        _options = options;
    }

    public override PluginDbContext CreateContext(
        Action<Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
    {
        return new PluginDbContext(_options);
    }
}
