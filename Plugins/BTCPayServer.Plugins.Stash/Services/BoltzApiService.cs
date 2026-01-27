#nullable enable
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Stash.Services;

/// <summary>
/// Service for interacting with the Boltz API v2.
/// Handles reverse submarine swaps: Lightning BTC -> Liquid USDT
/// 
/// Supports mock mode for regtest testing via STASH_MOCK_BOLTZ=true environment variable.
/// </summary>
public class BoltzApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BTCPayServerEnvironment _environment;
    private readonly MockBoltzOptions? _mockOptions;
    private readonly ILogger<BoltzApiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public BoltzApiService(
        IHttpClientFactory httpClientFactory,
        BTCPayServerEnvironment environment,
        ILogger<BoltzApiService> logger,
        MockBoltzOptions? mockOptions = null)
    {
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _logger = logger;
        _mockOptions = mockOptions;
    }

    /// <summary>
    /// Whether the service is using the mock Boltz server.
    /// </summary>
    public bool IsMockMode => _mockOptions?.Enabled == true && 
                              _environment.NetworkType == NBitcoin.ChainName.Regtest;

    /// <summary>
    /// Gets the Boltz API base URL based on the current network and mock settings.
    /// </summary>
    private string GetBaseUrl()
    {
        // Use mock server for regtest when enabled
        if (_mockOptions?.Enabled == true && 
            _environment.NetworkType == NBitcoin.ChainName.Regtest)
        {
            var mockUrl = $"http://localhost:{_mockOptions.Port}";
            _logger.LogDebug("Using mock Boltz server at {Url}", mockUrl);
            return mockUrl;
        }

        // Boltz uses the same API for mainnet and testnet, but different endpoints
        // Mainnet: https://api.boltz.exchange
        // Testnet: https://api.testnet.boltz.exchange
        if (_environment.NetworkType == NBitcoin.ChainName.Mainnet)
            return "https://api.boltz.exchange";
        
        // For testnet and regtest (without mock), use testnet API
        return "https://api.testnet.boltz.exchange";
    }

    /// <summary>
    /// Gets available pairs and their limits from Boltz.
    /// </summary>
    public async Task<BoltzPairsResponse?> GetPairsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Boltz");
            var url = $"{GetBaseUrl()}/v2/pairs";
            
            var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadFromJsonAsync<BoltzPairsResponse>(JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Boltz pairs");
            throw;
        }
    }

    /// <summary>
    /// Gets a quote for a reverse swap (Lightning -> Liquid).
    /// This is the first step: get current fees and limits.
    /// </summary>
    public async Task<BoltzReverseQuoteResponse?> GetReverseQuoteAsync(
        long invoiceAmountSats,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Boltz");
            // BTC/L-BTC is Lightning BTC -> Liquid BTC
            // For USDT, we need to check what pair Boltz supports
            // The typical pair for Lightning -> Liquid USDT would be BTC/USDT
            var url = $"{GetBaseUrl()}/v2/swap/reverse/quote?from=BTC&to=L-BTC&invoiceAmount={invoiceAmountSats}";
            
            _logger.LogDebug("Getting reverse quote from Boltz: {Url}", url);
            
            var response = await client.GetAsync(url, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Boltz quote failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }
            
            return await response.Content.ReadFromJsonAsync<BoltzReverseQuoteResponse>(JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Boltz reverse quote");
            throw;
        }
    }

    /// <summary>
    /// Creates a reverse swap (Lightning -> Liquid).
    /// Returns a Lightning invoice that must be paid, and Boltz will send Liquid to the claim address.
    /// </summary>
    public async Task<BoltzCreateReverseSwapResponse?> CreateReverseSwapAsync(
        BoltzCreateReverseSwapRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Boltz");
            var url = $"{GetBaseUrl()}/v2/swap/reverse";
            
            var json = JsonSerializer.Serialize(request, JsonOptions);
            _logger.LogDebug("Creating reverse swap with Boltz: {Request}", json);
            
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content, cancellationToken);
            
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Boltz create swap failed: {StatusCode} - {Error}", response.StatusCode, responseContent);
                
                // Try to parse error response
                try
                {
                    var errorResponse = JsonSerializer.Deserialize<BoltzErrorResponse>(responseContent, JsonOptions);
                    throw new BoltzApiException(errorResponse?.Error ?? responseContent, response.StatusCode);
                }
                catch (JsonException)
                {
                    throw new BoltzApiException(responseContent, response.StatusCode);
                }
            }
            
            return JsonSerializer.Deserialize<BoltzCreateReverseSwapResponse>(responseContent, JsonOptions);
        }
        catch (BoltzApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Boltz reverse swap");
            throw;
        }
    }

    /// <summary>
    /// Gets the status of a swap.
    /// </summary>
    public async Task<BoltzSwapStatusResponse?> GetSwapStatusAsync(
        string swapId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Boltz");
            var url = $"{GetBaseUrl()}/v2/swap/{swapId}";
            
            var response = await client.GetAsync(url, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Boltz get status failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }
            
            return await response.Content.ReadFromJsonAsync<BoltzSwapStatusResponse>(JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Boltz swap status for {SwapId}", swapId);
            throw;
        }
    }

    /// <summary>
    /// Checks if the error is transient (network-related) and may resolve on retry.
    /// </summary>
    public static bool IsTransientError(Exception ex)
    {
        return ex is HttpRequestException
            || ex is TaskCanceledException
            || ex is TimeoutException
            || (ex is BoltzApiException boltzEx && IsTransientStatusCode(boltzEx.StatusCode));
    }

    private static bool IsTransientStatusCode(System.Net.HttpStatusCode statusCode)
    {
        return statusCode == System.Net.HttpStatusCode.RequestTimeout
            || statusCode == System.Net.HttpStatusCode.BadGateway
            || statusCode == System.Net.HttpStatusCode.ServiceUnavailable
            || statusCode == System.Net.HttpStatusCode.GatewayTimeout;
    }
}

#region Boltz API Models

/// <summary>
/// Request to create a reverse swap (Lightning -> Liquid).
/// </summary>
public class BoltzCreateReverseSwapRequest
{
    /// <summary>
    /// Source asset (BTC for Lightning).
    /// </summary>
    public string From { get; set; } = "BTC";

    /// <summary>
    /// Destination asset (L-BTC for Liquid Bitcoin, or specific asset).
    /// </summary>
    public string To { get; set; } = "L-BTC";

    /// <summary>
    /// Invoice amount in satoshis (the amount to send via Lightning).
    /// </summary>
    public long InvoiceAmount { get; set; }

    /// <summary>
    /// Preimage hash for the swap (32 bytes hex encoded).
    /// If not provided, Boltz generates one.
    /// </summary>
    public string? PreimageHash { get; set; }

    /// <summary>
    /// Claim public key for the Liquid output (33 bytes hex encoded).
    /// </summary>
    public string ClaimPublicKey { get; set; } = null!;

    /// <summary>
    /// Liquid address where funds will be sent.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Address signature for verification (optional).
    /// </summary>
    public string? AddressSignature { get; set; }

    /// <summary>
    /// Referral ID for tracking (optional).
    /// </summary>
    public string? ReferralId { get; set; }

    /// <summary>
    /// Description to include in the Lightning invoice (optional).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Description hash for the Lightning invoice (optional).
    /// </summary>
    public string? DescriptionHash { get; set; }
}

/// <summary>
/// Response from creating a reverse swap.
/// </summary>
public class BoltzCreateReverseSwapResponse
{
    /// <summary>
    /// Unique swap ID.
    /// </summary>
    public string Id { get; set; } = null!;

    /// <summary>
    /// Lightning invoice to pay.
    /// </summary>
    public string Invoice { get; set; } = null!;

    /// <summary>
    /// Swap tree for claiming on Liquid.
    /// </summary>
    public BoltzSwapTree? SwapTree { get; set; }

    /// <summary>
    /// Lockup address on Liquid (where Boltz locks funds).
    /// </summary>
    public string? LockupAddress { get; set; }

    /// <summary>
    /// Refund public key from Boltz.
    /// </summary>
    public string? RefundPublicKey { get; set; }

    /// <summary>
    /// Timeout block height.
    /// </summary>
    public long TimeoutBlockHeight { get; set; }

    /// <summary>
    /// Onchain amount that will be received (after fees).
    /// </summary>
    public long OnchainAmount { get; set; }

    /// <summary>
    /// Blinding key for confidential transactions.
    /// </summary>
    public string? BlindingKey { get; set; }
}

/// <summary>
/// Swap tree for Liquid claim.
/// </summary>
public class BoltzSwapTree
{
    public string? ClaimLeaf { get; set; }
    public string? RefundLeaf { get; set; }
}

/// <summary>
/// Response from getting a reverse quote.
/// </summary>
public class BoltzReverseQuoteResponse
{
    /// <summary>
    /// Onchain amount in satoshis (what you'll receive).
    /// </summary>
    public long OnchainAmount { get; set; }

    /// <summary>
    /// Miner fee in satoshis.
    /// </summary>
    public long MinerFee { get; set; }

    /// <summary>
    /// Service fee in satoshis.
    /// </summary>
    public long ServiceFee { get; set; }

    /// <summary>
    /// Service fee percentage.
    /// </summary>
    public decimal? ServiceFeePercent { get; set; }
}

/// <summary>
/// Response from getting swap status.
/// </summary>
public class BoltzSwapStatusResponse
{
    /// <summary>
    /// Current status of the swap.
    /// </summary>
    public string Status { get; set; } = null!;

    /// <summary>
    /// Transaction details if available.
    /// </summary>
    public BoltzTransaction? Transaction { get; set; }

    /// <summary>
    /// Failure reason if swap failed.
    /// </summary>
    public string? FailureReason { get; set; }
}

/// <summary>
/// Transaction details from Boltz.
/// </summary>
public class BoltzTransaction
{
    /// <summary>
    /// Transaction ID.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Transaction hex.
    /// </summary>
    public string? Hex { get; set; }
}

/// <summary>
/// Response containing available pairs.
/// </summary>
public class BoltzPairsResponse
{
    public BoltzWarnings? Warnings { get; set; }
    public BoltzPairInfo? Submarine { get; set; }
    public BoltzPairInfo? Reverse { get; set; }
    public BoltzPairInfo? Chain { get; set; }
}

public class BoltzWarnings
{
    public string[]? Global { get; set; }
}

public class BoltzPairInfo
{
    // Pair-specific info varies by pair
}

/// <summary>
/// Error response from Boltz API.
/// </summary>
public class BoltzErrorResponse
{
    public string? Error { get; set; }
}

/// <summary>
/// Boltz swap states.
/// </summary>
public static class BoltzSwapStatus
{
    // Initial states
    public const string Created = "swap.created";
    public const string InvoiceSet = "invoice.set";
    
    // Payment states
    public const string InvoicePending = "invoice.pending";
    public const string InvoicePaid = "invoice.paid";
    public const string InvoiceFailedToPay = "invoice.failedToPay";
    public const string InvoiceExpired = "invoice.expired";
    
    // Transaction states
    public const string TransactionMempool = "transaction.mempool";
    public const string TransactionConfirmed = "transaction.confirmed";
    public const string TransactionClaimed = "transaction.claimed";
    public const string TransactionRefunded = "transaction.refunded";
    public const string TransactionLockupFailed = "transaction.lockupFailed";
    
    // Final states
    public const string SwapExpired = "swap.expired";

    /// <summary>
    /// Checks if the swap is in a final (completed or failed) state.
    /// </summary>
    public static bool IsFinalState(string status)
    {
        return status == TransactionClaimed
            || status == TransactionRefunded
            || status == SwapExpired
            || status == InvoiceFailedToPay
            || status == InvoiceExpired
            || status == TransactionLockupFailed;
    }

    /// <summary>
    /// Checks if the swap completed successfully.
    /// </summary>
    public static bool IsSuccessState(string status)
    {
        return status == TransactionClaimed;
    }

    /// <summary>
    /// Checks if the swap failed.
    /// </summary>
    public static bool IsFailedState(string status)
    {
        return status == TransactionRefunded
            || status == SwapExpired
            || status == InvoiceFailedToPay
            || status == InvoiceExpired
            || status == TransactionLockupFailed;
    }
}

/// <summary>
/// Exception for Boltz API errors.
/// </summary>
public class BoltzApiException : Exception
{
    public System.Net.HttpStatusCode StatusCode { get; }

    public BoltzApiException(string message, System.Net.HttpStatusCode statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}

#endregion

