#nullable enable
using System;
using System.Collections.Generic;
using BTCPayServer.Plugins.Stash.Data.Models;

namespace BTCPayServer.Plugins.Stash.ViewModels;

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
    
    /// <summary>
    /// Whether this batch can be retried (only failed batches).
    /// </summary>
    public bool CanRetry { get; set; }
    
    /// <summary>
    /// Whether the current configured address has an issue that caused the failure.
    /// </summary>
    public bool HasAddressIssue { get; set; }
    
    /// <summary>
    /// Current address validation error message, if any.
    /// </summary>
    public string? CurrentAddressError { get; set; }
    
    /// <summary>
    /// The current network type for display.
    /// </summary>
    public string? NetworkType { get; set; }
}

