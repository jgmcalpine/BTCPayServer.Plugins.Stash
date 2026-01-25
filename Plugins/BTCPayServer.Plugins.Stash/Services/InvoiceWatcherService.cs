using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Stash.Services;

/// <summary>
/// Listens for InvoiceSettled events and creates virtual allocations.
/// </summary>
public class InvoiceWatcherService : IHostedService, IDisposable
{
    private readonly EventAggregator _eventAggregator;
    private readonly StashSettingsService _settingsService;
    private readonly AllocationService _allocationService;
    private readonly RateFetcher _rateFetcher;
    private readonly DefaultRulesCollection _defaultRules;
    private readonly StoreRepository _storeRepository;
    private readonly ILogger<InvoiceWatcherService> _logger;
    private IEventAggregatorSubscription _subscription;

    public InvoiceWatcherService(
        EventAggregator eventAggregator,
        StashSettingsService settingsService,
        AllocationService allocationService,
        RateFetcher rateFetcher,
        DefaultRulesCollection defaultRules,
        StoreRepository storeRepository,
        ILogger<InvoiceWatcherService> logger)
    {
        _eventAggregator = eventAggregator;
        _settingsService = settingsService;
        _allocationService = allocationService;
        _rateFetcher = rateFetcher;
        _defaultRules = defaultRules;
        _storeRepository = storeRepository;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = _eventAggregator.SubscribeAsync<InvoiceEvent>(HandleInvoiceEvent);
        _logger.LogInformation("Stash InvoiceWatcherService started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _logger.LogInformation("Stash InvoiceWatcherService stopped");
        return Task.CompletedTask;
    }

    private async Task HandleInvoiceEvent(InvoiceEvent evt)
    {
        try
        {
            // We're interested in settled invoices
            if (evt.EventCode != InvoiceEventCode.Completed && 
                evt.EventCode != InvoiceEventCode.MarkedCompleted)
            {
                return;
            }

            var invoice = evt.Invoice;
            var storeId = invoice.StoreId;

            // Check if Stash is enabled for this store
            var settings = await _settingsService.GetSettingsAsync(storeId);
            if (settings == null || !settings.IsEnabled)
            {
                _logger.LogDebug("Stash not enabled for store {StoreId}", storeId);
                return;
            }

            // Get the total payment in sats
            var payments = invoice.GetPayments(true); // Only settled payments
            if (!payments.Any())
            {
                _logger.LogDebug("No payments found for invoice {InvoiceId}", invoice.Id);
                return;
            }

            // Sum up all payments - Value is already in the payment currency (e.g., BTC)
            decimal totalBtc = 0m;
            foreach (var payment in payments)
            {
                // Get BTC payments only for now
                if (payment.Currency == "BTC" || payment.Currency == "SATS")
                {
                    totalBtc += payment.Value;
                }
            }

            var totalSatsLong = (long)(totalBtc * 100_000_000m);

            if (totalSatsLong <= 0)
            {
                _logger.LogDebug("Zero or negative payment value for invoice {InvoiceId}", invoice.Id);
                return;
            }

            // Get the payment method (for logging)
            var primaryPayment = payments.FirstOrDefault();
            var paymentMethod = primaryPayment?.PaymentMethodId?.ToString();

            // Get current exchange rate
            decimal exchangeRate = 0;
            try
            {
                var store = await _storeRepository.FindStore(storeId);
                if (store != null)
                {
                    var storeBlob = store.GetStoreBlob();
                    var currencyPair = new CurrencyPair("BTC", settings.FiatCurrency);
                    var rateRules = storeBlob.GetRateRules(_defaultRules);
                    var rates = _rateFetcher.FetchRates(
                        new[] { currencyPair }.ToHashSet(), 
                        rateRules, 
                        new StoreIdRateContext(storeId), 
                        default);
                    
                    if (rates.TryGetValue(currencyPair, out var rateTask))
                    {
                        var rate = await rateTask;
                        if (rate.BidAsk != null)
                        {
                            exchangeRate = rate.BidAsk.Bid;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch exchange rate for {Currency}", settings.FiatCurrency);
                // Continue without exchange rate - we'll estimate later
            }

            // Create the allocation
            await _allocationService.CreateAllocationAsync(
                storeId,
                invoice.Id,
                paymentMethod,
                totalSatsLong,
                settings.AllocationPercentage,
                exchangeRate,
                settings.FiatCurrency);

            _logger.LogInformation(
                "Processed invoice {InvoiceId} for Stash: {TotalSats} sats received, {Percentage}% allocated",
                invoice.Id, totalSatsLong, settings.AllocationPercentage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing invoice event for Stash");
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
