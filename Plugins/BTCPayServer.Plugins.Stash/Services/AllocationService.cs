#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Stash.Data;
using BTCPayServer.Plugins.Stash.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Stash.Services;

public class AllocationService(
    PluginDbContextFactory dbContextFactory,
    ILogger<AllocationService> logger)
{
    /// <summary>
    /// Records a new allocation from a settled invoice.
    /// </summary>
    public async Task<PendingAllocation> CreateAllocationAsync(
        string storeId,
        string invoiceId,
        string? paymentMethod,
        long totalReceivedSats,
        decimal allocationPercentage,
        decimal exchangeRate,
        string fiatCurrency)
    {
        await using var db = dbContextFactory.CreateContext();

        // Check if we already processed this invoice
        var existing = await db.PendingAllocations
            .FirstOrDefaultAsync(a => a.InvoiceId == invoiceId);

        if (existing != null)
        {
            logger.LogDebug("Allocation already exists for invoice {InvoiceId}", invoiceId);
            return existing;
        }

        var allocatedSats = (long)(totalReceivedSats * (allocationPercentage / 100m));
        var fiatValue = (allocatedSats / 100_000_000m) * exchangeRate;

        var allocation = new PendingAllocation
        {
            StoreId = storeId,
            InvoiceId = invoiceId,
            PaymentMethod = paymentMethod,
            TotalReceivedSats = totalReceivedSats,
            AllocatedSats = allocatedSats,
            FiatValueAtReceipt = fiatValue,
            FiatCurrency = fiatCurrency,
            ExchangeRateAtReceipt = exchangeRate,
            SettledAt = DateTimeOffset.UtcNow
        };

        db.PendingAllocations.Add(allocation);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Created allocation for invoice {InvoiceId}: {AllocatedSats} sats ({FiatValue} {Currency})",
            invoiceId, allocatedSats, fiatValue, fiatCurrency);

        return allocation;
    }

    /// <summary>
    /// Gets all pending (unexecuted and not linked to a batch) allocations for a store.
    /// Allocations linked to a batch (even a failed one) are not included.
    /// </summary>
    public async Task<List<PendingAllocation>> GetPendingAllocationsAsync(string storeId)
    {
        await using var db = dbContextFactory.CreateContext();
        return await db.PendingAllocations
            .Where(a => a.StoreId == storeId && !a.IsExecuted && a.ExecutedBatchId == null)
            .OrderBy(a => a.SettledAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets the total pending allocation stats for a store.
    /// Only includes allocations not yet linked to any batch.
    /// </summary>
    public async Task<(long totalSats, decimal totalFiat, int count)> GetPendingTotalsAsync(string storeId)
    {
        await using var db = dbContextFactory.CreateContext();
        var pending = await db.PendingAllocations
            .Where(a => a.StoreId == storeId && !a.IsExecuted && a.ExecutedBatchId == null)
            .ToListAsync();

        return (
            pending.Sum(a => a.AllocatedSats),
            pending.Sum(a => a.FiatValueAtReceipt),
            pending.Count
        );
    }

    /// <summary>
    /// Marks allocations as executed and links them to a batch.
    /// </summary>
    public async Task MarkAllocationsExecutedAsync(List<string> allocationIds, string batchId)
    {
        await using var db = dbContextFactory.CreateContext();
        var allocations = await db.PendingAllocations
            .Where(a => allocationIds.Contains(a.Id))
            .ToListAsync();

        foreach (var allocation in allocations)
        {
            allocation.IsExecuted = true;
            allocation.ExecutedBatchId = batchId;
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Marked {Count} allocations as executed for batch {BatchId}",
            allocations.Count, batchId);
    }

    /// <summary>
    /// Marks all allocations linked to a batch as executed.
    /// Used when a batch retry succeeds.
    /// </summary>
    public async Task MarkBatchAllocationsExecutedAsync(string batchId)
    {
        await using var db = dbContextFactory.CreateContext();
        var allocations = await db.PendingAllocations
            .Where(a => a.ExecutedBatchId == batchId && !a.IsExecuted)
            .ToListAsync();

        foreach (var allocation in allocations)
        {
            allocation.IsExecuted = true;
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Marked {Count} allocations as executed for batch {BatchId} (retry success)",
            allocations.Count, batchId);
    }

    /// <summary>
    /// Unlinks allocations from a failed batch so they can be included in a new batch.
    /// Used when dismissing/cancelling a failed batch.
    /// </summary>
    public async Task UnlinkAllocationsFromBatchAsync(string batchId)
    {
        await using var db = dbContextFactory.CreateContext();
        var allocations = await db.PendingAllocations
            .Where(a => a.ExecutedBatchId == batchId && !a.IsExecuted)
            .ToListAsync();

        foreach (var allocation in allocations)
        {
            allocation.ExecutedBatchId = null;
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Unlinked {Count} allocations from failed batch {BatchId}",
            allocations.Count, batchId);
    }

    /// <summary>
    /// Resets (clears) all pending allocations for a store without moving funds.
    /// This is the "Emergency Stop" feature.
    /// </summary>
    public async Task<int> ResetPendingAllocationsAsync(string storeId)
    {
        await using var db = dbContextFactory.CreateContext();
        var pending = await db.PendingAllocations
            .Where(a => a.StoreId == storeId && !a.IsExecuted)
            .ToListAsync();

        db.PendingAllocations.RemoveRange(pending);
        await db.SaveChangesAsync();

        logger.LogWarning("Reset {Count} pending allocations for store {StoreId} (Emergency Stop)",
            pending.Count, storeId);

        return pending.Count;
    }

    /// <summary>
    /// Gets all allocations (including executed) for reporting.
    /// </summary>
    public async Task<List<PendingAllocation>> GetAllAllocationsAsync(
        string storeId, 
        DateTimeOffset? from = null, 
        DateTimeOffset? to = null)
    {
        await using var db = dbContextFactory.CreateContext();
        var query = db.PendingAllocations
            .Where(a => a.StoreId == storeId);

        if (from.HasValue)
            query = query.Where(a => a.SettledAt >= from.Value);

        if (to.HasValue)
            query = query.Where(a => a.SettledAt <= to.Value);

        return await query
            .OrderByDescending(a => a.SettledAt)
            .ToListAsync();
    }
}

