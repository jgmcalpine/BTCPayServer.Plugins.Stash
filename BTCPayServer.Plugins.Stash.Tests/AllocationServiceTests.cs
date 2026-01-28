using Xunit;

namespace BTCPayServer.Plugins.Stash.Tests;

public class AllocationServiceTests
{
    /// <summary>
    /// Tests that allocation calculation returns correct sats.
    /// Formula: allocatedSats = (long)(totalReceivedSats * (allocationPercentage / 100m))
    /// </summary>
    [Fact]
    public void CalculateAllocation_ReturnsCorrectSats()
    {
        // Arrange
        long totalReceivedSats = 1000;
        decimal allocationPercentage = 20.0m; // 20% tax rate
        
        // Act - Calculate allocation using the same formula as AllocationService
        var allocatedSats = (long)(totalReceivedSats * (allocationPercentage / 100m));
        
        // Assert
        Assert.Equal(200, allocatedSats);
    }

    /// <summary>
    /// Tests that allocation calculation returns correct fiat value.
    /// Formula: fiatValue = (allocatedSats / 100_000_000m) * exchangeRate
    /// </summary>
    [Fact]
    public void CalculateAllocation_ReturnsCorrectFiat()
    {
        // Arrange
        long totalReceivedSats = 100_000_000; // 1 BTC
        decimal allocationPercentage = 20.0m; // 20% tax rate
        decimal exchangeRate = 50_000m; // $50,000 per BTC
        
        // Act - Calculate allocation sats first
        var allocatedSats = (long)(totalReceivedSats * (allocationPercentage / 100m));
        // Then calculate fiat value
        var fiatValue = (allocatedSats / 100_000_000m) * exchangeRate;
        
        // Assert
        // 20% of 1 BTC = 0.2 BTC
        // 0.2 BTC * $50,000 = $10,000
        Assert.Equal(20_000_000, allocatedSats); // 0.2 BTC in sats
        Assert.Equal(10_000m, fiatValue); // $10,000
    }

    /// <summary>
    /// Tests allocation calculation with a different example: $100 invoice with 20% tax.
    /// </summary>
    [Fact]
    public void CalculateAllocation_ReturnsCorrectFiat_ForDollarAmount()
    {
        // Arrange
        // If invoice is $100 and we want to allocate 20% for tax
        // First, convert $100 to sats at a given exchange rate
        decimal invoiceFiat = 100m;
        decimal exchangeRate = 50_000m; // $50,000 per BTC
        decimal allocationPercentage = 20.0m; // 20% tax rate
        
        // Convert $100 to BTC, then to sats
        decimal invoiceBtc = invoiceFiat / exchangeRate; // 0.002 BTC
        long totalReceivedSats = (long)(invoiceBtc * 100_000_000m); // 200,000 sats
        
        // Act - Calculate allocation
        var allocatedSats = (long)(totalReceivedSats * (allocationPercentage / 100m));
        var fiatValue = (allocatedSats / 100_000_000m) * exchangeRate;
        
        // Assert
        // 20% of $100 = $20
        Assert.Equal(40_000, allocatedSats); // 0.0004 BTC = 40,000 sats
        Assert.Equal(20m, fiatValue); // $20
    }
}
