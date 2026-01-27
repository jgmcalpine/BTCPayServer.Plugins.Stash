#nullable enable
using BTCPayServer.Plugins.Stash.Data.Models;
using NBitcoin;

namespace BTCPayServer.Plugins.Stash.Services;

/// <summary>
/// Interface for validating Bitcoin and Liquid addresses.
/// Enables testing by allowing mock implementations.
/// </summary>
public interface IAddressValidator
{
    /// <summary>
    /// Gets the current network type.
    /// </summary>
    ChainName NetworkType { get; }

    /// <summary>
    /// Validates a Bitcoin address based on the current network environment.
    /// </summary>
    AddressValidationResult ValidateBitcoinAddressForNetwork(string address);

    /// <summary>
    /// Validates a Bitcoin address (legacy method for backwards compatibility).
    /// </summary>
    bool ValidateBitcoinAddress(string address, bool isTestnet = false);

    /// <summary>
    /// Validates an XPUB.
    /// </summary>
    bool ValidateXpub(string xpub);

    /// <summary>
    /// Validates a Liquid address based on the current network environment.
    /// </summary>
    AddressValidationResult ValidateLiquidAddressForNetwork(string address);

    /// <summary>
    /// Validates a Liquid address (legacy method for backwards compatibility).
    /// </summary>
    bool ValidateLiquidAddress(string address);

    /// <summary>
    /// Validates destination address before batch execution.
    /// </summary>
    AddressValidationResult ValidateDestinationForExecution(StashSettings settings);
}
