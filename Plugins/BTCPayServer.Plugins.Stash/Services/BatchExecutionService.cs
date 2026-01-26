#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Stash.Data;
using BTCPayServer.Plugins.Stash.Data.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;

namespace BTCPayServer.Plugins.Stash.Services;

public class BatchExecutionService(
    PluginDbContextFactory dbContextFactory,
    BTCPayServerEnvironment environment,
    StoreRepository storeRepository,
    BTCPayNetworkProvider networkProvider,
    ExplorerClientProvider explorerClientProvider,
    BTCPayWalletProvider walletProvider,
    PaymentMethodHandlerDictionary handlers,
    IFeeProviderFactory feeProviderFactory,
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

            // Get the store and its wallet configuration
            var store = await storeRepository.FindStore(batch.StoreId);
            if (store == null)
            {
                return await FailBatchAsync(dbBatch, "Store not found.", BatchErrorType.ExecutionError, false);
            }

            // Get the BTC network
            var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
            if (network == null)
            {
                return await FailBatchAsync(dbBatch, "Bitcoin network not available.", BatchErrorType.ExecutionError, false);
            }

            // Get derivation scheme settings (wallet config)
            var derivationScheme = store.GetDerivationSchemeSettings(handlers, "BTC");
            if (derivationScheme == null)
            {
                return await FailBatchAsync(dbBatch, "No Bitcoin wallet configured for this store. Please set up an on-chain wallet first.", BatchErrorType.ExecutionError, false);
            }

            // Check if it's a hot wallet (can sign transactions)
            if (!derivationScheme.IsHotWallet)
            {
                return await FailBatchAsync(dbBatch, "Store wallet is not a hot wallet. Cold storage sweep requires a hot wallet that can sign transactions automatically. Please enable hot wallet or manually withdraw funds.", BatchErrorType.ExecutionError, false);
            }

            // Check if NBXplorer is available
            if (!explorerClientProvider.IsAvailable("BTC"))
            {
                return await FailBatchAsync(dbBatch, "NBXplorer is not available. Please try again later.", BatchErrorType.NetworkError, true);
            }

            var explorerClient = explorerClientProvider.GetExplorerClient("BTC");

            // Get the account key for signing
            var extKeyStr = await explorerClient.GetMetadataAsync<string>(
                derivationScheme.AccountDerivation,
                WellknownMetadataKeys.AccountHDKey);

            if (string.IsNullOrEmpty(extKeyStr))
            {
                return await FailBatchAsync(dbBatch, "Could not retrieve wallet signing key. Ensure the wallet is properly configured as a hot wallet.", BatchErrorType.ExecutionError, false);
            }

            // Get wallet and UTXOs
            var wallet = walletProvider.GetWallet("BTC");
            var utxos = (await wallet.GetUnspentCoins(derivationScheme.AccountDerivation)).ToArray();
            
            if (!utxos.Any())
            {
                return await FailBatchAsync(dbBatch, "No unspent outputs available in the wallet.", BatchErrorType.InsufficientFunds, false);
            }

            var coins = utxos.Select(u => u.Coin).ToArray();
            var totalAvailable = coins.Sum(c => c.Amount.Satoshi);

            // Check if we have enough funds
            if (totalAvailable < batch.TotalSats)
            {
                return await FailBatchAsync(dbBatch, $"Insufficient funds. Available: {totalAvailable} sats, Required: {batch.TotalSats} sats.", BatchErrorType.InsufficientFunds, false);
            }

            // Parse account key and derive signing keys
            var accountKey = ExtKey.Parse(extKeyStr, network.NBitcoinNetwork);
            var keys = utxos.Select(u => accountKey.Derive(u.KeyPath).PrivateKey).ToArray();

            // Get change address
            var changeAddress = await explorerClient.GetUnusedAsync(
                derivationScheme.AccountDerivation, DerivationFeature.Change, 0, true);

            // Get fee rate based on settings
            var feeProvider = feeProviderFactory.CreateFeeProvider(network);
            var feeRate = await feeProvider.GetFeeRateAsync(Math.Max(settings.FeeBlockTarget, 1));

            // Parse destination address
            BitcoinAddress destinationAddress;
            try
            {
                destinationAddress = BitcoinAddress.Create(settings.DestinationAddress!, network.NBitcoinNetwork);
            }
            catch (Exception ex)
            {
                return await FailBatchAsync(dbBatch, $"Invalid destination address: {ex.Message}", BatchErrorType.InvalidAddress, false);
            }

            // Build the transaction
            var txBuilder = network.NBitcoinNetwork.CreateTransactionBuilder()
                .AddCoins(coins)
                .AddKeys(keys)
                .Send(destinationAddress, new Money(batch.TotalSats, MoneyUnit.Satoshi))
                .SetChange(changeAddress.Address)
                .SendEstimatedFees(feeRate);

            Transaction signedTx;
            try
            {
                signedTx = txBuilder.BuildTransaction(true);
            }
            catch (NotEnoughFundsException ex)
            {
                return await FailBatchAsync(dbBatch, $"Not enough funds to cover transaction and fees: {ex.Message}", BatchErrorType.InsufficientFunds, false);
            }

            // Calculate actual fee
            var fee = signedTx.GetFee(coins);
            var feeSats = fee?.Satoshi ?? 0;
            var netSats = batch.TotalSats - feeSats;

            // Broadcast the transaction
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var broadcastResult = await explorerClient.BroadcastAsync(signedTx, cts.Token);

            if (!broadcastResult.Success)
            {
                var errorMsg = $"Transaction broadcast failed: {broadcastResult.RPCMessage ?? "Unknown error"}";
                return await FailBatchAsync(dbBatch, errorMsg, BatchErrorType.NetworkError, true);
            }

            var txHash = signedTx.GetHash();

            // Update batch with success details
            dbBatch.Status = BatchStatus.Completed;
            dbBatch.TransactionId = txHash.ToString();
            dbBatch.FeeSats = feeSats;
            dbBatch.NetSats = netSats;
            dbBatch.CompletedAt = DateTimeOffset.UtcNow;
            dbBatch.ErrorMessage = null;
            await db.SaveChangesAsync();

            logger.LogInformation(
                "Cold storage sweep completed for batch {BatchId}. TxId: {TxId}, Amount: {Amount} sats, Fee: {Fee} sats",
                batch.Id, txHash, netSats, feeSats);

            return new BatchExecutionResult
            {
                IsSuccess = true,
                BatchId = batch.Id,
                TransactionId = txHash.ToString(),
                FeeSats = feeSats
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

    private async Task<BatchExecutionResult> FailBatchAsync(
        ExecutedBatch batch, 
        string errorMessage, 
        BatchErrorType errorType, 
        bool isRetryable)
    {
        await using var db = dbContextFactory.CreateContext();
        var dbBatch = await db.ExecutedBatches.FindAsync(batch.Id);
        
        if (dbBatch != null)
        {
            dbBatch.Status = BatchStatus.Failed;
            dbBatch.ErrorMessage = errorMessage;
            dbBatch.IsRetryable = isRetryable;
            dbBatch.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        logger.LogWarning("Batch {BatchId} failed: {Error} (retryable: {IsRetryable})", 
            batch.Id, errorMessage, isRetryable);

        return new BatchExecutionResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            BatchId = batch.Id,
            ErrorType = errorType,
            IsRetryable = isRetryable
        };
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

