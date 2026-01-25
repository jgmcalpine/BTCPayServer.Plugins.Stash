#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Stash.Data;
using BTCPayServer.Plugins.Stash.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Stash.Services;

public class BatchExecutionService(
    PluginDbContextFactory dbContextFactory,
    ILogger<BatchExecutionService> logger)
{
    // Bitcoin address regex patterns
    private static readonly Regex BtcMainnetAddressRegex = new(
        @"^(bc1[a-zA-HJ-NP-Z0-9]{25,87}|[13][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        RegexOptions.Compiled);

    private static readonly Regex BtcTestnetAddressRegex = new(
        @"^(tb1[a-zA-HJ-NP-Z0-9]{25,87}|[2mn][a-km-zA-HJ-NP-Z1-9]{25,34}|bcrt1[a-zA-HJ-NP-Z0-9]{25,87})$",
        RegexOptions.Compiled);

    private static readonly Regex XpubRegex = new(
        @"^([xyztuvXYZTUV]pub[a-zA-HJ-NP-Z0-9]{100,120})$",
        RegexOptions.Compiled);

    // Liquid address regex (simplified)
    private static readonly Regex LiquidAddressRegex = new(
        @"^(ex1[a-zA-HJ-NP-Z0-9]{25,87}|lq1[a-zA-HJ-NP-Z0-9]{25,87}|[GHVW][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        RegexOptions.Compiled);

    /// <summary>
    /// Validates a Bitcoin address.
    /// </summary>
    public bool ValidateBitcoinAddress(string address, bool isTestnet = false)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;

        return isTestnet 
            ? BtcTestnetAddressRegex.IsMatch(address) 
            : BtcMainnetAddressRegex.IsMatch(address);
    }

    /// <summary>
    /// Validates an XPUB.
    /// </summary>
    public bool ValidateXpub(string xpub)
    {
        if (string.IsNullOrWhiteSpace(xpub))
            return false;

        return XpubRegex.IsMatch(xpub);
    }

    /// <summary>
    /// Validates a Liquid address.
    /// </summary>
    public bool ValidateLiquidAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;

        return LiquidAddressRegex.IsMatch(address);
    }

    /// <summary>
    /// Creates a new batch execution record.
    /// </summary>
    public async Task<ExecutedBatch> CreateBatchAsync(
        string storeId,
        BatchExecutionType executionType,
        List<PendingAllocation> allocations,
        string? destinationAddress,
        decimal currentExchangeRate,
        string fiatCurrency)
    {
        await using var db = dbContextFactory.CreateContext();

        var totalSats = allocations.Sum(a => a.AllocatedSats);
        var fiatValue = (totalSats / 100_000_000m) * currentExchangeRate;

        // Calculate weighted average cost basis
        var weightedCostBasis = allocations.Sum(a => a.AllocatedSats * a.ExchangeRateAtReceipt) 
                                / (decimal)totalSats;

        var batch = new ExecutedBatch
        {
            StoreId = storeId,
            ExecutionType = executionType,
            Status = BatchStatus.Pending,
            TotalSats = totalSats,
            FeeSats = 0, // Will be updated after execution
            NetSats = totalSats,
            FiatValueAtExecution = fiatValue,
            FiatCurrency = fiatCurrency,
            ExchangeRateAtExecution = currentExchangeRate,
            WeightedAverageCostBasis = weightedCostBasis,
            DestinationAddress = destinationAddress,
            AllocationCount = allocations.Count,
            InitiatedAt = DateTimeOffset.UtcNow
        };

        db.ExecutedBatches.Add(batch);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Created batch {BatchId} for store {StoreId}: {TotalSats} sats ({FiatValue} {Currency})",
            batch.Id, storeId, totalSats, fiatValue, fiatCurrency);

        return batch;
    }

    /// <summary>
    /// Executes a cold storage sweep (on-chain transaction).
    /// NOTE: This is a placeholder - actual implementation would use BTCPay's wallet services.
    /// </summary>
    public async Task<BatchExecutionResult> ExecuteColdStorageSweepAsync(
        ExecutedBatch batch,
        StashSettings settings)
    {
        await using var db = dbContextFactory.CreateContext();
        var dbBatch = await db.ExecutedBatches.FindAsync(batch.Id);
        
        if (dbBatch == null)
            return new BatchExecutionResult { IsSuccess = false, ErrorMessage = "Batch not found" };

        try
        {
            dbBatch.Status = BatchStatus.Processing;
            await db.SaveChangesAsync();

            // TODO: Implement actual on-chain transaction using BTCPay's wallet services
            // This would involve:
            // 1. Get the store's wallet
            // 2. Build a transaction to the destination address
            // 3. Sign and broadcast the transaction
            // 4. Record the transaction ID

            // For now, we'll simulate the execution
            logger.LogWarning(
                "Cold storage sweep execution not yet implemented for batch {BatchId}",
                batch.Id);

            // Placeholder: In a real implementation, this would be the actual transaction
            dbBatch.Status = BatchStatus.Failed;
            dbBatch.ErrorMessage = "Cold storage sweep not yet implemented. Manual withdrawal required.";
            dbBatch.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return new BatchExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = "Cold storage sweep not yet implemented. Please manually withdraw funds.",
                BatchId = batch.Id
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing cold storage sweep for batch {BatchId}", batch.Id);
            
            dbBatch.Status = BatchStatus.Failed;
            dbBatch.ErrorMessage = ex.Message;
            dbBatch.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return new BatchExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                BatchId = batch.Id
            };
        }
    }

    /// <summary>
    /// Executes a Liquid swap via Boltz.
    /// NOTE: This is a placeholder - actual implementation would use Boltz API.
    /// </summary>
    public async Task<BatchExecutionResult> ExecuteLiquidSwapAsync(
        ExecutedBatch batch,
        StashSettings settings)
    {
        await using var db = dbContextFactory.CreateContext();
        var dbBatch = await db.ExecutedBatches.FindAsync(batch.Id);
        
        if (dbBatch == null)
            return new BatchExecutionResult { IsSuccess = false, ErrorMessage = "Batch not found" };

        try
        {
            dbBatch.Status = BatchStatus.Processing;
            await db.SaveChangesAsync();

            // TODO: Implement Boltz API integration
            // This would involve:
            // 1. Create a swap request with Boltz
            // 2. Generate Lightning invoice to pay
            // 3. Pay the invoice from the store's Lightning node
            // 4. Wait for Liquid USDT to be received
            // 5. Record the swap details

            logger.LogWarning(
                "Liquid swap execution not yet implemented for batch {BatchId}",
                batch.Id);

            dbBatch.Status = BatchStatus.Failed;
            dbBatch.ErrorMessage = "Liquid swap via Boltz not yet implemented.";
            dbBatch.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return new BatchExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = "Liquid swap via Boltz not yet implemented.",
                BatchId = batch.Id
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing Liquid swap for batch {BatchId}", batch.Id);
            
            dbBatch.Status = BatchStatus.Failed;
            dbBatch.ErrorMessage = ex.Message;
            dbBatch.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return new BatchExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                BatchId = batch.Id
            };
        }
    }

    /// <summary>
    /// Gets batch history for a store.
    /// </summary>
    public async Task<List<ExecutedBatch>> GetBatchHistoryAsync(
        string storeId,
        int limit = 50,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        await using var db = dbContextFactory.CreateContext();
        var query = db.ExecutedBatches
            .Where(b => b.StoreId == storeId);

        if (from.HasValue)
            query = query.Where(b => b.InitiatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(b => b.InitiatedAt <= to.Value);

        return await query
            .OrderByDescending(b => b.InitiatedAt)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Gets a batch with its allocations.
    /// </summary>
    public async Task<ExecutedBatch?> GetBatchWithAllocationsAsync(string batchId)
    {
        await using var db = dbContextFactory.CreateContext();
        return await db.ExecutedBatches
            .Include(b => b.Allocations)
            .FirstOrDefaultAsync(b => b.Id == batchId);
    }

    /// <summary>
    /// Gets lifetime stats for a store.
    /// </summary>
    public async Task<StashLifetimeStats> GetLifetimeStatsAsync(string storeId)
    {
        await using var db = dbContextFactory.CreateContext();
        
        var completedBatches = await db.ExecutedBatches
            .Where(b => b.StoreId == storeId && b.Status == BatchStatus.Completed)
            .ToListAsync();

        return new StashLifetimeStats
        {
            TotalBatches = completedBatches.Count,
            TotalSatsStashed = completedBatches.Sum(b => b.NetSats),
            TotalFiatStashed = completedBatches.Sum(b => b.FiatValueAtExecution),
            TotalFeesPaid = completedBatches.Sum(b => b.FeeSats)
        };
    }
}

public class BatchExecutionResult
{
    public bool IsSuccess { get; set; }
    public string? BatchId { get; set; }
    public string? TransactionId { get; set; }
    public string? SwapId { get; set; }
    public long FeeSats { get; set; }
    public string? ErrorMessage { get; set; }
}

public class StashLifetimeStats
{
    public int TotalBatches { get; set; }
    public long TotalSatsStashed { get; set; }
    public decimal TotalFiatStashed { get; set; }
    public long TotalFeesPaid { get; set; }
}

