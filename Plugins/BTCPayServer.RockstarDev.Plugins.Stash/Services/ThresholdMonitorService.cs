using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Rating;
using BTCPayServer.RockstarDev.Plugins.Stash.Data;
using BTCPayServer.RockstarDev.Plugins.Stash.Data.Models;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BTCPayServer.RockstarDev.Plugins.Stash.Services;

/// <summary>
/// Periodically monitors pending allocations and triggers batch execution when thresholds are met.
/// </summary>
public class ThresholdMonitorService(
    PluginDbContextFactory dbContextFactory,
    AllocationService allocationService,
    BatchExecutionService batchExecutionService,
    RateFetcher rateFetcher,
    DefaultRulesCollection defaultRules,
    StoreRepository storeRepository,
    ILogger<ThresholdMonitorService> logger) : IPeriodicTask
{
    public async Task Do(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = dbContextFactory.CreateContext();

            // Check if database is ready (migrations may not have run yet)
            try
            {
                // Get all stores with enabled Stash configurations
                var enabledSettings = await db.StashSettings
                    .Where(s => s.IsEnabled)
                    .ToListAsync(cancellationToken);

                foreach (var settings in enabledSettings)
                {
                    try
                    {
                        await CheckAndExecuteThresholdAsync(settings, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error checking threshold for store {StoreId}", settings.StoreId);
                    }
                }
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01") // relation does not exist
            {
                // Database tables don't exist yet - migrations haven't run. This is expected on first startup.
                logger.LogDebug("Stash database tables not ready yet, waiting for migrations to complete");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in ThresholdMonitorService");
        }
    }

    private async Task CheckAndExecuteThresholdAsync(StashSettings settings, CancellationToken cancellationToken)
    {
        var (totalSats, historicalFiat, count) = await allocationService.GetPendingTotalsAsync(settings.StoreId);

        if (count == 0 || totalSats == 0)
        {
            logger.LogDebug("No pending allocations for store {StoreId}", settings.StoreId);
            return;
        }

        // Get current exchange rate for "Fiat-Targeted Logic"
        decimal currentRate = 0;
        try
        {
            var store = await storeRepository.FindStore(settings.StoreId);
            if (store != null)
            {
                var storeBlob = store.GetStoreBlob();
                var currencyPair = new CurrencyPair("BTC", settings.FiatCurrency);
                var rateRules = storeBlob.GetRateRules(defaultRules);
                var rates = rateFetcher.FetchRates(
                    new[] { currencyPair }.ToHashSet(), 
                    rateRules, 
                    new StoreIdRateContext(settings.StoreId), 
                    cancellationToken);
                
                if (rates.TryGetValue(currencyPair, out var rateTask))
                {
                    var rate = await rateTask;
                    if (rate.BidAsk != null)
                    {
                        currentRate = rate.BidAsk.Bid;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch exchange rate for threshold check");
            // Fall back to using historical fiat values
        }

        // Calculate current fiat value of pending allocations
        var currentFiatValue = currentRate > 0 
            ? (totalSats / 100_000_000m) * currentRate 
            : historicalFiat;

        logger.LogDebug(
            "Store {StoreId}: {TotalSats} sats pending, current value {CurrentFiat} {Currency}, threshold {Threshold}",
            settings.StoreId, totalSats, currentFiatValue, settings.FiatCurrency, settings.BatchThresholdFiat);

        // Check if threshold is met
        if (currentFiatValue < settings.BatchThresholdFiat)
        {
            logger.LogDebug("Threshold not met for store {StoreId}", settings.StoreId);
            return;
        }

        // Check if batch size justifies fees (minimum viable batch)
        if (totalSats < settings.MinimumBatchSats)
        {
            logger.LogDebug(
                "Batch too small for store {StoreId}: {TotalSats} < {MinimumSats}",
                settings.StoreId, totalSats, settings.MinimumBatchSats);
            return;
        }

        // Check destination is configured
        if (string.IsNullOrWhiteSpace(settings.DestinationAddress) && 
            settings.DestinationType == StashDestinationType.ColdStorage)
        {
            logger.LogWarning("No destination address configured for store {StoreId}", settings.StoreId);
            return;
        }

        // Threshold met - execute batch
        logger.LogInformation(
            "Threshold met for store {StoreId}: {CurrentFiat} {Currency} >= {Threshold}. Executing batch.",
            settings.StoreId, currentFiatValue, settings.FiatCurrency, settings.BatchThresholdFiat);

        await ExecuteBatchAsync(settings, currentRate, cancellationToken);
    }

    private async Task ExecuteBatchAsync(StashSettings settings, decimal currentRate, CancellationToken cancellationToken)
    {
        try
        {
            // Get pending allocations
            var allocations = await allocationService.GetPendingAllocationsAsync(settings.StoreId);
            if (!allocations.Any())
            {
                logger.LogWarning("No allocations found when executing batch for store {StoreId}", settings.StoreId);
                return;
            }

            // Create batch record
            var executionType = settings.DestinationType == StashDestinationType.ColdStorage
                ? BatchExecutionType.ColdStorage
                : BatchExecutionType.LiquidSwap;

            var batch = await batchExecutionService.CreateBatchAsync(
                settings.StoreId,
                executionType,
                allocations,
                settings.DestinationAddress,
                currentRate,
                settings.FiatCurrency);

            // Execute based on type
            BatchExecutionResult result;
            if (executionType == BatchExecutionType.ColdStorage)
            {
                result = await batchExecutionService.ExecuteColdStorageSweepAsync(batch, settings);
            }
            else
            {
                result = await batchExecutionService.ExecuteLiquidSwapAsync(batch, settings);
            }

            if (result.IsSuccess)
            {
                // Mark allocations as executed
                var allocationIds = allocations.Select(a => a.Id).ToList();
                await allocationService.MarkAllocationsExecutedAsync(allocationIds, batch.Id);

                logger.LogInformation(
                    "Batch {BatchId} executed successfully for store {StoreId}",
                    batch.Id, settings.StoreId);
            }
            else
            {
                logger.LogWarning(
                    "Batch {BatchId} failed for store {StoreId}: {Error}",
                    batch.Id, settings.StoreId, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing batch for store {StoreId}", settings.StoreId);
        }
    }
}
