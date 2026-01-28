#nullable enable
using System;
using System.Text.RegularExpressions;
using BTCPayServer.Plugins.Stash.Data.Models;
using BTCPayServer.Services;
using NBitcoin;
using BTCPayServer;

namespace BTCPayServer.Plugins.Stash.Services;

/// <summary>
/// Validates Bitcoin and Liquid addresses for the current network.
/// </summary>
public class AddressValidator : IAddressValidator
{
    private readonly ChainName _networkType;
    private readonly BTCPayNetworkProvider _networkProvider;

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
    public AddressValidator(BTCPayServerEnvironment environment, BTCPayNetworkProvider networkProvider)
    {
        _networkType = environment.NetworkType;
        _networkProvider = networkProvider;
    }

    /// <summary>
    /// Creates an AddressValidator with an explicit network type.
    /// Useful for testing without BTCPayServerEnvironment dependency.
    /// </summary>
    public AddressValidator(ChainName networkType, BTCPayNetworkProvider? networkProvider = null)
    {
        _networkType = networkType;
        _networkProvider = networkProvider ?? throw new ArgumentNullException(nameof(networkProvider), "Network provider is required for address validation.");
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

        // Get the Bitcoin network for the current environment
        var btcNetwork = _networkProvider.GetNetwork<BTCPayNetwork>("BTC");
        if (btcNetwork == null)
        {
            return new AddressValidationResult(false, "Bitcoin network not available.");
        }

        var nbitcoinNetwork = btcNetwork.NBitcoinNetwork;

        // Try to parse the address with NBitcoin to validate checksum and format
        // First, try parsing with the expected network
        BitcoinAddress? parsedAddress = null;
        try
        {
            parsedAddress = BitcoinAddress.Create(address, nbitcoinNetwork);
            // If parsing succeeds, the address is valid for this network
            return new AddressValidationResult(true, null);
        }
        catch (FormatException)
        {
            // Address format is invalid or checksum is wrong
        }

        // If parsing failed, try to determine which network the address appears to belong to
        // This helps provide a better error message
        var isMainnetAddress = BtcMainnetAddressRegex.IsMatch(address);
        var isTestnetAddress = BtcTestnetAddressRegex.IsMatch(address);
        var isRegtestAddress = BtcRegtestAddressRegex.IsMatch(address);

        // Try parsing with other networks to see if it's a network mismatch
        if (_networkType != ChainName.Mainnet && isMainnetAddress)
        {
            try
            {
                BitcoinAddress.Create(address, Network.Main);
                return new AddressValidationResult(false,
                    $"This appears to be a mainnet address, but you are running on {expectedNetworkName}. Please use a {expectedNetworkName} address (starting with {GetAddressPrefixesForNetwork(_networkType)}).");
            }
            catch
            {
                // Invalid even for mainnet
            }
        }
        else if (_networkType != ChainName.Testnet && isTestnetAddress)
        {
            try
            {
                BitcoinAddress.Create(address, Network.TestNet);
                return new AddressValidationResult(false,
                    $"This appears to be a testnet address, but you are running on {expectedNetworkName}. Please use a {expectedNetworkName} address (starting with {GetAddressPrefixesForNetwork(_networkType)}).");
            }
            catch
            {
                // Invalid even for testnet
            }
        }
        else if (_networkType != ChainName.Regtest && isRegtestAddress)
        {
            try
            {
                BitcoinAddress.Create(address, Network.RegTest);
                return new AddressValidationResult(false,
                    $"This appears to be a regtest address, but you are running on {expectedNetworkName}. Please use a {expectedNetworkName} address (starting with {GetAddressPrefixesForNetwork(_networkType)}).");
            }
            catch
            {
                // Invalid even for regtest
            }
        }

        // Address format looks correct but checksum is invalid or address is malformed
        return new AddressValidationResult(false,
            $"Invalid Bitcoin address. The address format appears correct but the checksum is invalid or the address is malformed. Please enter a valid {expectedNetworkName} address.");
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

    private static string GetAddressPrefixesForNetwork(ChainName network)
    {
        if (network == ChainName.Mainnet)
            return "bc1, 1, or 3";
        if (network == ChainName.Testnet)
            return "tb1, m, n, or 2";
        if (network == ChainName.Regtest)
            return "bcrt1, m, n, or 2";
        return "a valid address";
    }
}
