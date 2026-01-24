using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Rating;
using BTCPayServer.RockstarDev.Plugins.Stash.Data;
using BTCPayServer.RockstarDev.Plugins.Stash.Data.Models;
using BTCPayServer.RockstarDev.Plugins.Stash.Services;
using BTCPayServer.RockstarDev.Plugins.Stash.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BTCPayServer.RockstarDev.Plugins.Stash.Controllers;

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

        // Validate destination address based on type
        if (model.IsEnabled && model.DestinationType == StashDestinationType.ColdStorage)
        {
            if (string.IsNullOrWhiteSpace(model.DestinationAddress))
            {
                ModelState.AddModelError(nameof(model.DestinationAddress), 
                    "Destination address is required for cold storage mode.");
                return View(model);
            }

            // Basic address validation
            var isTestnet = model.DestinationAddress.StartsWith("tb1") || 
                           model.DestinationAddress.StartsWith("bcrt1") ||
                           model.DestinationAddress.StartsWith("2") ||
                           model.DestinationAddress.StartsWith("m") ||
                           model.DestinationAddress.StartsWith("n");
            
            if (!batchExecutionService.ValidateBitcoinAddress(model.DestinationAddress, isTestnet) &&
                !batchExecutionService.ValidateXpub(model.DestinationAddress))
            {
                ModelState.AddModelError(nameof(model.DestinationAddress), 
                    "Invalid Bitcoin address or XPUB format.");
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

        var model = new BatchDetailViewModel
        {
            Batch = batch,
            Allocations = batch.Allocations
        };

        return View(model);
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
    public async Task<IActionResult> ExportCsv(string storeId, DateTimeOffset? from, DateTimeOffset? to)
    {
        var settings = await settingsService.GetSettingsAsync(storeId);
        var batches = await batchExecutionService.GetBatchHistoryAsync(storeId, 1000, from, to);

        var fiatCurrency = settings?.FiatCurrency ?? "USD";
        var sb = new StringBuilder();

        // Header - Koinly compatible format
        sb.AppendLine("Date,Sent Amount,Sent Currency,Received Amount,Received Currency,Fee Amount,Fee Currency,Net Worth Amount,Net Worth Currency,Label,Description,TxHash");

        foreach (var batch in batches.Where(b => b.Status == BatchStatus.Completed))
        {
            var date = batch.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) 
                       ?? batch.InitiatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var sentAmount = batch.NetSats / 100_000_000m;
            var feeAmount = batch.FeeSats / 100_000_000m;
            var netWorth = batch.FiatValueAtExecution;

            if (batch.ExecutionType == BatchExecutionType.LiquidSwap)
            {
                // Trade: BTC -> USDT
                var receivedAmount = batch.UsdtReceived ?? batch.FiatValueAtExecution;
                sb.AppendLine($"{date},{sentAmount:F8},BTC,{receivedAmount:F2},USDT,{feeAmount:F8},BTC,{netWorth:F2},{fiatCurrency},trade,Stash swap to stablecoin,{batch.SwapId}");
            }
            else
            {
                // Transfer: Internal to external (non-taxable)
                sb.AppendLine($"{date},{sentAmount:F8},BTC,{sentAmount:F8},BTC,{feeAmount:F8},BTC,{netWorth:F2},{fiatCurrency},transfer,Stash cold storage sweep,{batch.TransactionId}");
            }
        }

        var fileName = $"stash-export-{storeId}-{DateTime.UtcNow:yyyyMMdd}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
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
