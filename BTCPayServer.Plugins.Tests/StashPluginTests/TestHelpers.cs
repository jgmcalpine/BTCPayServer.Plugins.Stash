#nullable enable
using System.Net.Http;
using BTCPayServer.Plugins.Stash.Services;
using BTCPayServer.Services;
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
    /// This tests the actual production code, not a duplicate.
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

        // Create a mock BTCPayServerEnvironment
        var mockEnvironment = new Mock<BTCPayServerEnvironment>(
            MockBehavior.Loose,
            null!, // IWebHostEnvironment
            null!, // IOptions<DataDirectories>
            null!  // BTCPayNetworkProvider
        );
        mockEnvironment.Setup(e => e.NetworkType).Returns(effectiveNetwork);

        // Create a mock logger
        var mockLogger = Mock.Of<ILogger<BoltzApiService>>();

        // Create the real BoltzApiService with mocked dependencies
        return new BoltzApiService(
            mockHttpClientFactory.Object,
            mockEnvironment.Object,
            mockLogger,
            mockOptions: null);
    }

    /// <summary>
    /// Creates a real AddressValidator for testing with a mocked environment.
    /// This tests the actual production code, not a duplicate.
    /// </summary>
    public static AddressValidator CreateAddressValidator(ChainName network)
    {
        // Create a mock BTCPayServerEnvironment
        var mockEnvironment = new Mock<BTCPayServerEnvironment>(
            MockBehavior.Loose,
            null!, // IWebHostEnvironment
            null!, // IOptions<DataDirectories>
            null!  // BTCPayNetworkProvider
        );
        mockEnvironment.Setup(e => e.NetworkType).Returns(network);

        // Create the real AddressValidator with mocked dependencies
        return new AddressValidator(mockEnvironment.Object);
    }
}
