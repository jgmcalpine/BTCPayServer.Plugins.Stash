#nullable enable
using System.Net.Http;
using BTCPayServer.Plugins.Stash.Data.Models;
using BTCPayServer.Plugins.Stash.Services;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using RichardSzalay.MockHttp;

namespace BTCPayServer.Plugins.Tests.StashPluginTests;

/// <summary>
/// Test helpers for creating testable service instances.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Creates a BoltzApiService for testing with a mocked HTTP handler.
    /// Uses a wrapper to avoid mocking non-virtual BTCPayServerEnvironment members.
    /// </summary>
    public static TestableBoltzApiService CreateBoltzApiService(
        MockHttpMessageHandler mockHttp,
        ChainName? network = null)
    {
        var effectiveNetwork = network ?? ChainName.Testnet;
        return new TestableBoltzApiService(mockHttp.ToHttpClient(), effectiveNetwork);
    }

    /// <summary>
    /// Creates a testable address validator with the specified network.
    /// </summary>
    public static TestableAddressValidator CreateAddressValidator(ChainName network)
    {
        return new TestableAddressValidator(network);
    }
}

/// <summary>
/// Testable version of BoltzApiService that doesn't require BTCPayServerEnvironment.
/// </summary>
public class TestableBoltzApiService
{
    private readonly HttpClient _httpClient;
    private readonly ChainName _network;
    private readonly ILogger<BoltzApiService> _logger;

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public TestableBoltzApiService(HttpClient httpClient, ChainName network)
    {
        _httpClient = httpClient;
        _network = network;
        _logger = Mock.Of<ILogger<BoltzApiService>>();
    }

    public ChainName NetworkType => _network;

    private string GetBaseUrl()
    {
        if (_network == ChainName.Mainnet)
            return "https://api.boltz.exchange";
        return "https://api.testnet.boltz.exchange";
    }

    public async Task<BoltzPairsResponse?> GetPairsAsync(CancellationToken cancellationToken = default)
    {
        var url = $"{GetBaseUrl()}/v2/pairs";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return System.Text.Json.JsonSerializer.Deserialize<BoltzPairsResponse>(content, JsonOptions);
    }

    public async Task<BoltzReverseQuoteResponse> GetReverseQuoteAsync(
        long invoiceAmountSats,
        CancellationToken cancellationToken = default)
    {
        var url = $"{GetBaseUrl()}/v2/swap/reverse/quote?from=BTC&to=L-BTC&invoiceAmount={invoiceAmountSats}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var error = System.Text.Json.JsonSerializer.Deserialize<BoltzErrorResponse>(content, JsonOptions);
                throw new BoltzApiException(error?.Error ?? content, response.StatusCode);
            }
            catch (System.Text.Json.JsonException)
            {
                throw new BoltzApiException(content, response.StatusCode);
            }
        }
        
        var result = System.Text.Json.JsonSerializer.Deserialize<BoltzReverseQuoteResponse>(content, JsonOptions);
        return result ?? throw new BoltzApiException("Empty response", System.Net.HttpStatusCode.OK);
    }

    public async Task<BoltzCreateReverseSwapResponse> CreateReverseSwapAsync(
        BoltzCreateReverseSwapRequest request,
        CancellationToken cancellationToken = default)
    {
        var url = $"{GetBaseUrl()}/v2/swap/reverse";
        var json = System.Text.Json.JsonSerializer.Serialize(request, JsonOptions);
        var httpContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, httpContent, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var error = System.Text.Json.JsonSerializer.Deserialize<BoltzErrorResponse>(content, JsonOptions);
                throw new BoltzApiException(error?.Error ?? content, response.StatusCode);
            }
            catch (System.Text.Json.JsonException)
            {
                throw new BoltzApiException(content, response.StatusCode);
            }
        }

        var result = System.Text.Json.JsonSerializer.Deserialize<BoltzCreateReverseSwapResponse>(content, JsonOptions);
        return result ?? throw new BoltzApiException("Empty response", System.Net.HttpStatusCode.OK);
    }

    public async Task<BoltzSwapStatusResponse> GetSwapStatusAsync(
        string swapId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{GetBaseUrl()}/v2/swap/{swapId}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var error = System.Text.Json.JsonSerializer.Deserialize<BoltzErrorResponse>(content, JsonOptions);
                throw new BoltzApiException(error?.Error ?? content, response.StatusCode);
            }
            catch (System.Text.Json.JsonException)
            {
                throw new BoltzApiException(content, response.StatusCode);
            }
        }
        
        var result = System.Text.Json.JsonSerializer.Deserialize<BoltzSwapStatusResponse>(content, JsonOptions);
        return result ?? throw new BoltzApiException("Empty response", System.Net.HttpStatusCode.OK);
    }
}

/// <summary>
/// Testable address validator that doesn't require full BatchExecutionService dependencies.
/// </summary>
public class TestableAddressValidator
{
    private readonly ChainName _network;

    // Bitcoin address regex patterns
    private static readonly System.Text.RegularExpressions.Regex BtcMainnetAddressRegex = new(
        @"^(bc1[a-zA-HJ-NP-Z0-9]{25,87}|[13][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex BtcTestnetAddressRegex = new(
        @"^(tb1[a-zA-HJ-NP-Z0-9]{25,87}|[2mn][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex BtcRegtestAddressRegex = new(
        @"^(bcrt1[a-zA-HJ-NP-Z0-9]{25,87}|[2mn][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex XpubRegex = new(
        @"^([xyztuvXYZTUV]pub[a-zA-HJ-NP-Z0-9]{100,120})$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    // Liquid address regex patterns
    private static readonly System.Text.RegularExpressions.Regex LiquidMainnetAddressRegex = new(
        @"^(ex1[a-zA-HJ-NP-Z0-9]{25,120}|lq1[a-zA-HJ-NP-Z0-9]{25,120}|VJL[a-km-zA-HJ-NP-Z1-9]{76,100}|VTp[a-km-zA-HJ-NP-Z1-9]{76,100}|[GHVW][a-km-zA-HJ-NP-Z1-9]{25,34})$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex LiquidTestnetAddressRegex = new(
        @"^(tex1[a-zA-HJ-NP-Z0-9]{25,120}|tlq1[a-zA-HJ-NP-Z0-9]{25,120}|ert1[a-zA-HJ-NP-Z0-9]{25,120}|el1[a-zA-HJ-NP-Z0-9]{25,120})$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public TestableAddressValidator(ChainName network)
    {
        _network = network;
    }

    public ChainName NetworkType => _network;

    public AddressValidationResult ValidateBitcoinAddressForNetwork(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return new AddressValidationResult(false, "Address is required.");

        var expectedNetworkName = GetNetworkDisplayName(_network);

        // Check if it's an XPUB (valid on all networks)
        if (ValidateXpub(address))
            return new AddressValidationResult(true, null);

        // Determine which network the address belongs to
        var isMainnetAddress = BtcMainnetAddressRegex.IsMatch(address);
        var isTestnetAddress = BtcTestnetAddressRegex.IsMatch(address);
        var isRegtestAddress = BtcRegtestAddressRegex.IsMatch(address);

        if (_network == ChainName.Mainnet)
        {
            if (isMainnetAddress)
                return new AddressValidationResult(true, null);
            if (isTestnetAddress || isRegtestAddress)
                return new AddressValidationResult(false,
                    $"This appears to be a testnet/regtest address, but you are running on {expectedNetworkName}. Please use a mainnet address (starting with bc1, 1, or 3).");
        }
        else if (_network == ChainName.Testnet)
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
        else if (_network == ChainName.Regtest)
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

    public bool ValidateXpub(string xpub)
    {
        if (string.IsNullOrWhiteSpace(xpub))
            return false;
        return XpubRegex.IsMatch(xpub);
    }

    public AddressValidationResult ValidateLiquidAddressForNetwork(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return new AddressValidationResult(false, "Liquid address is required.");

        var isMainnetAddress = LiquidMainnetAddressRegex.IsMatch(address);
        var isTestnetAddress = LiquidTestnetAddressRegex.IsMatch(address);

        if (_network == ChainName.Mainnet)
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
        if (network == ChainName.Mainnet) return "mainnet";
        if (network == ChainName.Testnet) return "testnet";
        if (network == ChainName.Regtest) return "regtest";
        return network.ToString().ToLowerInvariant();
    }
}
