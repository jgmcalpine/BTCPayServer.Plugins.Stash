#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.Stash.Data.Models;

/// <summary>
/// Represents a virtual allocation of funds from a settled invoice.
/// Funds are not moved on-chain; this is a logical/accounting record.
/// </summary>
public class PendingAllocation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string StoreId { get; set; } = null!;

    /// <summary>
    /// Reference to the BTCPay invoice that triggered this allocation.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string InvoiceId { get; set; } = null!;

    /// <summary>
    /// The payment method used (e.g., "BTC-LN", "BTC").
    /// </summary>
    [MaxLength(50)]
    public string? PaymentMethod { get; set; }

    /// <summary>
    /// The total amount received in sats from the invoice payment.
    /// </summary>
    public long TotalReceivedSats { get; set; }

    /// <summary>
    /// The allocated portion in sats (based on allocation percentage).
    /// </summary>
    public long AllocatedSats { get; set; }

    /// <summary>
    /// The fiat value at time of receipt (for cost basis tracking).
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal FiatValueAtReceipt { get; set; }

    /// <summary>
    /// The fiat currency used for valuation.
    /// </summary>
    [MaxLength(10)]
    public string FiatCurrency { get; set; } = "USD";

    /// <summary>
    /// BTC/Fiat exchange rate at time of receipt.
    /// </summary>
    [Column(TypeName = "decimal(18,8)")]
    public decimal ExchangeRateAtReceipt { get; set; }

    /// <summary>
    /// Whether this allocation has been included in an executed batch.
    /// </summary>
    public bool IsExecuted { get; set; } = false;

    /// <summary>
    /// Reference to the batch that executed this allocation (null if pending).
    /// </summary>
    [MaxLength(50)]
    public string? ExecutedBatchId { get; set; }

    /// <summary>
    /// Timestamp when the invoice was settled.
    /// </summary>
    public DateTimeOffset SettledAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp when allocation was recorded.
    /// </summary>
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    // Navigation property
    public ExecutedBatch? ExecutedBatch { get; set; }
}

