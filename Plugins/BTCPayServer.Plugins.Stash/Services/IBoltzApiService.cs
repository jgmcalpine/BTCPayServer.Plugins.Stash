#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.Stash.Services;

/// <summary>
/// Interface for the Boltz API service.
/// Enables testing by allowing mock implementations.
/// </summary>
public interface IBoltzApiService
{
    /// <summary>
    /// Whether the service is using the mock Boltz server.
    /// </summary>
    bool IsMockMode { get; }

    /// <summary>
    /// Gets available pairs and their limits from Boltz.
    /// </summary>
    Task<BoltzPairsResponse?> GetPairsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a quote for a reverse swap (Lightning -> Liquid).
    /// This is the first step: get current fees and limits.
    /// </summary>
    /// <exception cref="BoltzApiException">Thrown when Boltz API returns an error response.</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown for network-level errors.</exception>
    Task<BoltzReverseQuoteResponse> GetReverseQuoteAsync(
        long invoiceAmountSats,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a reverse swap (Lightning -> Liquid).
    /// Returns a Lightning invoice that must be paid, and Boltz will send Liquid to the claim address.
    /// </summary>
    /// <exception cref="BoltzApiException">Thrown when Boltz API returns an error response.</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown for network-level errors.</exception>
    Task<BoltzCreateReverseSwapResponse> CreateReverseSwapAsync(
        BoltzCreateReverseSwapRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a swap.
    /// </summary>
    /// <exception cref="BoltzApiException">Thrown when Boltz API returns an error response.</exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Thrown for network-level errors.</exception>
    Task<BoltzSwapStatusResponse> GetSwapStatusAsync(
        string swapId,
        CancellationToken cancellationToken = default);
}
