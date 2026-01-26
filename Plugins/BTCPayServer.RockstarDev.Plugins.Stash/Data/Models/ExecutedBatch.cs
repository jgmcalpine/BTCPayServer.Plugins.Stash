#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.RockstarDev.Plugins.Stash.Data.Models;

/// <summary>
/// Represents an executed batch withdrawal/swap.
/// This is the permanent record of fund disposition.
/// </summary>
public class ExecutedBatch
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string StoreId { get; set; } = null!;

    /// <summary>
    /// Type of execution: ColdStorage or LiquidSwap.
    /// </summary>
    public BatchExecutionType ExecutionType { get; set; }

    /// <summary>
    /// Execution status.
    /// </summary>
    public BatchStatus Status { get; set; } = BatchStatus.Pending;

    /// <summary>
    /// Total satoshis in this batch before fees.
    /// </summary>
    public long TotalSats { get; set; }

    /// <summary>
    /// Network/transaction fees paid.
    /// </summary>
    public long FeeSats { get; set; }

    /// <summary>
    /// Net satoshis sent after fees.
    /// </summary>
    public long NetSats { get; set; }

    /// <summary>
    /// The fiat value of the batch at execution time.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal FiatValueAtExecution { get; set; }

    /// <summary>
    /// The fiat currency.
    /// </summary>
    [MaxLength(10)]
    public string FiatCurrency { get; set; } = "USD";

    /// <summary>
    /// Exchange rate at execution time.
    /// </summary>
    [Column(TypeName = "decimal(18,8)")]
    public decimal ExchangeRateAtExecution { get; set; }

    /// <summary>
    /// Weighted average cost basis from source allocations.
    /// </summary>
    [Column(TypeName = "decimal(18,8)")]
    public decimal WeightedAverageCostBasis { get; set; }

    /// <summary>
    /// Destination address used.
    /// </summary>
    [MaxLength(500)]
    public string? DestinationAddress { get; set; }

    /// <summary>
    /// Transaction ID for on-chain sweeps.
    /// </summary>
    [MaxLength(100)]
    public string? TransactionId { get; set; }

    /// <summary>
    /// Swap ID for Liquid swaps (Boltz).
    /// </summary>
    [MaxLength(100)]
    public string? SwapId { get; set; }

    /// <summary>
    /// For Liquid swaps: amount of USDT received.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? UsdtReceived { get; set; }

    /// <summary>
    /// Any error message if execution failed.
    /// </summary>
    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of allocations included in this batch.
    /// </summary>
    public int AllocationCount { get; set; }

    /// <summary>
    /// Timestamp when batch was initiated.
    /// </summary>
    public DateTimeOffset InitiatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp when batch was completed (or failed).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Record creation timestamp.
    /// </summary>
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    // Navigation property
    public List<PendingAllocation> Allocations { get; set; } = new();
}

public enum BatchExecutionType
{
    /// <summary>
    /// On-chain sweep to cold storage (non-taxable transfer).
    /// </summary>
    ColdStorage = 0,

    /// <summary>
    /// Swap to Liquid USDT via Boltz (taxable disposal).
    /// </summary>
    LiquidSwap = 1
}

public enum BatchStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

