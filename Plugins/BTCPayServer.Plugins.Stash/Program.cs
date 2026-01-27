using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Stash.Data;
using BTCPayServer.Plugins.Stash.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Stash;

public class StashPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(StashPlugin) + "Nav";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.3.0" }
    ];

    public override void Execute(IServiceCollection serviceCollection)
    {
        // Add UI extension for store integrations menu
        serviceCollection.AddUIExtension("store-integrations-nav", PluginNavKey);

        // Add the database related registrations
        serviceCollection.AddSingleton<PluginDbContextFactory>();
        serviceCollection.AddDbContext<PluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<PluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        serviceCollection.AddHostedService<PluginMigrationRunner>();

        // Configure Mock Boltz server for regtest testing
        // Enable via: STASH_MOCK_BOLTZ=true
        var mockBoltzEnabled = string.Equals(
            Environment.GetEnvironmentVariable("STASH_MOCK_BOLTZ"), 
            "true", 
            StringComparison.OrdinalIgnoreCase);
        
        var mockBoltzOptions = new MockBoltzOptions
        {
            Enabled = mockBoltzEnabled,
            Port = int.TryParse(Environment.GetEnvironmentVariable("STASH_MOCK_BOLTZ_PORT"), out var port) 
                ? port 
                : 9999,
            SwapProgressDelayMs = int.TryParse(Environment.GetEnvironmentVariable("STASH_MOCK_BOLTZ_DELAY"), out var delay) 
                ? delay 
                : 2000,
            SimulateFailure = string.Equals(
                Environment.GetEnvironmentVariable("STASH_MOCK_BOLTZ_FAIL"), 
                "true", 
                StringComparison.OrdinalIgnoreCase)
        };
        serviceCollection.AddSingleton(mockBoltzOptions);

        // Add HTTP client for Boltz API
        serviceCollection.AddHttpClient("Boltz", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // Add services
        serviceCollection.AddSingleton<StashSettingsService>();
        serviceCollection.AddSingleton<AllocationService>();
        serviceCollection.AddSingleton<IAddressValidator, AddressValidator>();
        serviceCollection.AddSingleton<BatchExecutionService>();
        serviceCollection.AddSingleton<IBoltzApiService, BoltzApiService>();

        // Add mock Boltz server (only runs if enabled)
        if (mockBoltzEnabled)
        {
            serviceCollection.AddHostedService<MockBoltzServer>();
        }

        // Invoice watcher (listens for settled invoices)
        serviceCollection.AddSingleton<InvoiceWatcherService>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<InvoiceWatcherService>());

        // Threshold monitor (periodic check for batch execution)
        var intervalSeconds = int.TryParse(
            Environment.GetEnvironmentVariable("BTCPAY_STASH_INTERVAL"), 
            out var seconds) && seconds > 0 ? seconds : 60;
        serviceCollection.AddScheduledTask<ThresholdMonitorService>(TimeSpan.FromSeconds(intervalSeconds));

        base.Execute(serviceCollection);
    }
}

