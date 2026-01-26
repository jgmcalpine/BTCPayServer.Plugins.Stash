#nullable enable
using System.Collections.Generic;
using BTCPayServer.RockstarDev.Plugins.Stash.Data.Models;
using BTCPayServer.RockstarDev.Plugins.Stash.Services;

namespace BTCPayServer.RockstarDev.Plugins.Stash.ViewModels;

public class StashDashboardViewModel
{
    public StashSettings? Settings { get; set; }
    
    public bool IsConfigured => Settings != null;
    public bool IsEnabled => Settings?.IsEnabled ?? false;

    // Pending allocation stats
    public long PendingSats { get; set; }
    public decimal PendingFiat { get; set; }
    public int PendingCount { get; set; }
    public decimal CurrentExchangeRate { get; set; }
    public decimal CurrentFiatValue { get; set; }

    // Threshold progress
    public decimal ThresholdPercentage => Settings?.BatchThresholdFiat > 0 
        ? (CurrentFiatValue / Settings.BatchThresholdFiat) * 100 
        : 0;
    public bool ThresholdMet => ThresholdPercentage >= 100;

    // Lifetime stats
    public StashLifetimeStats? LifetimeStats { get; set; }

    // Recent batches
    public List<ExecutedBatch> RecentBatches { get; set; } = new();

    // Recent allocations
    public List<PendingAllocation> RecentAllocations { get; set; } = new();
}

