#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins.Stash.Data;
using BTCPayServer.Plugins.Stash.Data.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    BoltzApiService boltzApiService,
    LightningClientFactoryService lightningClientFactory,
    IOptions<LightningNetworkOptions> lightningNetworkOptions,
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

    // Liquid address regex patterns
    // Mainnet: ex1... (blech32), lq1... (blech32m), or confidential addresses starting with VJL, VTp, etc.
    private static readonly Regex LiquidMainnetAddressRegex = new(
        @"^(ex1[a-zA-HJ-NP-Z0-9]{25,120}|lq1[a-zA-HJ-NP-Z0-9]{25,120}|VJL[a-km-zA-HJ-NP-Z1-9]{76,100}|VTp[a-km-zA-HJ-NP-Z1-9]{76,100}|[GHVW][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        RegexOptions.Compiled);

    // Testnet/Regtest: tex1... (blech32), tlq1... (blech32m)
    private static readonly Regex LiquidTestnetAddressRegex = new(
        @"^(tex1[a-zA-HJ-NP-Z0-9]{25,120}|tlq1[a-zA-HJ-NP-Z0-9]{25,120}|ert1[a-zA-HJ-NP-Z0-9]{25,120}|el1[a-zA-HJ-NP-Z0-9]{25,120})$",
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
    /// Validates a Liquid address based on the current network environment.
    /// </summary>
    public AddressValidationResult ValidateLiquidAddressForNetwork(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return new AddressValidationResult(false, "Liquid address is required.");

        var networkType = environment.NetworkType;
        var isMainnetAddress = LiquidMainnetAddressRegex.IsMatch(address);
        var isTestnetAddress = LiquidTestnetAddressRegex.IsMatch(address);

        if (networkType == ChainName.Mainnet)
        {
            if (isMainnetAddress)
                return new AddressValidationResult(true, null);
            if (isTestnetAddress)
                return new AddressValidationResult(false,
                    "This appears to be a testnet Liquid address, but you are running on mainnet. Please use a mainnet Liquid address (starting with ex1, lq1, or VJL/VTp).");
        }
        else // Testnet or Regtest
        {
            if (isTestnetAddress)
                return new AddressValidationResult(true, null);
            if (isMainnetAddress)
                return new AddressValidationResult(false,
                    "This appears to be a mainnet Liquid address, but you are running on testnet. Please use a testnet Liquid address (starting with tex1, tlq1, ert1, or el1).");
        }

        return new AddressValidationResult(false,
            "Invalid Liquid address format. Please enter a valid Liquid network address.");
    }

    /// <summary>
    /// Validates a Liquid address (legacy method for backwards compatibility).
    /// </summary>
    public bool ValidateLiquidAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;

        return LiquidMainnetAddressRegex.IsMatch(address) || LiquidTestnetAddressRegex.IsMatch(address);
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
    /// Creates a new batch execution record and links allocations to it.
    /// Allocations are linked immediately (ExecutedBatchId set) but IsExecuted remains false until batch succeeds.
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

        // Link allocations to this batch immediately (but don't mark as executed yet)
        // This ensures we know which allocations belong to this batch even if it fails
        var allocationIds = allocations.Select(a => a.Id).ToList();
        var dbAllocations = await db.PendingAllocations
            .Where(a => allocationIds.Contains(a.Id))
            .ToListAsync();
        
        foreach (var allocation in dbAllocations)
        {
            allocation.ExecutedBatchId = batch.Id;
            // IsExecuted remains false until batch succeeds
        }
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Created batch {BatchId} for store {StoreId}: {TotalSats} sats ({FiatValue} {Currency}), linked {Count} allocations",
            batch.Id, storeId, totalSats, fiatValue, fiatCurrency, dbAllocations.Count);

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

            return ValidateLiquidAddressForNetwork(settings.LiquidAddress);
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
    /// Executes a Liquid swap via Boltz reverse submarine swap.
    /// Flow: Lightning BTC -> Liquid BTC (L-BTC)
    /// </summary>
    public async Task<BatchExecutionResult> ExecuteLiquidSwapAsync(
        ExecutedBatch batch,
        StashSettings settings)
    {
        await using var db = dbContextFactory.CreateContext();
        var dbBatch = await db.ExecutedBatches.FindAsync(batch.Id);
        
        if (dbBatch == null)
            return new BatchExecutionResult { IsSuccess = false, ErrorMessage = "Batch not found" };

        BoltzSwap? boltzSwap = null;

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

            // Step 1: Get the store and Lightning client
            var store = await storeRepository.FindStore(batch.StoreId);
            if (store == null)
            {
                return await FailBatchAsync(dbBatch, "Store not found.", BatchErrorType.ExecutionError, false);
            }

            var lightningClient = await GetLightningClientAsync(store);
            if (lightningClient == null)
            {
                return await FailBatchAsync(dbBatch, 
                    "No Lightning node configured for this store. Liquid swaps require a Lightning-enabled wallet.", 
                    BatchErrorType.ExecutionError, false);
            }

            // Step 2: Get a quote from Boltz
            logger.LogInformation(
                "Getting Boltz quote for batch {BatchId}: {Sats} sats",
                batch.Id, batch.TotalSats);

            var quote = await boltzApiService.GetReverseQuoteAsync(batch.TotalSats);
            if (quote == null)
            {
                return await FailBatchAsync(dbBatch, 
                    "Failed to get quote from Boltz. Please try again later.", 
                    BatchErrorType.NetworkError, true);
            }

            logger.LogInformation(
                "Boltz quote received: onchain={OnchainAmount}, minerFee={MinerFee}, serviceFee={ServiceFee}",
                quote.OnchainAmount, quote.MinerFee, quote.ServiceFee);

            // Step 3: Generate a claim keypair for the swap
            // In production, this should use proper key derivation from the store's wallet
            var claimKeyPair = GenerateClaimKeyPair();

            // Step 4: Create the reverse swap with Boltz
            var createRequest = new BoltzCreateReverseSwapRequest
            {
                From = "BTC",
                To = "L-BTC", // Liquid Bitcoin - Boltz will handle the asset
                InvoiceAmount = batch.TotalSats,
                ClaimPublicKey = claimKeyPair.PublicKeyHex,
                Address = settings.LiquidAddress, // Destination address for the Liquid funds
                ReferralId = "btcpay-stash",
                Description = $"Stash batch {batch.Id}"
            };

            logger.LogInformation(
                "Creating Boltz reverse swap for batch {BatchId}",
                batch.Id);

            BoltzCreateReverseSwapResponse? swapResponse;
            try
            {
                swapResponse = await boltzApiService.CreateReverseSwapAsync(createRequest);
                if (swapResponse == null)
                {
                    return await FailBatchAsync(dbBatch, 
                        "Failed to create swap with Boltz.", 
                        BatchErrorType.NetworkError, true);
                }
            }
            catch (BoltzApiException ex)
            {
                var isRetryable = BoltzApiService.IsTransientError(ex);
                return await FailBatchAsync(dbBatch, 
                    $"Boltz API error: {ex.Message}", 
                    BatchErrorType.NetworkError, isRetryable);
            }

            // Step 5: Create BoltzSwap record to track the swap
            boltzSwap = new BoltzSwap
            {
                StoreId = batch.StoreId,
                BatchId = batch.Id,
                BoltzSwapId = swapResponse.Id,
                State = BoltzSwapState.Created,
                BoltzStatus = BoltzSwapStatus.Created,
                Invoice = swapResponse.Invoice,
                InvoiceAmountSats = batch.TotalSats,
                OnchainAmountSats = swapResponse.OnchainAmount,
                MinerFeeSats = quote.MinerFee,
                ServiceFeeSats = quote.ServiceFee,
                DestinationAddress = settings.LiquidAddress,
                LockupAddress = swapResponse.LockupAddress,
                TimeoutBlockHeight = swapResponse.TimeoutBlockHeight,
                BlindingKey = swapResponse.BlindingKey,
                ClaimPublicKey = claimKeyPair.PublicKeyHex,
                SwapTreeJson = swapResponse.SwapTree != null 
                    ? JsonSerializer.Serialize(swapResponse.SwapTree) 
                    : null
            };
            db.BoltzSwaps.Add(boltzSwap);
            await db.SaveChangesAsync();

            logger.LogInformation(
                "Boltz swap created: {SwapId}, invoice amount={InvoiceAmount}, onchain amount={OnchainAmount}",
                swapResponse.Id, batch.TotalSats, swapResponse.OnchainAmount);

            // Update batch with swap ID
            dbBatch.SwapId = swapResponse.Id;
            await db.SaveChangesAsync();

            // Step 6: Pay the Lightning invoice
            logger.LogInformation(
                "Paying Boltz Lightning invoice for batch {BatchId}, swap {SwapId}",
                batch.Id, swapResponse.Id);

            var payResult = await PayLightningInvoiceAsync(lightningClient, swapResponse.Invoice);
            
            if (!payResult.Success)
            {
                var errorMsg = $"Failed to pay Lightning invoice: {payResult.ErrorMessage}";
                boltzSwap.State = BoltzSwapState.Failed;
                boltzSwap.ErrorMessage = errorMsg;
                boltzSwap.IsRetryable = payResult.IsRetryable;
                boltzSwap.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();

                return await FailBatchAsync(dbBatch, errorMsg, BatchErrorType.ExecutionError, payResult.IsRetryable);
            }

            // Update swap state after successful payment
            boltzSwap.State = BoltzSwapState.Paid;
            boltzSwap.BoltzStatus = BoltzSwapStatus.InvoicePaid;
            boltzSwap.Preimage = payResult.Preimage;
            boltzSwap.PaidAt = DateTimeOffset.UtcNow;
            boltzSwap.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation(
                "Lightning invoice paid for swap {SwapId}. Waiting for Boltz to send Liquid funds.",
                swapResponse.Id);

            // Step 7: Wait for Boltz to complete the swap (with polling)
            var swapResult = await WaitForSwapCompletionAsync(
                swapResponse.Id, 
                boltzSwap, 
                TimeSpan.FromMinutes(10)); // Timeout after 10 minutes

            if (!swapResult.Success)
            {
                boltzSwap.State = BoltzSwapState.Failed;
                boltzSwap.ErrorMessage = swapResult.ErrorMessage;
                boltzSwap.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();

                return await FailBatchAsync(dbBatch, 
                    swapResult.ErrorMessage ?? "Swap failed", 
                    BatchErrorType.ExecutionError, swapResult.IsRetryable);
            }

            // Step 8: Swap completed successfully
            boltzSwap.State = BoltzSwapState.Completed;
            boltzSwap.BoltzStatus = BoltzSwapStatus.TransactionClaimed;
            boltzSwap.ClaimTransactionId = swapResult.TransactionId;
            boltzSwap.CompletedAt = DateTimeOffset.UtcNow;
            boltzSwap.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            // Update batch with success
            var totalFeeSats = boltzSwap.MinerFeeSats + boltzSwap.ServiceFeeSats;
            dbBatch.Status = BatchStatus.Completed;
            dbBatch.TransactionId = swapResult.TransactionId;
            dbBatch.SwapId = swapResponse.Id;
            dbBatch.FeeSats = totalFeeSats;
            dbBatch.NetSats = boltzSwap.OnchainAmountSats;
            dbBatch.UsdtReceived = batch.FiatValueAtExecution; // Approximate - actual USDT would need conversion
            dbBatch.DestinationAddress = settings.LiquidAddress;
            dbBatch.CompletedAt = DateTimeOffset.UtcNow;
            dbBatch.ErrorMessage = null;
            await db.SaveChangesAsync();

            logger.LogInformation(
                "Liquid swap completed for batch {BatchId}. Swap={SwapId}, TxId={TxId}, Amount={Amount} sats, Fee={Fee} sats",
                batch.Id, swapResponse.Id, swapResult.TransactionId, boltzSwap.OnchainAmountSats, totalFeeSats);

            return new BatchExecutionResult
            {
                IsSuccess = true,
                BatchId = batch.Id,
                SwapId = swapResponse.Id,
                TransactionId = swapResult.TransactionId,
                FeeSats = totalFeeSats
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing Liquid swap for batch {BatchId}", batch.Id);
            
            // Update swap state if we have one
            if (boltzSwap != null)
            {
                boltzSwap.State = BoltzSwapState.Failed;
                boltzSwap.ErrorMessage = ex.Message;
                boltzSwap.IsRetryable = IsTransientError(ex) || BoltzApiService.IsTransientError(ex);
                boltzSwap.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            // Network/transient errors are retryable
            var isRetryable = IsTransientError(ex) || BoltzApiService.IsTransientError(ex);
            
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
    /// Gets the Lightning client for a store.
    /// </summary>
    private async Task<ILightningClient?> GetLightningClientAsync(StoreData store)
    {
        var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
        if (network == null)
            return null;

        var id = PaymentTypes.LN.GetPaymentMethodId("BTC");
        var existing = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(id, handlers);
        if (existing == null)
            return null;

        if (existing.GetExternalLightningUrl() is { } connectionString)
        {
            return lightningClientFactory.Create(connectionString, network);
        }

        if (existing.IsInternalNode && 
            lightningNetworkOptions.Value.InternalLightningByCryptoCode.TryGetValue("BTC", out var internalNode))
        {
            return internalNode;
        }

        return null;
    }

    /// <summary>
    /// Pays a Lightning invoice and returns the result.
    /// </summary>
    private async Task<LightningPaymentResult> PayLightningInvoiceAsync(
        ILightningClient lightningClient, 
        string bolt11Invoice)
    {
        try
        {
            var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
            if (network == null || !BOLT11PaymentRequest.TryParse(bolt11Invoice, out var parsed, network.NBitcoinNetwork))
            {
                return new LightningPaymentResult
                {
                    Success = false,
                    ErrorMessage = "Invalid BOLT11 invoice format.",
                    IsRetryable = false
                };
            }

            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var payResponse = await lightningClient.Pay(bolt11Invoice, new PayInvoiceParams(), cts.Token);

            if (payResponse.Result == PayResult.Ok)
            {
                return new LightningPaymentResult
                {
                    Success = true,
                    Preimage = payResponse.Details?.Preimage?.ToString()
                };
            }

            var errorDetail = payResponse.ErrorDetail ?? payResponse.Result.ToString();
            var isRetryable = payResponse.Result == PayResult.CouldNotFindRoute;

            return new LightningPaymentResult
            {
                Success = false,
                ErrorMessage = $"Payment failed: {errorDetail}",
                IsRetryable = isRetryable
            };
        }
        catch (Exception ex)
        {
            return new LightningPaymentResult
            {
                Success = false,
                ErrorMessage = $"Payment error: {ex.Message}",
                IsRetryable = IsTransientError(ex)
            };
        }
    }

    /// <summary>
    /// Waits for a Boltz swap to complete by polling the status.
    /// </summary>
    private async Task<SwapCompletionResult> WaitForSwapCompletionAsync(
        string swapId, 
        BoltzSwap boltzSwap,
        TimeSpan timeout)
    {
        var startTime = DateTimeOffset.UtcNow;
        var pollInterval = TimeSpan.FromSeconds(5);

        while (DateTimeOffset.UtcNow - startTime < timeout)
        {
            try
            {
                var status = await boltzApiService.GetSwapStatusAsync(swapId);
                if (status == null)
                {
                    logger.LogWarning("Failed to get swap status for {SwapId}", swapId);
                    await Task.Delay(pollInterval);
                    continue;
                }

                logger.LogDebug("Swap {SwapId} status: {Status}", swapId, status.Status);

                // Update local swap status
                await using var db = dbContextFactory.CreateContext();
                var dbSwap = await db.BoltzSwaps.FindAsync(boltzSwap.Id);
                if (dbSwap != null)
                {
                    dbSwap.BoltzStatus = status.Status;
                    dbSwap.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }

                // Check for final states
                if (BoltzSwapStatus.IsSuccessState(status.Status))
                {
                    return new SwapCompletionResult
                    {
                        Success = true,
                        TransactionId = status.Transaction?.Id
                    };
                }

                if (BoltzSwapStatus.IsFailedState(status.Status))
                {
                    var failureReason = status.FailureReason ?? $"Swap failed with status: {status.Status}";
                    return new SwapCompletionResult
                    {
                        Success = false,
                        ErrorMessage = failureReason,
                        IsRetryable = status.Status == BoltzSwapStatus.TransactionLockupFailed
                    };
                }

                // Still processing, wait and poll again
                await Task.Delay(pollInterval);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error polling swap status for {SwapId}", swapId);
                await Task.Delay(pollInterval);
            }
        }

        // Timeout - the swap might still complete, but we can't wait longer
        return new SwapCompletionResult
        {
            Success = false,
            ErrorMessage = "Swap timed out waiting for completion. The swap may still complete - check the Boltz status.",
            IsRetryable = true
        };
    }

    /// <summary>
    /// Generates a claim keypair for Boltz swaps.
    /// In production, this should derive from the store's HD wallet.
    /// </summary>
    private static ClaimKeyPair GenerateClaimKeyPair()
    {
        // Generate a random private key
        var privateKeyBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(privateKeyBytes);

        var key = new Key(privateKeyBytes);
        var pubKey = key.PubKey;

        return new ClaimKeyPair
        {
            PrivateKeyHex = Convert.ToHexString(privateKeyBytes).ToLowerInvariant(),
            PublicKeyHex = Convert.ToHexString(pubKey.ToBytes()).ToLowerInvariant()
        };
    }

    private class ClaimKeyPair
    {
        public string PrivateKeyHex { get; set; } = null!;
        public string PublicKeyHex { get; set; } = null!;
    }

    private class LightningPaymentResult
    {
        public bool Success { get; set; }
        public string? Preimage { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsRetryable { get; set; }
    }

    private class SwapCompletionResult
    {
        public bool Success { get; set; }
        public string? TransactionId { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsRetryable { get; set; }
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

