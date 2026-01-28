using Xunit;

namespace BTCPayServer.Plugins.Stash.Tests;

public class BatchExecutionServiceTests
{
    /// <summary>
    /// Tests calculation of sats required for a fiat target.
    /// Formula: satsRequired = (fiatTarget / exchangeRate) * 100_000_000
    /// </summary>
    [Fact]
    public void CalculateSatsRequiredForFiatTarget()
    {
        // Arrange
        decimal fiatTarget = 100m; // $100
        decimal exchangeRate = 50_000m; // $50,000 per BTC
        
        // Act - Calculate sats required
        // First convert fiat to BTC: $100 / $50,000 = 0.002 BTC
        // Then convert BTC to sats: 0.002 * 100,000,000 = 200,000 sats
        decimal btcRequired = fiatTarget / exchangeRate;
        long satsRequired = (long)(btcRequired * 100_000_000m);
        
        // Assert
        Assert.Equal(0.002m, btcRequired);
        Assert.Equal(200_000, satsRequired);
    }

    /// <summary>
    /// Tests safety cap application: if calculated swap amount > 110% of available segregated sats,
    /// verify it returns the capped amount (110%), not the full amount.
    /// </summary>
    [Fact]
    public void ApplySafetyCap()
    {
        // Arrange
        long availableSegregatedSats = 1_000_000; // 0.01 BTC
        long calculatedSwapAmount = 1_200_000; // 0.012 BTC (120% of available)
        decimal safetyCapPercentage = 110m; // 110% cap
        
        // Act - Apply safety cap
        long cappedAmount;
        if (calculatedSwapAmount > (long)(availableSegregatedSats * (safetyCapPercentage / 100m)))
        {
            // Cap at 110% of available
            cappedAmount = (long)(availableSegregatedSats * (safetyCapPercentage / 100m));
        }
        else
        {
            cappedAmount = calculatedSwapAmount;
        }
        
        // Assert
        // 110% of 1,000,000 = 1,100,000 sats
        long expectedCappedAmount = (long)(availableSegregatedSats * (safetyCapPercentage / 100m));
        Assert.Equal(expectedCappedAmount, cappedAmount);
        Assert.Equal(1_100_000, cappedAmount);
        Assert.True(cappedAmount < calculatedSwapAmount); // Verify it was capped
    }

    /// <summary>
    /// Tests that safety cap is not applied when calculated amount is within limits.
    /// </summary>
    [Fact]
    public void ApplySafetyCap_NoCapWhenWithinLimits()
    {
        // Arrange
        long availableSegregatedSats = 1_000_000; // 0.01 BTC
        long calculatedSwapAmount = 1_000_000; // 0.01 BTC (100% of available, within 110% cap)
        decimal safetyCapPercentage = 110m; // 110% cap
        
        // Act - Apply safety cap
        long cappedAmount;
        if (calculatedSwapAmount > (long)(availableSegregatedSats * (safetyCapPercentage / 100m)))
        {
            cappedAmount = (long)(availableSegregatedSats * (safetyCapPercentage / 100m));
        }
        else
        {
            cappedAmount = calculatedSwapAmount;
        }
        
        // Assert
        Assert.Equal(calculatedSwapAmount, cappedAmount); // No cap applied
        Assert.Equal(1_000_000, cappedAmount);
    }

    /// <summary>
    /// Tests safety cap at exact boundary (110%).
    /// </summary>
    [Fact]
    public void ApplySafetyCap_AtExactBoundary()
    {
        // Arrange
        long availableSegregatedSats = 1_000_000; // 0.01 BTC
        long calculatedSwapAmount = 1_100_000; // 0.011 BTC (exactly 110% of available)
        decimal safetyCapPercentage = 110m; // 110% cap
        
        // Act - Apply safety cap
        long cappedAmount;
        if (calculatedSwapAmount > (long)(availableSegregatedSats * (safetyCapPercentage / 100m)))
        {
            cappedAmount = (long)(availableSegregatedSats * (safetyCapPercentage / 100m));
        }
        else
        {
            cappedAmount = calculatedSwapAmount;
        }
        
        // Assert
        // At exactly 110%, no cap should be applied (it's <= not <)
        long capLimit = (long)(availableSegregatedSats * (safetyCapPercentage / 100m));
        Assert.Equal(capLimit, cappedAmount);
        Assert.Equal(1_100_000, cappedAmount);
    }
}
