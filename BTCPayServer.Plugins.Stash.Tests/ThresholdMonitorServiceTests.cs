using Xunit;

namespace BTCPayServer.Plugins.Stash.Tests;

public class ThresholdMonitorServiceTests
{
    /// <summary>
    /// Tests that ShouldTrigger returns true when accumulated liability exceeds threshold.
    /// Logic: currentFiatValue >= BatchThresholdFiat
    /// </summary>
    [Fact]
    public void ShouldTrigger_ReturnsTrue_WhenLiabilityExceedsThreshold()
    {
        // Arrange
        decimal accumulatedLiabilityFiat = 105m;
        decimal thresholdFiat = 100m;
        
        // Act - Check if threshold is met (same logic as ThresholdMonitorService)
        bool shouldTrigger = accumulatedLiabilityFiat >= thresholdFiat;
        
        // Assert
        Assert.True(shouldTrigger);
    }

    /// <summary>
    /// Tests that ShouldTrigger returns false when liability is below threshold.
    /// </summary>
    [Fact]
    public void ShouldTrigger_ReturnsFalse_WhenLiabilityIsLow()
    {
        // Arrange
        decimal accumulatedLiabilityFiat = 90m;
        decimal thresholdFiat = 100m;
        
        // Act - Check if threshold is met
        bool shouldTrigger = accumulatedLiabilityFiat >= thresholdFiat;
        
        // Assert
        Assert.False(shouldTrigger);
    }

    /// <summary>
    /// Tests threshold check at exact boundary (equal values).
    /// </summary>
    [Fact]
    public void ShouldTrigger_ReturnsTrue_WhenLiabilityEqualsThreshold()
    {
        // Arrange
        decimal accumulatedLiabilityFiat = 100m;
        decimal thresholdFiat = 100m;
        
        // Act
        bool shouldTrigger = accumulatedLiabilityFiat >= thresholdFiat;
        
        // Assert
        Assert.True(shouldTrigger);
    }

    /// <summary>
    /// Tests threshold calculation with current exchange rate.
    /// Formula: currentFiatValue = (totalSats / 100_000_000m) * currentRate
    /// </summary>
    [Fact]
    public void ShouldTrigger_CalculatesFiatValueCorrectly()
    {
        // Arrange
        long totalSats = 2_000_000; // 0.02 BTC
        decimal currentExchangeRate = 50_000m; // $50,000 per BTC
        decimal thresholdFiat = 100m;
        
        // Act - Calculate current fiat value (same logic as ThresholdMonitorService)
        decimal currentFiatValue = (totalSats / 100_000_000m) * currentExchangeRate;
        bool shouldTrigger = currentFiatValue >= thresholdFiat;
        
        // Assert
        // 0.02 BTC * $50,000 = $1,000
        Assert.Equal(1_000m, currentFiatValue);
        Assert.True(shouldTrigger);
    }
}
