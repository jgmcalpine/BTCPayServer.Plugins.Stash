#nullable enable
using System.Net;
using System.Text.Json;
using BTCPayServer.Plugins.Stash.Data.Models;
using BTCPayServer.Plugins.Stash.Services;
using NBitcoin;
using RichardSzalay.MockHttp;
using Xunit;

namespace BTCPayServer.Plugins.Tests.StashPluginTests;

/// <summary>
/// Integration tests for the swap execution flow.
/// Tests the complete Boltz swap lifecycle with mocked HTTP responses.
/// </summary>
public class SwapExecutionFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    #region Complete Swap Flow Tests

    [Fact]
    public async Task CompleteSwapFlow_QuoteToCompletion_Success()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        var invoiceAmount = 500000L; // 500k sats

        // Step 1: Quote
        var quoteResponse = new BoltzReverseQuoteResponse
        {
            OnchainAmount = 496500,
            MinerFee = 1000,
            ServiceFee = 2500
        };
        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/swap/reverse/quote*")
            .Respond("application/json", JsonSerializer.Serialize(quoteResponse, JsonOptions));

        // Step 2: Create swap
        var createResponse = new BoltzCreateReverseSwapResponse
        {
            Id = "swap-flow-test-123",
            Invoice = "lntb5m1ptest...",
            OnchainAmount = 496500,
            TimeoutBlockHeight = 12345,
            LockupAddress = "tex1qlockup...",
            BlindingKey = new string('a', 64)
        };
        mockHttp
            .When(HttpMethod.Post, "https://api.testnet.boltz.exchange/v2/swap/reverse")
            .Respond("application/json", JsonSerializer.Serialize(createResponse, JsonOptions));

        // Step 3: Status polling - starts with created, then paid, then claimed
        var statusSequence = new Queue<BoltzSwapStatusResponse>(new[]
        {
            new BoltzSwapStatusResponse { Status = BoltzSwapStatus.Created },
            new BoltzSwapStatusResponse { Status = BoltzSwapStatus.InvoicePaid },
            new BoltzSwapStatusResponse { Status = BoltzSwapStatus.TransactionMempool },
            new BoltzSwapStatusResponse 
            { 
                Status = BoltzSwapStatus.TransactionClaimed,
                Transaction = new BoltzTransaction { Id = "liquid-claim-tx-abc123" }
            }
        });

        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/swap/swap-flow-test-123")
            .Respond(_ => 
            {
                var response = statusSequence.Count > 1 ? statusSequence.Dequeue() : statusSequence.Peek();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(response, JsonOptions),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            });

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act - Execute the flow
        // 1. Get quote
        var quote = await service.GetReverseQuoteAsync(invoiceAmount);
        Assert.NotNull(quote);
        Assert.Equal(496500, quote.OnchainAmount);

        // 2. Create swap
        var createRequest = new BoltzCreateReverseSwapRequest
        {
            From = "BTC",
            To = "L-BTC",
            InvoiceAmount = invoiceAmount,
            ClaimPublicKey = "02" + new string('a', 64),
            Address = "tex1qtest..."
        };
        var swap = await service.CreateReverseSwapAsync(createRequest);
        Assert.NotNull(swap);
        Assert.Equal("swap-flow-test-123", swap.Id);

        // 3. Poll for completion
        BoltzSwapStatusResponse? finalStatus = null;
        for (int i = 0; i < 5; i++)
        {
            var status = await service.GetSwapStatusAsync(swap.Id);
            if (BoltzSwapStatus.IsFinalState(status.Status))
            {
                finalStatus = status;
                break;
            }
            await Task.Delay(10); // Minimal delay for test
        }

        // Assert
        Assert.NotNull(finalStatus);
        Assert.Equal(BoltzSwapStatus.TransactionClaimed, finalStatus.Status);
        Assert.Equal("liquid-claim-tx-abc123", finalStatus.Transaction?.Id);
    }

    [Fact]
    public async Task SwapFlow_FailsGracefully_WhenQuoteFails()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/swap/reverse/quote*")
            .Respond(HttpStatusCode.BadRequest, "application/json", 
                "{\"error\": \"Amount below minimum\"}");

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act & Assert - now throws BoltzApiException
        var exception = await Assert.ThrowsAsync<BoltzApiException>(
            () => service.GetReverseQuoteAsync(100)); // Too small
        Assert.Contains("Amount below minimum", exception.Message);
    }

    [Fact]
    public async Task SwapFlow_FailsGracefully_WhenCreateSwapFails()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        // Quote succeeds
        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/swap/reverse/quote*")
            .Respond("application/json", "{\"onchainAmount\": 99000}");

        // But create fails
        mockHttp
            .When(HttpMethod.Post, "https://api.testnet.boltz.exchange/v2/swap/reverse")
            .Respond(HttpStatusCode.BadRequest, "application/json",
                "{\"error\": \"Invalid claim public key\"}");

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        var quote = await service.GetReverseQuoteAsync(100000);
        Assert.NotNull(quote);

        var request = new BoltzCreateReverseSwapRequest
        {
            InvoiceAmount = 100000,
            ClaimPublicKey = "invalid"
        };

        // Assert
        var exception = await Assert.ThrowsAsync<BoltzApiException>(
            () => service.CreateReverseSwapAsync(request));
        Assert.Contains("Invalid claim public key", exception.Message);
    }

    [Fact]
    public async Task SwapFlow_DetectsSwapExpired()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        var expiredResponse = new BoltzSwapStatusResponse
        {
            Status = BoltzSwapStatus.SwapExpired,
            FailureReason = "Invoice not paid within timeout"
        };

        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/swap/*")
            .Respond("application/json", JsonSerializer.Serialize(expiredResponse, JsonOptions));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        var status = await service.GetSwapStatusAsync("expired-swap-123");

        // Assert
        Assert.NotNull(status);
        Assert.True(BoltzSwapStatus.IsFinalState(status.Status));
        Assert.True(BoltzSwapStatus.IsFailedState(status.Status));
        Assert.False(BoltzSwapStatus.IsSuccessState(status.Status));
    }

    [Fact]
    public async Task SwapFlow_DetectsInvoicePaymentFailed()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        var failedResponse = new BoltzSwapStatusResponse
        {
            Status = BoltzSwapStatus.InvoiceFailedToPay,
            FailureReason = "No route found"
        };

        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/swap/*")
            .Respond("application/json", JsonSerializer.Serialize(failedResponse, JsonOptions));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        var status = await service.GetSwapStatusAsync("failed-payment-swap");

        // Assert
        Assert.NotNull(status);
        Assert.Equal(BoltzSwapStatus.InvoiceFailedToPay, status.Status);
        Assert.True(BoltzSwapStatus.IsFailedState(status.Status));
    }

    #endregion

    #region Fee Calculation Tests

    [Theory]
    [InlineData(100000, 99000, 300, 700)]      // 1% total fee
    [InlineData(500000, 496500, 1000, 2500)]   // 0.7% total fee
    [InlineData(1000000, 993500, 1500, 5000)]  // 0.65% total fee
    public void CalculateTotalFees_MatchesQuoteResponse(
        long invoiceAmount,
        long onchainAmount,
        long expectedMinerFee,
        long expectedServiceFee)
    {
        // Arrange
        var quote = new BoltzReverseQuoteResponse
        {
            OnchainAmount = onchainAmount,
            MinerFee = expectedMinerFee,
            ServiceFee = expectedServiceFee
        };

        // Act
        var totalFees = quote.MinerFee + quote.ServiceFee;
        var calculatedOnchain = invoiceAmount - totalFees;

        // Assert
        Assert.Equal(onchainAmount, calculatedOnchain);
        Assert.Equal(invoiceAmount, onchainAmount + totalFees);
    }

    #endregion

    #region Batch State Tracking Tests

    [Fact]
    public void BatchStatus_TransitionsCorrectly_Success()
    {
        // Arrange & Act - Simulate successful batch lifecycle
        var batch = new ExecutedBatch
        {
            Status = BatchStatus.Pending
        };

        // Transition to Processing
        batch.Status = BatchStatus.Processing;
        Assert.Equal(BatchStatus.Processing, batch.Status);

        // Transition to Completed
        batch.Status = BatchStatus.Completed;
        batch.CompletedAt = DateTimeOffset.UtcNow;
        batch.TransactionId = "tx-success-123";

        // Assert
        Assert.Equal(BatchStatus.Completed, batch.Status);
        Assert.NotNull(batch.CompletedAt);
        Assert.NotNull(batch.TransactionId);
    }

    [Fact]
    public void BatchStatus_TransitionsCorrectly_Failure()
    {
        // Arrange & Act
        var batch = new ExecutedBatch
        {
            Status = BatchStatus.Pending
        };

        batch.Status = BatchStatus.Processing;
        
        // Transition to Failed
        batch.Status = BatchStatus.Failed;
        batch.ErrorMessage = "Swap timed out";
        batch.IsRetryable = true;
        batch.CompletedAt = DateTimeOffset.UtcNow;

        // Assert
        Assert.Equal(BatchStatus.Failed, batch.Status);
        Assert.NotNull(batch.ErrorMessage);
        Assert.True(batch.IsRetryable);
    }

    [Theory]
    [InlineData(0, true)]  // First attempt
    [InlineData(1, true)]  // Second attempt
    [InlineData(4, true)]  // Fifth attempt (max is 5)
    [InlineData(5, false)] // Beyond max
    [InlineData(10, false)] // Well beyond max
    public void ShouldAutoRetry_RespectsMaxRetries(int retryCount, bool expectedShouldRetry)
    {
        // Arrange
        const int MaxAutoRetries = 5;
        var batch = new ExecutedBatch
        {
            Status = BatchStatus.Failed,
            IsRetryable = true,
            RetryCount = retryCount
        };

        // Act
        var shouldRetry = batch.Status == BatchStatus.Failed 
            && batch.IsRetryable 
            && batch.RetryCount < MaxAutoRetries;

        // Assert
        Assert.Equal(expectedShouldRetry, shouldRetry);
    }

    #endregion

    #region BoltzSwap State Tracking Tests

    [Fact]
    public void BoltzSwap_TracksFullLifecycle()
    {
        // Arrange
        var swap = new BoltzSwap
        {
            BoltzSwapId = "test-swap-123",
            State = BoltzSwapState.Created,
            InvoiceAmountSats = 500000,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act & Assert - Track through lifecycle
        Assert.Equal(BoltzSwapState.Created, swap.State);

        // Invoice paid
        swap.State = BoltzSwapState.Paid;
        swap.PaidAt = DateTimeOffset.UtcNow;
        swap.Preimage = "0123456789abcdef";
        Assert.Equal(BoltzSwapState.Paid, swap.State);
        Assert.NotNull(swap.PaidAt);

        // Completed
        swap.State = BoltzSwapState.Completed;
        swap.CompletedAt = DateTimeOffset.UtcNow;
        swap.ClaimTransactionId = "liquid-tx-abc";
        Assert.Equal(BoltzSwapState.Completed, swap.State);
        Assert.NotNull(swap.CompletedAt);
        Assert.NotNull(swap.ClaimTransactionId);
    }

    [Fact]
    public void BoltzSwap_TracksFailure()
    {
        // Arrange
        var swap = new BoltzSwap
        {
            BoltzSwapId = "failing-swap-123",
            State = BoltzSwapState.Paid
        };

        // Act
        swap.State = BoltzSwapState.Failed;
        swap.ErrorMessage = "Swap expired before claim";
        swap.IsRetryable = false;
        swap.UpdatedAt = DateTimeOffset.UtcNow;

        // Assert
        Assert.Equal(BoltzSwapState.Failed, swap.State);
        Assert.NotNull(swap.ErrorMessage);
        Assert.False(swap.IsRetryable);
    }

    #endregion

    #region Network Error Handling

    [Fact]
    public async Task SwapFlow_HandlesNetworkTimeout()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/swap/reverse/quote*")
            .Throw(new TaskCanceledException("Request timed out"));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<TaskCanceledException>(
            () => service.GetReverseQuoteAsync(100000));
        
        Assert.True(BoltzApiService.IsTransientError(exception));
    }

    [Fact]
    public async Task SwapFlow_HandlesConnectionRefused()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        mockHttp
            .When("https://api.testnet.boltz.exchange/v2/swap/reverse/quote*")
            .Throw(new HttpRequestException("Connection refused"));

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetReverseQuoteAsync(100000));
        
        Assert.True(BoltzApiService.IsTransientError(exception));
    }

    [Fact]
    public async Task SwapFlow_Handles503ServiceUnavailable()
    {
        // Arrange
        var mockHttp = new MockHttpMessageHandler();
        
        mockHttp
            .When(HttpMethod.Post, "https://api.testnet.boltz.exchange/v2/swap/reverse")
            .Respond(HttpStatusCode.ServiceUnavailable, "application/json",
                "{\"error\": \"Service temporarily unavailable\"}");

        var service = TestHelpers.CreateBoltzApiService(mockHttp);

        // Act
        var request = new BoltzCreateReverseSwapRequest
        {
            InvoiceAmount = 100000,
            ClaimPublicKey = "02" + new string('a', 64)
        };

        var exception = await Assert.ThrowsAsync<BoltzApiException>(
            () => service.CreateReverseSwapAsync(request));

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.True(BoltzApiService.IsTransientError(exception));
    }

    #endregion
}
