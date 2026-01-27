using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Rating;
using BTCPayServer.Plugins.Stash.Data;
using BTCPayServer.Plugins.Stash.Data.Models;
using BTCPayServer.Plugins.Stash.Services;
using BTCPayServer.Plugins.Stash.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BTCPayServer.Plugins.Stash.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/stash")]
public class StashController(
    StashSettingsService settingsService,
    AllocationService allocationService,
    BatchExecutionService batchExecutionService,
    RateFetcher rateFetcher,
    DefaultRulesCollection defaultRules,
    StoreRepository storeRepository,
    PluginDbContextFactory dbContextFactory,
    ILogger<StashController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string storeId)
    {
        // Check if database is ready (migrations may still be running)
        if (!await IsDatabaseReadyAsync())
        {
            return View("MigrationPending");
        }

        var settings = await settingsService.GetSettingsAsync(storeId);
        var (pendingSats, pendingFiat, pendingCount) = await allocationService.GetPendingTotalsAsync(storeId);
        var lifetimeStats = await batchExecutionService.GetLifetimeStatsAsync(storeId);
        var recentBatches = await batchExecutionService.GetBatchHistoryAsync(storeId, 5);
        var recentAllocations = await allocationService.GetAllAllocationsAsync(storeId);

        // Get current exchange rate
        decimal currentRate = 0;
        var fiatCurrency = settings?.FiatCurrency ?? "USD";
        try
        {
            var store = await storeRepository.FindStore(storeId);
            if (store != null)
            {
                var storeBlob = store.GetStoreBlob();
                var currencyPair = new CurrencyPair("BTC", fiatCurrency);
                var rateRules = storeBlob.GetRateRules(defaultRules);
                var rates = rateFetcher.FetchRates(
                    new[] { currencyPair }.ToHashSet(), 
                    rateRules, 
                    new StoreIdRateContext(storeId), 
                    default);
                
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
        catch
        {
            // Ignore rate fetch errors
        }

        var currentFiatValue = currentRate > 0
            ? (pendingSats / 100_000_000m) * currentRate
            : pendingFiat;

        var model = new StashDashboardViewModel
        {
            Settings = settings,
            PendingSats = pendingSats,
            PendingFiat = pendingFiat,
            PendingCount = pendingCount,
            CurrentExchangeRate = currentRate,
            CurrentFiatValue = currentFiatValue,
            LifetimeStats = lifetimeStats,
            RecentBatches = recentBatches,
            RecentAllocations = recentAllocations.Take(10).ToList()
        };

        return View(model);
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        if (!await IsDatabaseReadyAsync())
        {
            return View("MigrationPending");
        }

        var settings = await settingsService.GetOrCreateSettingsAsync(storeId);
        var model = StashSettingsViewModel.FromModel(settings);
        return View(model);
    }

    [HttpPost("settings")]
    public async Task<IActionResult> Settings(string storeId, StashSettingsViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        // Validate fiat currency is in supported list
        if (!StashSettingsViewModel.SupportedCurrencies.Contains(model.FiatCurrency))
        {
            ModelState.AddModelError(nameof(model.FiatCurrency), 
                "Selected currency is not supported.");
            return View(model);
        }

        // Validate destination address based on type using network-aware validation
        if (model.IsEnabled && model.DestinationType == StashDestinationType.ColdStorage)
        {
            if (string.IsNullOrWhiteSpace(model.DestinationAddress))
            {
                ModelState.AddModelError(nameof(model.DestinationAddress), 
                    "Bitcoin destination address is required for cold storage mode.");
                return View(model);
            }

            // Use network-aware address validation
            var addressValidation = batchExecutionService.ValidateBitcoinAddressForNetwork(model.DestinationAddress);
            if (!addressValidation.IsValid)
            {
                ModelState.AddModelError(nameof(model.DestinationAddress), addressValidation.ErrorMessage!);
                return View(model);
            }
        }

        // Validate Liquid address for Liquid Swap mode
        if (model.IsEnabled && model.DestinationType == StashDestinationType.LiquidSwap)
        {
            if (string.IsNullOrWhiteSpace(model.LiquidAddress))
            {
                ModelState.AddModelError(nameof(model.LiquidAddress), 
                    "Liquid destination address is required for Liquid swap mode.");
                return View(model);
            }

            // Use network-aware validation (allows ert1/el1 for regtest)
            var liquidValidation = batchExecutionService.ValidateLiquidAddressForNetwork(model.LiquidAddress);
            if (!liquidValidation.IsValid)
            {
                ModelState.AddModelError(nameof(model.LiquidAddress), liquidValidation.ErrorMessage!);
                return View(model);
            }
        }

        var settings = model.ToModel(storeId);
        await settingsService.SaveSettingsAsync(settings);

        TempData[WellKnownTempData.SuccessMessage] = "Stash settings saved successfully.";
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpGet("batches")]
    public async Task<IActionResult> Batches(string storeId, DateTimeOffset? from, DateTimeOffset? to)
    {
        var settings = await settingsService.GetSettingsAsync(storeId);
        var batches = await batchExecutionService.GetBatchHistoryAsync(storeId, 100, from, to);

        var model = new BatchHistoryViewModel
        {
            Batches = batches,
            FromDate = from,
            ToDate = to,
            FiatCurrency = settings?.FiatCurrency ?? "USD"
        };

        return View(model);
    }

    [HttpGet("batches/{batchId}")]
    public async Task<IActionResult> BatchDetail(string storeId, string batchId)
    {
        var batch = await batchExecutionService.GetBatchWithAllocationsAsync(batchId);
        
        if (batch == null || batch.StoreId != storeId)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Batch not found.";
            return RedirectToAction(nameof(Batches), new { storeId });
        }

        // Check if there's an address validation issue that can be fixed
        var settings = await settingsService.GetSettingsAsync(storeId);
        var addressValidation = settings != null 
            ? batchExecutionService.ValidateDestinationForExecution(settings) 
            : new Services.AddressValidationResult(false, "Settings not found");

        var model = new BatchDetailViewModel
        {
            Batch = batch,
            Allocations = batch.Allocations,
            CanRetry = batch.Status == BatchStatus.Failed,
            HasAddressIssue = batch.Status == BatchStatus.Failed && !addressValidation.IsValid,
            CurrentAddressError = !addressValidation.IsValid ? addressValidation.ErrorMessage : null,
            NetworkType = batchExecutionService.NetworkType.ToString()
        };

        return View(model);
    }

    [HttpPost("batches/{batchId}/retry")]
    public async Task<IActionResult> RetryBatch(string storeId, string batchId)
    {
        var batch = await batchExecutionService.GetBatchAsync(batchId);
        
        if (batch == null || batch.StoreId != storeId)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Batch not found.";
            return RedirectToAction(nameof(Batches), new { storeId });
        }

        if (batch.Status != BatchStatus.Failed)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Only failed batches can be retried.";
            return RedirectToAction(nameof(BatchDetail), new { storeId, batchId });
        }

        // Validate the address before allowing retry
        var settings = await settingsService.GetSettingsAsync(storeId);
        if (settings == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Stash settings not found. Please configure your settings first.";
            return RedirectToAction(nameof(Settings), new { storeId });
        }

        var addressValidation = batchExecutionService.ValidateDestinationForExecution(settings);
        if (!addressValidation.IsValid)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Cannot retry: {addressValidation.ErrorMessage} Please update your settings and try again.";
            return RedirectToAction(nameof(BatchDetail), new { storeId, batchId });
        }

        // Reset batch to pending and re-execute
        var resetSuccess = await batchExecutionService.ResetBatchForRetryAsync(batchId);
        if (!resetSuccess)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Failed to reset batch for retry.";
            return RedirectToAction(nameof(BatchDetail), new { storeId, batchId });
        }

        // Execute the batch
        var updatedBatch = await batchExecutionService.GetBatchAsync(batchId);
        if (updatedBatch == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Batch not found after reset.";
            return RedirectToAction(nameof(Batches), new { storeId });
        }

        BatchExecutionResult result;
        if (updatedBatch.ExecutionType == BatchExecutionType.ColdStorage)
        {
            result = await batchExecutionService.ExecuteColdStorageSweepAsync(updatedBatch, settings);
        }
        else
        {
            result = await batchExecutionService.ExecuteLiquidSwapAsync(updatedBatch, settings);
        }

        if (result.IsSuccess)
        {
            // Mark allocations linked to this batch as executed
            await allocationService.MarkBatchAllocationsExecutedAsync(batchId);
            TempData[WellKnownTempData.SuccessMessage] = "Batch retried successfully.";
        }
        else
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Batch retry failed: {result.ErrorMessage}";
        }

        return RedirectToAction(nameof(BatchDetail), new { storeId, batchId });
    }

    [HttpPost("reset")]
    public async Task<IActionResult> ResetPendingAllocations(string storeId)
    {
        var count = await allocationService.ResetPendingAllocationsAsync(storeId);
        
        TempData[WellKnownTempData.SuccessMessage] = 
            $"Emergency stop executed. {count} pending allocation(s) cleared.";
        
        logger.LogWarning("Emergency stop: Reset {Count} pending allocations for store {StoreId}", 
            count, storeId);

        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportCsv(string storeId, DateTimeOffset? from, DateTimeOffset? to, string type = "all")
    {
        var settings = await settingsService.GetSettingsAsync(storeId);
        var fiatCurrency = settings?.FiatCurrency ?? "USD";
        var sb = new StringBuilder();

        if (type == "batches")
        {
            // Export completed batches only (for tax/bookkeeping purposes)
            var batches = await batchExecutionService.GetBatchHistoryAsync(storeId, 1000, from, to);
            var completedBatches = batches.Where(b => b.Status == BatchStatus.Completed).ToList();

            // Header - Koinly compatible format with full details
            sb.AppendLine("Date,Type,Total Sats,Fee Sats,Net Sats,Sent BTC,Received Amount,Received Currency,Fee BTC,Fiat Value,Fiat Currency,Exchange Rate,Cost Basis,Allocations,Label,Description,TxHash,Destination");

            foreach (var batch in completedBatches)
            {
                var date = batch.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) 
                           ?? batch.InitiatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                var sentAmount = batch.NetSats / 100_000_000m;
                var feeAmount = batch.FeeSats / 100_000_000m;
                var netWorth = batch.FiatValueAtExecution;
                var batchType = batch.ExecutionType == BatchExecutionType.ColdStorage ? "Cold Storage" : "Liquid Swap";
                var destAddress = batch.DestinationAddress?.Replace(",", "") ?? "";

                if (batch.ExecutionType == BatchExecutionType.LiquidSwap)
                {
                    // Trade: BTC -> USDT (taxable event)
                    var receivedAmount = batch.UsdtReceived ?? batch.FiatValueAtExecution;
                    sb.AppendLine($"{date},{batchType},{batch.TotalSats},{batch.FeeSats},{batch.NetSats},{sentAmount:F8},{receivedAmount:F2},USDT,{feeAmount:F8},{netWorth:F2},{fiatCurrency},{batch.ExchangeRateAtExecution:F2},{batch.WeightedAverageCostBasis:F2},{batch.AllocationCount},trade,Stash swap to stablecoin,{batch.SwapId},{destAddress}");
                }
                else
                {
                    // Transfer: Internal to external (non-taxable transfer)
                    sb.AppendLine($"{date},{batchType},{batch.TotalSats},{batch.FeeSats},{batch.NetSats},{sentAmount:F8},{sentAmount:F8},BTC,{feeAmount:F8},{netWorth:F2},{fiatCurrency},{batch.ExchangeRateAtExecution:F2},{batch.WeightedAverageCostBasis:F2},{batch.AllocationCount},transfer,Stash cold storage sweep,{batch.TransactionId},{destAddress}");
                }
            }

            var fileName = $"stash-batches-{storeId}-{DateTime.UtcNow:yyyyMMdd}.csv";
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
        }
        else
        {
            // Default: Export all allocations (payments received) - comprehensive for bookkeeping
            var allocations = await allocationService.GetAllAllocationsAsync(storeId, from, to);
            
            // Comprehensive format with all allocation details
            sb.AppendLine("Date,Invoice ID,Payment Method,Total Received Sats,Allocated Sats,Allocated BTC,Total Received BTC,Fiat Value,Fiat Currency,Exchange Rate,Allocation %,Status,Batch ID");

            foreach (var alloc in allocations)
            {
                var date = alloc.SettledAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                var receivedBtc = alloc.TotalReceivedSats / 100_000_000m;
                var allocatedBtc = alloc.AllocatedSats / 100_000_000m;
                var fiatValue = alloc.FiatValueAtReceipt;
                var allocationPct = alloc.TotalReceivedSats > 0 
                    ? (alloc.AllocatedSats * 100m / alloc.TotalReceivedSats) 
                    : 0m;
                var status = alloc.IsExecuted ? "Executed" : "Pending";
                var batchId = alloc.ExecutedBatchId ?? "";
                var paymentMethod = alloc.PaymentMethod ?? "Unknown";
                
                sb.AppendLine($"{date},{alloc.InvoiceId},{paymentMethod},{alloc.TotalReceivedSats},{alloc.AllocatedSats},{allocatedBtc:F8},{receivedBtc:F8},{fiatValue:F2},{alloc.FiatCurrency},{alloc.ExchangeRateAtReceipt:F2},{allocationPct:F1},{status},{batchId}");
            }

            var allocFileName = $"stash-payments-{storeId}-{DateTime.UtcNow:yyyyMMdd}.csv";
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", allocFileName);
        }
    }

    [HttpGet("allocations")]
    public async Task<IActionResult> Allocations(string storeId, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (!await IsDatabaseReadyAsync())
        {
            return View("MigrationPending");
        }

        var allocations = await allocationService.GetAllAllocationsAsync(storeId, from, to);
        return View(allocations);
    }

    /// <summary>
    /// API endpoint for dashboard stats refresh (AJAX polling).
    /// </summary>
    [HttpGet("api/stats")]
    public async Task<IActionResult> GetStats(string storeId)
    {
        if (!await IsDatabaseReadyAsync())
        {
            return Json(new { error = "Database not ready" });
        }

        var settings = await settingsService.GetSettingsAsync(storeId);
        if (settings == null || !settings.IsEnabled)
        {
            return Json(new { enabled = false });
        }

        var (pendingSats, pendingFiat, pendingCount) = await allocationService.GetPendingTotalsAsync(storeId);
        var lifetimeStats = await batchExecutionService.GetLifetimeStatsAsync(storeId);
        var recentBatches = await batchExecutionService.GetBatchHistoryAsync(storeId, 5);

        // Get current exchange rate
        decimal currentRate = 0;
        var fiatCurrency = settings.FiatCurrency;
        try
        {
            var store = await storeRepository.FindStore(storeId);
            if (store != null)
            {
                var storeBlob = store.GetStoreBlob();
                var currencyPair = new CurrencyPair("BTC", fiatCurrency);
                var rateRules = storeBlob.GetRateRules(defaultRules);
                var rates = rateFetcher.FetchRates(
                    new[] { currencyPair }.ToHashSet(), 
                    rateRules, 
                    new StoreIdRateContext(storeId), 
                    default);
                
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
        catch
        {
            // Ignore rate fetch errors
        }

        var currentFiatValue = currentRate > 0
            ? (pendingSats / 100_000_000m) * currentRate
            : pendingFiat;

        var thresholdPercentage = settings.BatchThresholdFiat > 0 
            ? (currentFiatValue / settings.BatchThresholdFiat) * 100 
            : 0;

        return Json(new
        {
            enabled = true,
            pendingSats,
            pendingFiat,
            pendingCount,
            currentFiatValue,
            currentRate,
            fiatCurrency,
            thresholdPercentage,
            thresholdMet = thresholdPercentage >= 100,
            threshold = settings.BatchThresholdFiat,
            lifetimeStats = new
            {
                totalBatches = lifetimeStats.TotalBatches,
                totalSatsStashed = lifetimeStats.TotalSatsStashed,
                totalFiatStashed = lifetimeStats.TotalFiatStashed,
                totalFeesPaid = lifetimeStats.TotalFeesPaid
            },
            recentBatches = recentBatches.Select(b => new
            {
                id = b.Id,
                initiatedAt = b.InitiatedAt.ToString("o"),
                executionType = b.ExecutionType.ToString(),
                netSats = b.NetSats,
                status = b.Status.ToString()
            })
        });
    }

    private async Task<bool> IsDatabaseReadyAsync()
    {
        try
        {
            await using var db = dbContextFactory.CreateContext();
            // Try a simple query to see if the table exists
            await db.StashSettings.Take(1).ToListAsync();
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // relation does not exist
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Database readiness check failed");
            return false;
        }
    }
}
