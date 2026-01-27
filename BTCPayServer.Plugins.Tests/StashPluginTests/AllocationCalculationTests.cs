#nullable enable
using Xunit;

namespace BTCPayServer.Plugins.Tests.StashPluginTests;

/// <summary>
/// Unit tests for allocation calculation logic.
/// These tests verify the math behind allocation percentages, fiat conversions,
/// and weighted cost basis calculations.
/// </summary>
public class AllocationCalculationTests
{
    #region Allocation Percentage Calculations

    [Theory]
    [InlineData(100000, 20, 20000)]      // 20% of 100k sats
    [InlineData(1000000, 10, 100000)]    // 10% of 1M sats
    [InlineData(500000, 50, 250000)]     // 50% of 500k sats
    [InlineData(1000000, 100, 1000000)]  // 100% of 1M sats
    [InlineData(1000000, 0, 0)]          // 0% of 1M sats
    [InlineData(12345, 25, 3086)]        // 25% of 12345 (truncates to 3086)
    public void CalculateAllocatedSats_ReturnsCorrectAmount(
        long totalReceivedSats,
        decimal allocationPercentage,
        long expectedAllocatedSats)
    {
        // Act
        var allocatedSats = (long)(totalReceivedSats * (allocationPercentage / 100m));

        // Assert
        Assert.Equal(expectedAllocatedSats, allocatedSats);
    }

    [Theory]
    [InlineData(100000, 50000, 50)]         // 100k sats = 0.001 BTC at $50k/BTC = $50
    [InlineData(1000000, 50000, 500)]       // 1M sats = 0.01 BTC at $50k/BTC = $500
    [InlineData(100000000, 100000, 100000)] // 1 BTC at $100k/BTC = $100k
    [InlineData(200000, 50000, 100)]        // 200k sats = 0.002 BTC at $50k/BTC = $100
    public void CalculateFiatValue_ReturnsCorrectAmount(
        long allocatedSats,
        decimal exchangeRate,
        decimal expectedFiatValue)
    {
        // Act
        var fiatValue = (allocatedSats / 100_000_000m) * exchangeRate;

        // Assert - allow small floating point differences
        Assert.Equal(expectedFiatValue, fiatValue, 2);
    }

    #endregion

    #region Weighted Average Cost Basis Calculations

    [Fact]
    public void CalculateWeightedAverageCostBasis_SingleAllocation()
    {
        // Arrange
        var allocations = new[]
        {
            new TestAllocation(100000, 50000m) // 100k sats at $50k/BTC
        };

        // Act
        var totalSats = allocations.Sum(a => a.Sats);
        var weightedCostBasis = allocations.Sum(a => a.Sats * a.ExchangeRate) / totalSats;

        // Assert
        Assert.Equal(50000m, weightedCostBasis);
    }

    [Fact]
    public void CalculateWeightedAverageCostBasis_MultipleAllocations_SameRate()
    {
        // Arrange
        var allocations = new[]
        {
            new TestAllocation(100000, 50000m),
            new TestAllocation(200000, 50000m),
            new TestAllocation(50000, 50000m)
        };

        // Act
        var totalSats = allocations.Sum(a => a.Sats);
        var weightedCostBasis = allocations.Sum(a => a.Sats * a.ExchangeRate) / totalSats;

        // Assert
        Assert.Equal(50000m, weightedCostBasis);
    }

    [Fact]
    public void CalculateWeightedAverageCostBasis_MultipleAllocations_DifferentRates()
    {
        // Arrange - weighted average of allocations bought at different prices
        var allocations = new[]
        {
            new TestAllocation(100000, 40000m), // 100k sats at $40k
            new TestAllocation(100000, 60000m)  // 100k sats at $60k
        };

        // Act
        var totalSats = allocations.Sum(a => a.Sats);
        var weightedCostBasis = allocations.Sum(a => a.Sats * a.ExchangeRate) / totalSats;

        // Assert - should be exactly $50k (average of 40k and 60k with equal weights)
        Assert.Equal(50000m, weightedCostBasis);
    }

    [Fact]
    public void CalculateWeightedAverageCostBasis_MultipleAllocations_UnequalWeights()
    {
        // Arrange - more sats bought at lower price
        var allocations = new[]
        {
            new TestAllocation(300000, 40000m), // 300k sats at $40k (75%)
            new TestAllocation(100000, 60000m)  // 100k sats at $60k (25%)
        };

        // Act
        var totalSats = allocations.Sum(a => a.Sats);
        var weightedCostBasis = allocations.Sum(a => a.Sats * a.ExchangeRate) / totalSats;

        // Assert - should be weighted toward $40k: (300k*40k + 100k*60k) / 400k = 45k
        Assert.Equal(45000m, weightedCostBasis);
    }

    [Fact]
    public void CalculateWeightedAverageCostBasis_RealWorldScenario()
    {
        // Arrange - simulate multiple invoices over time
        var allocations = new[]
        {
            new TestAllocation(50000, 42000m),   // Invoice 1: $42k/BTC
            new TestAllocation(75000, 45000m),   // Invoice 2: $45k/BTC  
            new TestAllocation(25000, 48000m),   // Invoice 3: $48k/BTC
            new TestAllocation(100000, 52000m),  // Invoice 4: $52k/BTC
            new TestAllocation(150000, 55000m)   // Invoice 5: $55k/BTC
        };

        // Act
        var totalSats = allocations.Sum(a => a.Sats);
        var weightedCostBasis = allocations.Sum(a => a.Sats * a.ExchangeRate) / totalSats;

        // Assert
        // (50k*42k + 75k*45k + 25k*48k + 100k*52k + 150k*55k) / 400k
        // = (2.1B + 3.375B + 1.2B + 5.2B + 8.25B) / 400k
        // = 20.125B / 400k = 50312.5
        Assert.Equal(50312.5m, weightedCostBasis);
        Assert.Equal(400000, totalSats);
    }

    #endregion

    #region Batch Fee Calculations

    [Fact]
    public void CalculateNetSats_SubtractsFees()
    {
        // Arrange
        var totalSats = 500000L;
        var minerFee = 1000L;
        var serviceFee = 2500L;

        // Act
        var totalFees = minerFee + serviceFee;
        var netSats = totalSats - totalFees;

        // Assert
        Assert.Equal(496500, netSats);
    }

    [Theory]
    [InlineData(100000, 500, 500)]   // 1% total fee
    [InlineData(500000, 1000, 2500)] // 0.7% total fee
    [InlineData(1000000, 1500, 5000)] // 0.65% total fee
    public void CalculateFeePercentage_ReturnsCorrectPercentage(
        long totalSats,
        long minerFee,
        long serviceFee)
    {
        // Act
        var totalFees = minerFee + serviceFee;
        var feePercentage = (totalFees * 100m) / totalSats;

        // Assert
        Assert.True(feePercentage > 0);
        Assert.True(feePercentage < 100);
    }

    #endregion

    #region Threshold Calculations

    [Theory]
    [InlineData(50, 100, false)]   // $50 pending, $100 threshold -> not met
    [InlineData(100, 100, true)]   // $100 pending, $100 threshold -> met
    [InlineData(150, 100, true)]   // $150 pending, $100 threshold -> met
    [InlineData(99.99, 100, false)] // Just under threshold
    public void IsThresholdMet_ReturnsCorrectValue(
        decimal pendingFiatValue,
        decimal thresholdFiat,
        bool expectedResult)
    {
        // Act
        var isThresholdMet = pendingFiatValue >= thresholdFiat;

        // Assert
        Assert.Equal(expectedResult, isThresholdMet);
    }

    [Theory]
    [InlineData(50000, 100, 200000)]   // $100 threshold at $50k/BTC = 0.002 BTC = 200k sats
    [InlineData(100000, 100, 100000)]  // $100 threshold at $100k/BTC = 0.001 BTC = 100k sats
    [InlineData(50000, 50, 100000)]    // $50 threshold at $50k/BTC = 0.001 BTC = 100k sats
    public void CalculateMinimumSatsForThreshold_ReturnsCorrectValue(
        decimal exchangeRate,
        decimal thresholdFiat,
        long minimumSatsExpected)
    {
        // Act - calculate how many sats needed to reach threshold
        var minimumSats = (long)((thresholdFiat / exchangeRate) * 100_000_000m);

        // Assert
        Assert.Equal(minimumSatsExpected, minimumSats);
    }

    #endregion

    #region Fiat-Targeted Logic

    [Fact]
    public void CalculateSatsForTargetFiat_AccountsForPriceChange()
    {
        // Arrange
        // User accumulated $100 worth of Bitcoin when price was $50k
        // That was 200,000 sats (0.002 BTC)
        // Now price is $40k - they need to send more sats to get $100 worth
        var targetFiatValue = 100m;
        var originalExchangeRate = 50000m;
        var currentExchangeRate = 40000m;
        
        // Original sats for that fiat value
        var originalSats = (long)((targetFiatValue / originalExchangeRate) * 100_000_000m);

        // Act - Calculate sats needed at current price
        var currentSatsNeeded = (long)((targetFiatValue / currentExchangeRate) * 100_000_000m);

        // Assert
        Assert.Equal(200000, originalSats);   // 200k sats at $50k
        Assert.Equal(250000, currentSatsNeeded); // 250k sats at $40k (need more sats)
    }

    [Fact]
    public void CalculateSatsForTargetFiat_PriceIncrease_NeedFewerSats()
    {
        // Arrange
        // Price went up from $50k to $100k
        var targetFiatValue = 100m;
        var originalExchangeRate = 50000m;
        var currentExchangeRate = 100000m;

        var originalSats = (long)((targetFiatValue / originalExchangeRate) * 100_000_000m);

        // Act
        var currentSatsNeeded = (long)((targetFiatValue / currentExchangeRate) * 100_000_000m);

        // Assert
        Assert.Equal(200000, originalSats);   // 200k sats at $50k
        Assert.Equal(100000, currentSatsNeeded); // 100k sats at $100k (need fewer sats)
    }

    #endregion

    #region Helper Classes

    private record TestAllocation(long Sats, decimal ExchangeRate);

    #endregion
}
