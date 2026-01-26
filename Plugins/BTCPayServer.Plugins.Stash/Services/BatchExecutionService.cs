#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Stash.Data;
using BTCPayServer.Plugins.Stash.Data.Models;
using BTCPayServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayServer.Plugins.Stash.Services;

public class BatchExecutionService(
    PluginDbContextFactory dbContextFactory,
    BTCPayServerEnvironment environment,
    ILogger<BatchExecutionService> logger)
{
    // Bitcoin address regex patterns
    private static readonly Regex BtcMainnetAddressRegex = new(
        @"^(bc1[a-zA-HJ-NP-Z0-9]{25,87}|[13][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        RegexOptions.Compiled);

    private static readonly Regex BtcTestnetAddressRegex = new(
        @"^(tb1[a-zA-HJ-NP-Z0-9]{25,87}|[2mn][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        RegexOptions.Compiled);

    private static readonly Regex BtcRegtestAddressRegex = new(
        @"^(bcrt1[a-zA-HJ-NP-Z0-9]{25,87}|[2mn][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        RegexOptions.Compiled);

    private static readonly Regex XpubRegex = new(
        @"^([xyztuvXYZTUV]pub[a-zA-HJ-NP-Z0-9]{100,120})$",
        RegexOptions.Compiled);

    // Liquid address regex (simplified)
    private static readonly Regex LiquidAddressRegex = new(
        @"^(ex1[a-zA-HJ-NP-Z0-9]{25,87}|lq1[a-zA-HJ-NP-Z0-9]{25,87}|[GHVW][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        RegexOptions.Compiled);

    /// <summary>
    /// Gets the current network type.
    /// </summary>
    public ChainName NetworkType => environment.NetworkType;

    /// <summary>
    /// Validates a Bitcoin address based on the current network environment.
    /// </summary>
    public AddressValidationResult ValidateBitcoinAddressForNetwork(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return new AddressValidationResult(false, "Address is required.");

        var networkType = environment.NetworkType;
        var expectedNetworkName = GetNetworkDisplayName(networkType);

        // Check if it's an XPUB (valid on all networks)
        if (ValidateXpub(address))
            return new AddressValidationResult(true, null);

        // Determine which network the address belongs to
        var isMainnetAddress = BtcMainnetAddressRegex.IsMatch(address);
        var isTestnetAddress = BtcTestnetAddressRegex.IsMatch(address);
        var isRegtestAddress = BtcRegtestAddressRegex.IsMatch(address);

        // Validate against current network using if-else (ChainName is a struct, can't use switch pattern matching)
        if (networkType == ChainName.Mainnet)
        {
            if (isMainnetAddress)
                return new AddressValidationResult(true, null);
            if (isTestnetAddress || isRegtestAddress)
                return new AddressValidationResult(false, 
                    $"This appears to be a testnet/regtest address, but you are running on {expectedNetworkName}. Please use a mainnet address (starting with bc1, 1, or 3).");
        }
        else if (networkType == ChainName.Testnet)
        {
            if (isTestnetAddress)
                return new AddressValidationResult(true, null);
            if (isMainnetAddress)
                return new AddressValidationResult(false, 
                    $"This appears to be a mainnet address, but you are running on {expectedNetworkName}. Please use a testnet address (starting with tb1, m, n, or 2).");
            if (isRegtestAddress)
                return new AddressValidationResult(false, 
                    $"This appears to be a regtest address, but you are running on {expectedNetworkName}. Please use a testnet address (starting with tb1, m, n, or 2).");
        }
        else if (networkType == ChainName.Regtest)
        {
            if (isRegtestAddress)
                return new AddressValidationResult(true, null);
            if (isMainnetAddress)
                return new AddressValidationResult(false, 
                    $"This appears to be a mainnet address, but you are running on {expectedNetworkName}. Please use a regtest address (starting with bcrt1, m, n, or 2).");
            if (isTestnetAddress)
                return new AddressValidationResult(false, 
                    $"This appears to be a testnet address, but you are running on {expectedNetworkName}. Please use a regtest address (starting with bcrt1, m, n, or 2).");
        }

        return new AddressValidationResult(false, 
            $"Invalid Bitcoin address format. Please enter a valid {expectedNetworkName} address.");
    }

    /// <summary>
    /// Validates a Bitcoin address (legacy method for backwards compatibility).
    /// </summary>
    public bool ValidateBitcoinAddress(string address, bool isTestnet = false)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;

        if (isTestnet)
            return BtcTestnetAddressRegex.IsMatch(address) || BtcRegtestAddressRegex.IsMatch(address);
        
        return BtcMainnetAddressRegex.IsMatch(address);
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

    private static string GetNetworkDisplayName(ChainName network)
    {
        if (network == ChainName.Mainnet)
            return "mainnet";
        if (network == ChainName.Testnet)
            return "testnet";
        if (network == ChainName.Regtest)
            return "regtest";
        return network.ToString().ToLowerInvariant();
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
    /// Validates destination address before batch execution.
    /// </summary>
    public AddressValidationResult ValidateDestinationForExecution(StashSettings settings)
    {
        if (settings.DestinationType == StashDestinationType.ColdStorage)
        {
            if (string.IsNullOrWhiteSpace(settings.DestinationAddress))
                return new AddressValidationResult(false, "No destination address configured. Please configure a Bitcoin address in your Stash settings.");

            return ValidateBitcoinAddressForNetwork(settings.DestinationAddress);
        }
        else if (settings.DestinationType == StashDestinationType.LiquidSwap)
        {
            if (string.IsNullOrWhiteSpace(settings.LiquidAddress))
                return new AddressValidationResult(false, "No Liquid address configured. Please configure a Liquid address in your Stash settings.");

            if (!ValidateLiquidAddress(settings.LiquidAddress))
                return new AddressValidationResult(false, "Invalid Liquid address format. Please check your Liquid address in settings.");
        }

        return new AddressValidationResult(true, null);
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
            // Validate destination address before execution
            var addressValidation = ValidateDestinationForExecution(settings);
            if (!addressValidation.IsValid)
            {
                dbBatch.Status = BatchStatus.Failed;
                dbBatch.ErrorMessage = addressValidation.ErrorMessage;
                dbBatch.IsRetryable = false; // Address errors require user intervention
                dbBatch.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();

                logger.LogWarning(
                    "Batch {BatchId} failed address validation (not retryable): {Error}",
                    batch.Id, addressValidation.ErrorMessage);

                return new BatchExecutionResult
                {
                    IsSuccess = false,
                    ErrorMessage = addressValidation.ErrorMessage,
                    BatchId = batch.Id,
                    ErrorType = BatchErrorType.InvalidAddress,
                    IsRetryable = false
                };
            }

            dbBatch.Status = BatchStatus.Processing;
            dbBatch.RetryCount++;
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
            // NotImplemented is not retryable - requires code changes
            dbBatch.Status = BatchStatus.Failed;
            dbBatch.ErrorMessage = "Cold storage sweep not yet implemented. Manual withdrawal required.";
            dbBatch.IsRetryable = false;
            dbBatch.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return new BatchExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = "Cold storage sweep not yet implemented. Please manually withdraw funds.",
                BatchId = batch.Id,
                ErrorType = BatchErrorType.NotImplemented,
                IsRetryable = false
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing cold storage sweep for batch {BatchId}", batch.Id);
            
            // Network/transient errors are retryable
            var isRetryable = IsTransientError(ex);
            
            dbBatch.Status = BatchStatus.Failed;
            dbBatch.ErrorMessage = ex.Message;
            dbBatch.IsRetryable = isRetryable;
            dbBatch.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return new BatchExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                BatchId = batch.Id,
                ErrorType = BatchErrorType.ExecutionError,
                IsRetryable = isRetryable
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
            // Validate destination address before execution
            var addressValidation = ValidateDestinationForExecution(settings);
            if (!addressValidation.IsValid)
            {
                dbBatch.Status = BatchStatus.Failed;
                dbBatch.ErrorMessage = addressValidation.ErrorMessage;
                dbBatch.IsRetryable = false; // Address errors require user intervention
                dbBatch.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();

                logger.LogWarning(
                    "Batch {BatchId} failed address validation (not retryable): {Error}",
                    batch.Id, addressValidation.ErrorMessage);

                return new BatchExecutionResult
                {
                    IsSuccess = false,
                    ErrorMessage = addressValidation.ErrorMessage,
                    BatchId = batch.Id,
                    ErrorType = BatchErrorType.InvalidAddress,
                    IsRetryable = false
                };
            }

            dbBatch.Status = BatchStatus.Processing;
            dbBatch.RetryCount++;
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

            // NotImplemented is not retryable - requires code changes
            dbBatch.Status = BatchStatus.Failed;
            dbBatch.ErrorMessage = "Liquid swap via Boltz not yet implemented.";
            dbBatch.IsRetryable = false;
            dbBatch.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return new BatchExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = "Liquid swap via Boltz not yet implemented.",
                BatchId = batch.Id,
                ErrorType = BatchErrorType.NotImplemented,
                IsRetryable = false
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing Liquid swap for batch {BatchId}", batch.Id);
            
            // Network/transient errors are retryable
            var isRetryable = IsTransientError(ex);
            
            dbBatch.Status = BatchStatus.Failed;
            dbBatch.ErrorMessage = ex.Message;
            dbBatch.IsRetryable = isRetryable;
            dbBatch.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return new BatchExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                BatchId = batch.Id,
                ErrorType = BatchErrorType.ExecutionError,
                IsRetryable = isRetryable
            };
        }
    }

    /// <summary>
    /// Determines if an exception is a transient error that may resolve on retry.
    /// </summary>
    private static bool IsTransientError(Exception ex)
    {
        // Network-related exceptions are typically transient
        return ex is System.Net.Http.HttpRequestException
            || ex is System.Net.Sockets.SocketException
            || ex is TimeoutException
            || ex is TaskCanceledException
            || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Maximum number of automatic retries for transient errors.
    /// </summary>
    public const int MaxAutoRetries = 5;

    /// <summary>
    /// Checks if a batch should be automatically retried.
    /// </summary>
    public bool ShouldAutoRetry(ExecutedBatch batch)
    {
        return batch.Status == BatchStatus.Failed 
            && batch.IsRetryable 
            && batch.RetryCount < MaxAutoRetries;
    }

    /// <summary>
    /// Gets failed batches that are eligible for automatic retry.
    /// </summary>
    public async Task<List<ExecutedBatch>> GetRetryableBatchesAsync(string storeId)
    {
        await using var db = dbContextFactory.CreateContext();
        return await db.ExecutedBatches
            .Where(b => b.StoreId == storeId 
                && b.Status == BatchStatus.Failed 
                && b.IsRetryable 
                && b.RetryCount < MaxAutoRetries)
            .ToListAsync();
    }

    /// <summary>
    /// Resets a failed batch to pending status for retry.
    /// </summary>
    public async Task<bool> ResetBatchForRetryAsync(string batchId)
    {
        await using var db = dbContextFactory.CreateContext();
        var batch = await db.ExecutedBatches.FindAsync(batchId);
        
        if (batch == null || batch.Status != BatchStatus.Failed)
            return false;

        batch.Status = BatchStatus.Pending;
        batch.ErrorMessage = null;
        batch.CompletedAt = null;
        // Note: We don't reset RetryCount here - it persists across manual retries
        await db.SaveChangesAsync();

        logger.LogInformation("Reset batch {BatchId} to pending status for retry (attempt {RetryCount})", 
            batchId, batch.RetryCount);
        return true;
    }

    /// <summary>
    /// Gets a batch by ID.
    /// </summary>
    public async Task<ExecutedBatch?> GetBatchAsync(string batchId)
    {
        await using var db = dbContextFactory.CreateContext();
        return await db.ExecutedBatches.FindAsync(batchId);
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
    public BatchErrorType ErrorType { get; set; } = BatchErrorType.None;
    public bool IsRetryable { get; set; } = true;
}

public enum BatchErrorType
{
    None = 0,
    InvalidAddress = 1,
    NotImplemented = 2,
    ExecutionError = 3,
    InsufficientFunds = 4,
    NetworkError = 5
}

public class StashLifetimeStats
{
    public int TotalBatches { get; set; }
    public long TotalSatsStashed { get; set; }
    public decimal TotalFiatStashed { get; set; }
    public long TotalFeesPaid { get; set; }
}

public class AddressValidationResult
{
    public bool IsValid { get; }
    public string? ErrorMessage { get; }

    public AddressValidationResult(bool isValid, string? errorMessage)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }
}

