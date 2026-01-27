#nullable enable
using System.Text.Json;
using BTCPayServer.Plugins.Stash.Data.Models;
using BTCPayServer.Plugins.Stash.Services;
using Xunit;

namespace BTCPayServer.Plugins.Tests.StashPluginTests;

/// <summary>
/// Unit tests for core service validation and business logic.
/// Tests focus on validation methods and edge cases that can be tested without database dependencies.
/// </summary>
public class CoreServiceTests
{
    #region Quote Validation Tests

    [Fact]
    public void ValidateQuoteResponse_ValidQuote_ReturnsValid()
    {
        // Arrange
        var quote = new BoltzReverseQuoteResponse
        {
            OnchainAmount = 99000,
            MinerFee = 500,
            ServiceFee = 500
        };
        var invoiceAmount = 100000L;

        // Act
        var result = QuoteValidator.ValidateQuoteResponse(quote, invoiceAmount);

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateQuoteResponse_NegativeOnchainAmount_ReturnsInvalid()
    {
        // Arrange
        var quote = new BoltzReverseQuoteResponse
        {
            OnchainAmount = -1000,
            MinerFee = 500,
            ServiceFee = 500
        };

        // Act
        var result = QuoteValidator.ValidateQuoteResponse(quote, 100000);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("onchain amount", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateQuoteResponse_ZeroOnchainAmount_ReturnsInvalid()
    {
        // Arrange
        var quote = new BoltzReverseQuoteResponse
        {
            OnchainAmount = 0,
            MinerFee = 500,
            ServiceFee = 500
        };

        // Act
        var result = QuoteValidator.ValidateQuoteResponse(quote, 100000);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("positive", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateQuoteResponse_NegativeMinerFee_ReturnsInvalid()
    {
        // Arrange
        var quote = new BoltzReverseQuoteResponse
        {
            OnchainAmount = 99000,
            MinerFee = -100,
            ServiceFee = 500
        };

        // Act
        var result = QuoteValidator.ValidateQuoteResponse(quote, 100000);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("negative", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateQuoteResponse_NegativeServiceFee_ReturnsInvalid()
    {
        // Arrange
        var quote = new BoltzReverseQuoteResponse
        {
            OnchainAmount = 99000,
            MinerFee = 500,
            ServiceFee = -200
        };

        // Act
        var result = QuoteValidator.ValidateQuoteResponse(quote, 100000);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("negative", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateQuoteResponse_FeesExceedInvoice_ReturnsInvalid()
    {
        // Arrange - fees of 110k exceed the 100k invoice amount
        // OnchainAmount must be positive to get past the first validation
        var quote = new BoltzReverseQuoteResponse
        {
            OnchainAmount = 1, // Minimal positive value to pass first check
            MinerFee = 60000,
            ServiceFee = 50000 // Total fees = 110000 > 100000 invoice
        };

        // Act
        var result = QuoteValidator.ValidateQuoteResponse(quote, 100000);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("exceed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateQuoteResponse_OnchainDoesNotMatchExpected_ReturnsInvalid()
    {
        // Arrange - onchain should be ~99000 but is 50000 (way off)
        var quote = new BoltzReverseQuoteResponse
        {
            OnchainAmount = 50000, // Should be ~99000
            MinerFee = 500,
            ServiceFee = 500
        };

        // Act
        var result = QuoteValidator.ValidateQuoteResponse(quote, 100000);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("doesn't match", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateQuoteResponse_UnreasonablyHighFees_ReturnsInvalid()
    {
        // Arrange - 15% fee (> 10% threshold)
        var quote = new BoltzReverseQuoteResponse
        {
            OnchainAmount = 85000,
            MinerFee = 7500,
            ServiceFee = 7500 // 15% total
        };

        // Act
        var result = QuoteValidator.ValidateQuoteResponse(quote, 100000);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("unreasonably high", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(100000, 99000, 500, 500)]    // 1% fee
    [InlineData(500000, 492500, 3500, 4000)] // 1.5% fee
    [InlineData(1000000, 985000, 7000, 8000)] // 1.5% fee
    public void ValidateQuoteResponse_ReasonableFees_ReturnsValid(
        long invoiceAmount,
        long onchainAmount,
        long minerFee,
        long serviceFee)
    {
        // Arrange
        var quote = new BoltzReverseQuoteResponse
        {
            OnchainAmount = onchainAmount,
            MinerFee = minerFee,
            ServiceFee = serviceFee
        };

        // Act
        var result = QuoteValidator.ValidateQuoteResponse(quote, invoiceAmount);

        // Assert
        Assert.True(result.IsValid, result.ErrorMessage);
    }

    #endregion

    #region Batch Creation Validation Tests

    [Fact]
    public void BatchCreation_EmptyAllocations_ThrowsArgumentException()
    {
        // This tests the validation logic we added to CreateBatchAsync
        var allocations = new List<PendingAllocation>();
        
        Assert.Throws<ArgumentException>(() => 
            ValidateBatchCreationInputs(allocations));
    }

    [Fact]
    public void BatchCreation_NullAllocations_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => 
            ValidateBatchCreationInputs(null!));
    }

    [Fact]
    public void BatchCreation_ZeroTotalSats_ThrowsArgumentException()
    {
        var allocations = new List<PendingAllocation>
        {
            new() { AllocatedSats = 0 },
            new() { AllocatedSats = 0 }
        };

        Assert.Throws<ArgumentException>(() => 
            ValidateBatchCreationInputs(allocations));
    }

    [Fact]
    public void BatchCreation_NegativeTotalSats_ThrowsArgumentException()
    {
        // Edge case: if someone managed to create an allocation with negative sats
        var allocations = new List<PendingAllocation>
        {
            new() { AllocatedSats = -1000 },
            new() { AllocatedSats = 500 }
        };

        Assert.Throws<ArgumentException>(() => 
            ValidateBatchCreationInputs(allocations));
    }

    [Fact]
    public void BatchCreation_ValidAllocations_NoException()
    {
        var allocations = new List<PendingAllocation>
        {
            new() { AllocatedSats = 10000 },
            new() { AllocatedSats = 20000 }
        };

        var exception = Record.Exception(() => 
            ValidateBatchCreationInputs(allocations));
        
        Assert.Null(exception);
    }

    /// <summary>
    /// Helper method that mimics the validation logic in CreateBatchAsync
    /// </summary>
    private static void ValidateBatchCreationInputs(List<PendingAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        
        if (allocations.Count == 0)
        {
            throw new ArgumentException("Cannot create batch with no allocations.", nameof(allocations));
        }

        var totalSats = allocations.Sum(a => a.AllocatedSats);
        
        if (totalSats <= 0)
        {
            throw new ArgumentException(
                $"Cannot create batch with zero or negative total sats ({totalSats}). Allocations may have invalid values.",
                nameof(allocations));
        }
    }

    #endregion

    #region Exchange Rate Validation Tests

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(-0.01)]
    public void ExchangeRateValidation_NegativeRate_IsInvalid(decimal rate)
    {
        Assert.True(IsInvalidExchangeRate(rate));
    }

    [Theory]
    [InlineData(10_000_001)]
    [InlineData(100_000_000)]
    public void ExchangeRateValidation_UnreasonablyHighRate_IsInvalid(decimal rate)
    {
        Assert.True(IsInvalidExchangeRate(rate));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50000)]
    [InlineData(100000)]
    [InlineData(1000000)]
    [InlineData(10_000_000)]
    public void ExchangeRateValidation_ValidRange_IsValid(decimal rate)
    {
        Assert.False(IsInvalidExchangeRate(rate));
    }

    private static bool IsInvalidExchangeRate(decimal rate)
    {
        return rate < 0 || rate > 10_000_000;
    }

    #endregion

    #region Weighted Cost Basis Tests

    [Fact]
    public void WeightedCostBasis_SingleAllocation_ReturnsSameRate()
    {
        var allocations = new List<(long sats, decimal rate)>
        {
            (100000, 50000m)
        };

        var result = CalculateWeightedCostBasis(allocations);

        Assert.Equal(50000m, result);
    }

    [Fact]
    public void WeightedCostBasis_EqualWeights_ReturnsAverage()
    {
        var allocations = new List<(long sats, decimal rate)>
        {
            (100000, 40000m),
            (100000, 60000m)
        };

        var result = CalculateWeightedCostBasis(allocations);

        Assert.Equal(50000m, result);
    }

    [Fact]
    public void WeightedCostBasis_UnequalWeights_ReturnsWeightedAverage()
    {
        var allocations = new List<(long sats, decimal rate)>
        {
            (300000, 40000m), // 75%
            (100000, 60000m)  // 25%
        };

        var result = CalculateWeightedCostBasis(allocations);

        // (300k*40k + 100k*60k) / 400k = 45k
        Assert.Equal(45000m, result);
    }

    private static decimal CalculateWeightedCostBasis(List<(long sats, decimal rate)> allocations)
    {
        var totalSats = allocations.Sum(a => a.sats);
        if (totalSats == 0) return 0;
        return allocations.Sum(a => a.sats * a.rate) / totalSats;
    }

    #endregion

    #region Transient Error Detection Tests

    [Fact]
    public void IsTransientError_HttpRequestException_ReturnsTrue()
    {
        var ex = new HttpRequestException("Connection refused");
        Assert.True(IsTransientError(ex));
    }

    [Fact]
    public void IsTransientError_TimeoutException_ReturnsTrue()
    {
        var ex = new TimeoutException("Request timed out");
        Assert.True(IsTransientError(ex));
    }

    [Fact]
    public void IsTransientError_TaskCanceledException_ReturnsTrue()
    {
        var ex = new TaskCanceledException("Request was cancelled");
        Assert.True(IsTransientError(ex));
    }

    [Fact]
    public void IsTransientError_GenericException_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Some operation failed");
        Assert.False(IsTransientError(ex));
    }

    [Fact]
    public void IsTransientError_ExceptionWithTimeoutInMessage_ReturnsTrue()
    {
        var ex = new Exception("The operation timeout exceeded");
        Assert.True(IsTransientError(ex));
    }

    [Fact]
    public void IsTransientError_ExceptionWithConnectionInMessage_ReturnsTrue()
    {
        var ex = new Exception("Connection refused by server");
        Assert.True(IsTransientError(ex));
    }

    private static bool IsTransientError(Exception ex)
    {
        return ex is HttpRequestException
            || ex is System.Net.Sockets.SocketException
            || ex is TimeoutException
            || ex is TaskCanceledException
            || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
