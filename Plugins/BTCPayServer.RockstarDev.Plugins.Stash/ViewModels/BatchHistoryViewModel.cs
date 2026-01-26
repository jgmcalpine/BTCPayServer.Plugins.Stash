using System;
using System.Collections.Generic;
using BTCPayServer.RockstarDev.Plugins.Stash.Data.Models;

namespace BTCPayServer.RockstarDev.Plugins.Stash.ViewModels;

public class BatchHistoryViewModel
{
    public List<ExecutedBatch> Batches { get; set; } = new();
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public string FiatCurrency { get; set; } = "USD";
}

public class BatchDetailViewModel
{
    public ExecutedBatch Batch { get; set; } = null!;
    public List<PendingAllocation> Allocations { get; set; } = new();
}

