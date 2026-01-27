#nullable enable
using System.Net;
using System.Text.Json;
using BTCPayServer.Plugins.Stash.Services;
using NBitcoin;
using RichardSzalay.MockHttp;
using Xunit;

namespace BTCPayServer.Plugins.Tests.StashPluginTests;

/// <summary>
/// Unit tests for BoltzApiService - verifies API interaction logic with mocked HTTP responses.
/// </summary>
public class BoltzApiServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    #region GetPairsAsync Tests

    [Fact]
    public async Task GetPairsAsync_ReturnsValidResponse_WhenApiSucceeds()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var expectedResponse = new BoltzPairsResponse
        {
            Warnings = new BoltzWarnings { Global = Array.Empty<string>() }
        };

        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/pairs")
            .Respond("application/json", JsonSerializer.Serialize(expectedResponse, JsonOptions));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        var result = await service.GetPairsAsync();

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetPairsAsync_UsesMainnetUrl_WhenOnMainnet()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        mockHttp
            .When("https://api.boltz.exchange/v2/pairs")
            .Respond("application/json", "{}");

        var service = TestHelpers.CreateBoltzApiService(mockHttp, ChainName.Mainnet);

        // Act
        var result = await service.GetPairsAsync();

        // Assert
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetPairsAsync_UsesTestnetUrl_WhenOnTestnet()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/pairs")
            .Respond("application/json", "{}");

        var service = TestHelpers.CreateBoltzApiService(mockHttp, ChainName.Testnet);

        // Act
        var result = await service.GetPairsAsync();

        // Assert
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetPairsAsync_UsesTestnetUrl_WhenOnRegtest()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        // Regtest uses testnet API (without mock server)
        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/pairs")
            .Respond("application/json", "{}");

        var service = TestHelpers.CreateBoltzApiService(mockHttp, ChainName.Regtest);

        // Act
        var result = await service.GetPairsAsync();

        // Assert
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetPairsAsync_ThrowsException_WhenApiReturnsError()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/pairs")
            .Respond(HttpStatusCode.InternalServerError);

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetPairsAsync());
    }

    #endregion

    #region GetReverseQuoteAsync Tests

    [Fact]
    public async Task GetReverseQuoteAsync_ReturnsValidQuote_WhenApiSucceeds()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var invoiceAmount = 100000L;
        var expectedResponse = new BoltzReverseQuoteResponse
        {
            OnchainAmount = 99500,
            MinerFee = 300,
            ServiceFee = 200,
            ServiceFeePercent = 0.2m
        };

        mockHttp
            .When($"https://api.testnet.boltz.exchange/v2/swap/reverse/quote?from=BTC&to=L-BTC&invoiceAmount={invoiceAmount}")
            .Respond("application/json", JsonSerializer.Serialize(expectedResponse, JsonOptions));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        var result = await service.GetReverseQuoteAsync(invoiceAmount);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(99500, result.OnchainAmount);
        Assert.Equal(300, result.MinerFee);
        Assert.Equal(200, result.ServiceFee);
    }

    [Fact]
    public async Task GetReverseQuoteAsync_ReturnsNull_WhenApiReturnsError()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/swap/reverse/quote*")
            .Respond(HttpStatusCode.BadRequest, "application/json", "{\"error\": \"Amount too small\"}");

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        var result = await service.GetReverseQuoteAsync(100);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetReverseQuoteAsync_CalculatesFeesCorrectly()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var invoiceAmount = 500000L; // 500k sats
        
        // Boltz typically charges ~0.5% service fee + mining fee
        var expectedResponse = new BoltzReverseQuoteResponse
        {
            OnchainAmount = 496500, // After fees
            MinerFee = 1000,
            ServiceFee = 2500, // 0.5% of 500k
            ServiceFeePercent = 0.5m
        };

        mockHttp
            .When($"https://api.testnet.boltz.exchange/v2/swap/reverse/quote*")
            .Respond("application/json", JsonSerializer.Serialize(expectedResponse, JsonOptions));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        var result = await service.GetReverseQuoteAsync(invoiceAmount);

        // Assert
        Assert.NotNull(result);
        var totalFees = result.MinerFee + result.ServiceFee;
        Assert.Equal(invoiceAmount - totalFees, result.OnchainAmount);
    }

    #endregion

    #region CreateReverseSwapAsync Tests

    [Fact]
    public async Task CreateReverseSwapAsync_ReturnsSwapDetails_WhenApiSucceeds()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var request = new BoltzCreateReverseSwapRequest
        {
            From = "BTC",
            To = "L-BTC",
            InvoiceAmount = 100000,
            ClaimPublicKey = "02" + new string('a', 64),
            Address = "tex1qtest123",
            ReferralId = "btcpay-stash"
        };

        var expectedResponse = new BoltzCreateReverseSwapResponse
        {
            Id = "swap-test-123",
            Invoice = "lntb1000u1ptest...",
            OnchainAmount = 99500,
            TimeoutBlockHeight = 12345,
            LockupAddress = "tex1qlockup...",
            RefundPublicKey = "03" + new string('b', 64),
            BlindingKey = new string('c', 64)
        };

        mockHttp
            .When(HttpMethod.Post, "https://api.testnet.boltz.exchange/v2/swap/reverse")
            .Respond("application/json", JsonSerializer.Serialize(expectedResponse, JsonOptions));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        var result = await service.CreateReverseSwapAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("swap-test-123", result.Id);
        Assert.StartsWith("lntb", result.Invoice);
        Assert.Equal(99500, result.OnchainAmount);
        Assert.Equal(12345, result.TimeoutBlockHeight);
    }

    [Fact]
    public async Task CreateReverseSwapAsync_ThrowsBoltzApiException_WhenApiReturnsError()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var request = new BoltzCreateReverseSwapRequest
        {
            From = "BTC",
            To = "L-BTC",
            InvoiceAmount = 50, // Too small
            ClaimPublicKey = "02" + new string('a', 64)
        };

        var errorResponse = new { error = "Amount is below minimum" };

        mockHttp
            .When(HttpMethod.Post, "https://api.testnet.boltz.exchange/v2/swap/reverse")
            .Respond(HttpStatusCode.BadRequest, "application/json", 
                JsonSerializer.Serialize(errorResponse, JsonOptions));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BoltzApiException>(
            () => service.CreateReverseSwapAsync(request));
        
        Assert.Contains("below minimum", exception.Message);
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task CreateReverseSwapAsync_IncludesAllRequestFields()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var request = new BoltzCreateReverseSwapRequest
        {
            From = "BTC",
            To = "L-BTC",
            InvoiceAmount = 100000,
            ClaimPublicKey = "02" + new string('a', 64),
            Address = "tex1qtest123",
            ReferralId = "btcpay-stash",
            Description = "Stash batch test-batch-1"
        };

        string? capturedBody = null;
        mockHttp
            .When(HttpMethod.Post, "https://api.testnet.boltz.exchange/v2/swap/reverse")
            .With(req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", JsonSerializer.Serialize(
                new BoltzCreateReverseSwapResponse { Id = "test", Invoice = "lntb..." }, 
                JsonOptions));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        await service.CreateReverseSwapAsync(request);

        // Assert
        Assert.NotNull(capturedBody);
        Assert.Contains("btcpay-stash", capturedBody);
        Assert.Contains("tex1qtest123", capturedBody);
        Assert.Contains("100000", capturedBody);
    }

    #endregion

    #region GetSwapStatusAsync Tests

    [Fact]
    public async Task GetSwapStatusAsync_ReturnsStatus_WhenSwapExists()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var swapId = "swap-test-123";
        var expectedResponse = new BoltzSwapStatusResponse
        {
            Status = BoltzSwapStatus.TransactionClaimed,
            Transaction = new BoltzTransaction
            {
                Id = "liquid-tx-abc123"
            }
        };

        mockHttp
            .When($"https://api.testnet.boltz.exchange/v2/swap/{swapId}")
            .Respond("application/json", JsonSerializer.Serialize(expectedResponse, JsonOptions));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        var result = await service.GetSwapStatusAsync(swapId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(BoltzSwapStatus.TransactionClaimed, result.Status);
        Assert.Equal("liquid-tx-abc123", result.Transaction?.Id);
    }

    [Fact]
    public async Task GetSwapStatusAsync_ReturnsNull_WhenSwapNotFound()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/swap/*")
            .Respond(HttpStatusCode.NotFound);

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        var result = await service.GetSwapStatusAsync("nonexistent-swap");

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData(BoltzSwapStatus.Created)]
    [InlineData(BoltzSwapStatus.InvoicePending)]
    [InlineData(BoltzSwapStatus.InvoicePaid)]
    [InlineData(BoltzSwapStatus.TransactionMempool)]
    [InlineData(BoltzSwapStatus.TransactionConfirmed)]
    public async Task GetSwapStatusAsync_ReturnsCorrectStatus_ForAllStates(string expectedStatus)
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var response = new BoltzSwapStatusResponse { Status = expectedStatus };

        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/swap/*")
            .Respond("application/json", JsonSerializer.Serialize(response, JsonOptions));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        var result = await service.GetSwapStatusAsync("test-swap");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedStatus, result.Status);
    }

    #endregion

    #region BoltzSwapStatus Helper Tests

    [Theory]
    [InlineData(BoltzSwapStatus.TransactionClaimed, true)]
    [InlineData(BoltzSwapStatus.TransactionRefunded, true)]
    [InlineData(BoltzSwapStatus.SwapExpired, true)]
    [InlineData(BoltzSwapStatus.InvoiceFailedToPay, true)]
    [InlineData(BoltzSwapStatus.InvoiceExpired, true)]
    [InlineData(BoltzSwapStatus.TransactionLockupFailed, true)]
    [InlineData(BoltzSwapStatus.Created, false)]
    [InlineData(BoltzSwapStatus.InvoicePaid, false)]
    [InlineData(BoltzSwapStatus.TransactionMempool, false)]
    public void IsFinalState_ReturnsCorrectValue(string status, bool expectedIsFinal)
    {
        // Act
        var result = BoltzSwapStatus.IsFinalState(status);

        // Assert
        Assert.Equal(expectedIsFinal, result);
    }

    [Theory]
    [InlineData(BoltzSwapStatus.TransactionClaimed, true)]
    [InlineData(BoltzSwapStatus.TransactionRefunded, false)]
    [InlineData(BoltzSwapStatus.SwapExpired, false)]
    [InlineData(BoltzSwapStatus.InvoicePaid, false)]
    public void IsSuccessState_ReturnsCorrectValue(string status, bool expectedIsSuccess)
    {
        // Act
        var result = BoltzSwapStatus.IsSuccessState(status);

        // Assert
        Assert.Equal(expectedIsSuccess, result);
    }

    [Theory]
    [InlineData(BoltzSwapStatus.TransactionRefunded, true)]
    [InlineData(BoltzSwapStatus.SwapExpired, true)]
    [InlineData(BoltzSwapStatus.InvoiceFailedToPay, true)]
    [InlineData(BoltzSwapStatus.InvoiceExpired, true)]
    [InlineData(BoltzSwapStatus.TransactionLockupFailed, true)]
    [InlineData(BoltzSwapStatus.TransactionClaimed, false)]
    [InlineData(BoltzSwapStatus.InvoicePaid, false)]
    public void IsFailedState_ReturnsCorrectValue(string status, bool expectedIsFailed)
    {
        // Act
        var result = BoltzSwapStatus.IsFailedState(status);

        // Assert
        Assert.Equal(expectedIsFailed, result);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void IsTransientError_ReturnsTrueForHttpRequestException()
    {
        // Arrange
        var exception = new HttpRequestException("Connection refused");

        // Act
        var result = BoltzApiService.IsTransientError(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsTransientError_ReturnsTrueForTaskCanceledException()
    {
        // Arrange
        var exception = new TaskCanceledException("Request timed out");

        // Act
        var result = BoltzApiService.IsTransientError(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsTransientError_ReturnsTrueForTimeoutException()
    {
        // Arrange
        var exception = new TimeoutException("Operation timed out");

        // Act
        var result = BoltzApiService.IsTransientError(exception);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void IsTransientError_ReturnsCorrectValueForBoltzApiException(
        HttpStatusCode statusCode, 
        bool expectedIsTransient)
    {
        // Arrange
        var exception = new BoltzApiException("Test error", statusCode);

        // Act
        var result = BoltzApiService.IsTransientError(exception);

        // Assert
        Assert.Equal(expectedIsTransient, result);
    }

    #endregion

    #region Network Timeout Tests

    [Fact]
    public async Task GetReverseQuoteAsync_HandlesTimeoutGracefully()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/swap/reverse/quote*")
            .Throw(new TaskCanceledException("Request timed out"));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => service.GetReverseQuoteAsync(100000));
    }

    #endregion
}
