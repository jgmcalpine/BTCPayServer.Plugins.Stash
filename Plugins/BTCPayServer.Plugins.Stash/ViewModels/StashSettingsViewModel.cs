#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Plugins.Stash.Data.Models;

namespace BTCPayServer.Plugins.Stash.ViewModels;

public class StashSettingsViewModel
{
    public string? Id { get; set; }

    [Display(Name = "Enable Stash")]
    public bool IsEnabled { get; set; }

    [Display(Name = "Allocation Percentage")]
    [Range(0, 100, ErrorMessage = "Percentage must be between 0 and 100")]
    public decimal AllocationPercentage { get; set; } = 20.0m;

    [Display(Name = "Batch Threshold")]
    [Range(1, 1000000, ErrorMessage = "Threshold must be at least 1")]
    public decimal BatchThresholdFiat { get; set; } = 100.0m;

    [Display(Name = "Fiat Currency")]
    [Required(ErrorMessage = "Currency is required")]
    public string FiatCurrency { get; set; } = "USD";

    [Display(Name = "Destination Type")]
    public StashDestinationType DestinationType { get; set; } = StashDestinationType.ColdStorage;

    [Display(Name = "Bitcoin Destination Address")]
    [StringLength(500)]
    public string? DestinationAddress { get; set; }

    [Display(Name = "Liquid Destination Address")]
    [StringLength(500)]
    public string? LiquidAddress { get; set; }

    [Display(Name = "Minimum Batch Size (sats)")]
    [Range(1000, 100000000, ErrorMessage = "Minimum must be at least 1000 sats")]
    public long MinimumBatchSats { get; set; } = 10000;

    [Display(Name = "Fee Block Target")]
    [Range(1, 144, ErrorMessage = "Block target must be between 1 and 144")]
    public int FeeBlockTarget { get; set; } = 144;

    [Display(Name = "Use Tor for API calls")]
    public bool UseTor { get; set; } = true;

    /// <summary>
    /// Supported fiat currencies for threshold calculations.
    /// </summary>
    public static IReadOnlyList<string> SupportedCurrencies => new[] { "USD" };

    public static StashSettingsViewModel FromModel(StashSettings settings)
    {
        return new StashSettingsViewModel
        {
            Id = settings.Id,
            IsEnabled = settings.IsEnabled,
            AllocationPercentage = settings.AllocationPercentage,
            BatchThresholdFiat = settings.BatchThresholdFiat,
            FiatCurrency = settings.FiatCurrency,
            DestinationType = settings.DestinationType,
            DestinationAddress = settings.DestinationAddress,
            LiquidAddress = settings.LiquidAddress,
            MinimumBatchSats = settings.MinimumBatchSats,
            FeeBlockTarget = settings.FeeBlockTarget,
            UseTor = settings.UseTor
        };
    }

    public StashSettings ToModel(string storeId)
    {
        return new StashSettings
        {
            Id = Id ?? string.Empty,
            StoreId = storeId,
            IsEnabled = IsEnabled,
            AllocationPercentage = AllocationPercentage,
            BatchThresholdFiat = BatchThresholdFiat,
            FiatCurrency = FiatCurrency,
            DestinationType = DestinationType,
            DestinationAddress = DestinationAddress,
            LiquidAddress = LiquidAddress,
            MinimumBatchSats = MinimumBatchSats,
            FeeBlockTarget = FeeBlockTarget,
            UseTor = UseTor
        };
    }
}

