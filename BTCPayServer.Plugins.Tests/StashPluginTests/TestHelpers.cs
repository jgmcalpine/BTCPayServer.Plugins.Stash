#nullable enable
using System.Net.Http;
using BTCPayServer.Plugins.Stash.Services;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using RichardSzalay.MockHttp;

namespace BTCPayServer.Plugins.Tests.StashPluginTests;

/// <summary>
/// Test helpers for creating testable service instances.
/// All helpers create real production service instances with mocked dependencies,
/// ensuring tests verify actual production code behavior.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Creates a real BoltzApiService for testing with a mocked HTTP handler.
    /// Uses the ChainName constructor to avoid BTCPayServerEnvironment mocking issues.
    /// </summary>
    public static BoltzApiService CreateBoltzApiService(
        MockHttpMessageHandler mockHttp,
        ChainName? network = null)
    {
        var effectiveNetwork = network ?? ChainName.Testnet;
        
        // Create a mock IHttpClientFactory that returns the mock HTTP client
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        mockHttpClientFactory
            .Setup(f => f.CreateClient("Boltz"))
            .Returns(mockHttp.ToHttpClient());

        // Create a mock logger
        var mockLogger = Mock.Of<ILogger<BoltzApiService>>();

        // Create the real BoltzApiService with the ChainName constructor
        return new BoltzApiService(
            mockHttpClientFactory.Object,
            effectiveNetwork,
            mockLogger,
            mockOptions: null);
    }

    /// <summary>
    /// Creates a real AddressValidator for testing.
    /// Uses the ChainName constructor to avoid BTCPayServerEnvironment mocking issues.
    /// </summary>
    public static AddressValidator CreateAddressValidator(ChainName network)
    {
        // Use the ChainName constructor directly - no mocking needed
        return new AddressValidator(network);
    }
}
