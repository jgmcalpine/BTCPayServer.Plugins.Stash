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

        // Add services
        serviceCollection.AddSingleton<StashSettingsService>();
        serviceCollection.AddSingleton<AllocationService>();
        serviceCollection.AddSingleton<BatchExecutionService>();

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

