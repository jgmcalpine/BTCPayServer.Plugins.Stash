#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.Stash.Data.Models;

/// <summary>
/// Configuration settings for the Stash plugin per store.
/// </summary>
public class StashSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string StoreId { get; set; } = null!;

    /// <summary>
    /// Whether the plugin is enabled for this store.
    /// </summary>
    public bool IsEnabled { get; set; } = false;

    /// <summary>
    /// Percentage of each settled invoice to allocate (0-100).
    /// </summary>
    [Range(0, 100)]
    [Column(TypeName = "decimal(5,2)")]
    public decimal AllocationPercentage { get; set; } = 20.0m;

    /// <summary>
    /// Threshold in fiat currency (e.g., USD) that triggers batch execution.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal BatchThresholdFiat { get; set; } = 100.0m;

    /// <summary>
    /// The fiat currency for threshold calculations.
    /// </summary>
    [MaxLength(10)]
    public string FiatCurrency { get; set; } = "USD";

    /// <summary>
    /// Destination type: ColdStorage (on-chain sweep) or LiquidSwap (swap to stablecoin).
    /// </summary>
    public StashDestinationType DestinationType { get; set; } = StashDestinationType.ColdStorage;

    /// <summary>
    /// Destination address for cold storage sweeps (BTC address or XPUB).
    /// </summary>
    [MaxLength(500)]
    public string? DestinationAddress { get; set; }

    /// <summary>
    /// Destination Liquid address for USDT swaps.
    /// </summary>
    [MaxLength(500)]
    public string? LiquidAddress { get; set; }

    /// <summary>
    /// For XPUB destinations: the current derivation index.
    /// </summary>
    public int XpubDerivationIndex { get; set; } = 0;

    /// <summary>
    /// Minimum sats required for batch execution to be economically viable.
    /// </summary>
    public long MinimumBatchSats { get; set; } = 10000;

    /// <summary>
    /// Fee block target for on-chain transactions (1 = next block, 6 = ~1 hour, 144 = ~1 day, etc.).
    /// </summary>
    public int FeeBlockTarget { get; set; } = 144;

    /// <summary>
    /// Whether to route API requests through Tor for privacy.
    /// </summary>
    public bool UseTor { get; set; } = true;

    /// <summary>
    /// Record creation timestamp.
    /// </summary>
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
}

public enum StashDestinationType
{
    /// <summary>
    /// Sweep to an external on-chain Bitcoin address (non-taxable transfer).
    /// </summary>
    ColdStorage = 0,

    /// <summary>
    /// Swap Lightning BTC to Liquid USDT via Boltz (taxable disposal).
    /// </summary>
    LiquidSwap = 1
}

