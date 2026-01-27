#nullable enable
using System.Text.RegularExpressions;
using BTCPayServer.Plugins.Stash.Data.Models;
using BTCPayServer.Services;
using NBitcoin;

namespace BTCPayServer.Plugins.Stash.Services;

/// <summary>
/// Validates Bitcoin and Liquid addresses for the current network.
/// </summary>
public class AddressValidator : IAddressValidator
{
    private readonly ChainName _networkType;

    // Bitcoin address regex patterns
    private static readonly Regex BtcMainnetAddressRegex = new(
        @"^(bc1[a-zA-HJ-NP-Z0-9]{25,87}|[13][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        RegexOptions.Compiled);

    private static readonly Regex BtcTestnetAddressRegex = new(
        @"^(tb1[a-zA-HJ-NP-Z0-9]{25,87}|[2mn][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        RegexOptions.Compiled);

    private static readonly Regex BtcRegtestAddressRegex = new(
        @"^(bcrt1[a-zA-HJ-NP-Z0-9]{25,87}|[2mn][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        RegexOptions.Compiled);

    private static readonly Regex XpubRegex = new(
        @"^([xyztuvXYZTUV]pub[a-zA-HJ-NP-Z0-9]{100,120})$",
        RegexOptions.Compiled);

    // Liquid address regex patterns
    // Mainnet: ex1... (blech32), lq1... (blech32m), or confidential addresses starting with VJL, VTp, etc.
    private static readonly Regex LiquidMainnetAddressRegex = new(
        @"^(ex1[a-zA-HJ-NP-Z0-9]{25,120}|lq1[a-zA-HJ-NP-Z0-9]{25,120}|VJL[a-km-zA-HJ-NP-Z1-9]{76,100}|VTp[a-km-zA-HJ-NP-Z1-9]{76,100}|[GHVW][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        RegexOptions.Compiled);

    // Testnet/Regtest: tex1... (blech32), tlq1... (blech32m)
    private static readonly Regex LiquidTestnetAddressRegex = new(
        @"^(tex1[a-zA-HJ-NP-Z0-9]{25,120}|tlq1[a-zA-HJ-NP-Z0-9]{25,120}|ert1[a-zA-HJ-NP-Z0-9]{25,120}|el1[a-zA-HJ-NP-Z0-9]{25,120})$",
        RegexOptions.Compiled);

    /// <summary>
    /// Creates an AddressValidator for production use.
    /// </summary>
    public AddressValidator(BTCPayServerEnvironment environment)
    {
        _networkType = environment.NetworkType;
    }

    /// <summary>
    /// Creates an AddressValidator with an explicit network type.
    /// Useful for testing without BTCPayServerEnvironment dependency.
    /// </summary>
    public AddressValidator(ChainName networkType)
    {
        _networkType = networkType;
    }

    /// <inheritdoc/>
    public ChainName NetworkType => _networkType;

    /// <inheritdoc/>
    public AddressValidationResult ValidateBitcoinAddressForNetwork(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return new AddressValidationResult(false, "Address is required.");

        var expectedNetworkName = GetNetworkDisplayName(_networkType);

        // Check if it's an XPUB (valid on all networks)
        if (ValidateXpub(address))
            return new AddressValidationResult(true, null);

        // Determine which network the address belongs to
        var isMainnetAddress = BtcMainnetAddressRegex.IsMatch(address);
        var isTestnetAddress = BtcTestnetAddressRegex.IsMatch(address);
        var isRegtestAddress = BtcRegtestAddressRegex.IsMatch(address);

        // Validate against current network
        if (_networkType == ChainName.Mainnet)
        {
            if (isMainnetAddress)
                return new AddressValidationResult(true, null);
            if (isTestnetAddress || isRegtestAddress)
                return new AddressValidationResult(false,
                    $"This appears to be a testnet/regtest address, but you are running on {expectedNetworkName}. Please use a mainnet address (starting with bc1, 1, or 3).");
        }
        else if (_networkType == ChainName.Testnet)
        {
            if (isTestnetAddress)
                return new AddressValidationResult(true, null);
            if (isMainnetAddress)
                return new AddressValidationResult(false,
                    $"This appears to be a mainnet address, but you are running on {expectedNetworkName}. Please use a testnet address (starting with tb1, m, n, or 2).");
            if (isRegtestAddress)
                return new AddressValidationResult(false,
                    $"This appears to be a regtest address, but you are running on {expectedNetworkName}. Please use a testnet address (starting with tb1, m, n, or 2).");
        }
        else if (_networkType == ChainName.Regtest)
        {
            if (isRegtestAddress)
                return new AddressValidationResult(true, null);
            if (isMainnetAddress)
                return new AddressValidationResult(false,
                    $"This appears to be a mainnet address, but you are running on {expectedNetworkName}. Please use a regtest address (starting with bcrt1, m, n, or 2).");
            if (isTestnetAddress)
                return new AddressValidationResult(false,
                    $"This appears to be a testnet address, but you are running on {expectedNetworkName}. Please use a regtest address (starting with bcrt1, m, n, or 2).");
        }

        return new AddressValidationResult(false,
            $"Invalid Bitcoin address format. Please enter a valid {expectedNetworkName} address.");
    }

    /// <inheritdoc/>
    public bool ValidateBitcoinAddress(string address, bool isTestnet = false)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;

        if (isTestnet)
            return BtcTestnetAddressRegex.IsMatch(address) || BtcRegtestAddressRegex.IsMatch(address);

        return BtcMainnetAddressRegex.IsMatch(address);
    }

    /// <inheritdoc/>
    public bool ValidateXpub(string xpub)
    {
        if (string.IsNullOrWhiteSpace(xpub))
            return false;

        return XpubRegex.IsMatch(xpub);
    }

    /// <inheritdoc/>
    public AddressValidationResult ValidateLiquidAddressForNetwork(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return new AddressValidationResult(false, "Liquid address is required.");

        var isMainnetAddress = LiquidMainnetAddressRegex.IsMatch(address);
        var isTestnetAddress = LiquidTestnetAddressRegex.IsMatch(address);

        if (_networkType == ChainName.Mainnet)
        {
            if (isMainnetAddress)
                return new AddressValidationResult(true, null);
            if (isTestnetAddress)
                return new AddressValidationResult(false,
                    "This appears to be a testnet Liquid address, but you are running on mainnet. Please use a mainnet Liquid address (starting with ex1, lq1, or VJL/VTp).");
        }
        else // Testnet or Regtest
        {
            if (isTestnetAddress)
                return new AddressValidationResult(true, null);
            if (isMainnetAddress)
                return new AddressValidationResult(false,
                    "This appears to be a mainnet Liquid address, but you are running on testnet. Please use a testnet Liquid address (starting with tex1, tlq1, ert1, or el1).");
        }

        return new AddressValidationResult(false,
            "Invalid Liquid address format. Please enter a valid Liquid network address.");
    }

    /// <inheritdoc/>
    public bool ValidateLiquidAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;

        return LiquidMainnetAddressRegex.IsMatch(address) || LiquidTestnetAddressRegex.IsMatch(address);
    }

    /// <inheritdoc/>
    public AddressValidationResult ValidateDestinationForExecution(StashSettings settings)
    {
        if (settings.DestinationType == StashDestinationType.ColdStorage)
        {
            if (string.IsNullOrWhiteSpace(settings.DestinationAddress))
                return new AddressValidationResult(false, "No destination address configured. Please configure a Bitcoin address in your Stash settings.");

            return ValidateBitcoinAddressForNetwork(settings.DestinationAddress);
        }
        else if (settings.DestinationType == StashDestinationType.LiquidSwap)
        {
            if (string.IsNullOrWhiteSpace(settings.LiquidAddress))
                return new AddressValidationResult(false, "No Liquid address configured. Please configure a Liquid address in your Stash settings.");

            return ValidateLiquidAddressForNetwork(settings.LiquidAddress);
        }

        return new AddressValidationResult(true, null);
    }

    private static string GetNetworkDisplayName(ChainName network)
    {
        if (network == ChainName.Mainnet)
            return "mainnet";
        if (network == ChainName.Testnet)
            return "testnet";
        if (network == ChainName.Regtest)
            return "regtest";
        return network.ToString().ToLowerInvariant();
    }
}
