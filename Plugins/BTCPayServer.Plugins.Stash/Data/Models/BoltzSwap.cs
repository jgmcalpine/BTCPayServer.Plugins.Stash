#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.Stash.Data.Models;

/// <summary>
/// Tracks a Boltz reverse swap (Lightning -> Liquid).
/// This model follows the swap lifecycle: Created -> Paid -> Settled
/// </summary>
public class BoltzSwap
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; } = null!;

    /// <summary>
    /// The store this swap belongs to.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string StoreId { get; set; } = null!;

    /// <summary>
    /// The batch this swap is executing for.
    /// </summary>
    [MaxLength(50)]
    public string? BatchId { get; set; }

    /// <summary>
    /// Boltz swap ID returned from the API.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string BoltzSwapId { get; set; } = null!;

    /// <summary>
    /// Current status of the swap.
    /// </summary>
    public BoltzSwapState State { get; set; } = BoltzSwapState.Created;

    /// <summary>
    /// Detailed status from Boltz API (e.g., "invoice.paid", "transaction.claimed").
    /// </summary>
    [MaxLength(100)]
    public string? BoltzStatus { get; set; }

    /// <summary>
    /// Lightning invoice to pay.
    /// </summary>
    [MaxLength(2000)]
    public string? Invoice { get; set; }

    /// <summary>
    /// Amount in satoshis being sent (Lightning side).
    /// </summary>
    public long InvoiceAmountSats { get; set; }

    /// <summary>
    /// Amount in satoshis being received (Liquid side, after fees).
    /// </summary>
    public long OnchainAmountSats { get; set; }

    /// <summary>
    /// Miner fee in satoshis.
    /// </summary>
    public long MinerFeeSats { get; set; }

    /// <summary>
    /// Service fee in satoshis.
    /// </summary>
    public long ServiceFeeSats { get; set; }

    /// <summary>
    /// Destination Liquid address.
    /// </summary>
    [MaxLength(500)]
    public string? DestinationAddress { get; set; }

    /// <summary>
    /// Liquid lockup address (where Boltz locks funds).
    /// </summary>
    [MaxLength(500)]
    public string? LockupAddress { get; set; }

    /// <summary>
    /// Lightning payment preimage (proof of payment).
    /// </summary>
    [MaxLength(200)]
    public string? Preimage { get; set; }

    /// <summary>
    /// Liquid transaction ID when funds are claimed.
    /// </summary>
    [MaxLength(100)]
    public string? ClaimTransactionId { get; set; }

    /// <summary>
    /// Refund transaction ID if swap was refunded.
    /// </summary>
    [MaxLength(100)]
    public string? RefundTransactionId { get; set; }

    /// <summary>
    /// Error message if swap failed.
    /// </summary>
    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether the error is retryable.
    /// </summary>
    public bool IsRetryable { get; set; } = true;

    /// <summary>
    /// Timeout block height for the swap.
    /// </summary>
    public long TimeoutBlockHeight { get; set; }

    /// <summary>
    /// Blinding key for Liquid confidential transactions.
    /// </summary>
    [MaxLength(200)]
    public string? BlindingKey { get; set; }

    /// <summary>
    /// Claim public key used for the swap.
    /// </summary>
    [MaxLength(200)]
    public string? ClaimPublicKey { get; set; }

    /// <summary>
    /// Swap tree data (JSON serialized).
    /// </summary>
    public string? SwapTreeJson { get; set; }

    /// <summary>
    /// When the swap was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the Lightning invoice was paid.
    /// </summary>
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>
    /// When the swap was completed (claimed on Liquid).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Last time the swap status was updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation property
    public ExecutedBatch? Batch { get; set; }
}

/// <summary>
/// Simplified swap states for internal tracking.
/// </summary>
public enum BoltzSwapState
{
    /// <summary>
    /// Swap created, waiting for Lightning payment.
    /// </summary>
    Created = 0,

    /// <summary>
    /// Lightning invoice has been paid.
    /// </summary>
    Paid = 1,

    /// <summary>
    /// Boltz has broadcast the Liquid transaction.
    /// </summary>
    Processing = 2,

    /// <summary>
    /// Swap completed successfully - funds claimed on Liquid.
    /// </summary>
    Completed = 3,

    /// <summary>
    /// Swap failed or expired.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// Swap was refunded.
    /// </summary>
    Refunded = 5
}

