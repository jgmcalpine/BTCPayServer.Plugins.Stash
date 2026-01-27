#nullable enable
using System;

namespace BTCPayServer.Plugins.Stash.Services;

/// <summary>
/// Validates Boltz quote responses to protect against malicious or buggy API data.
/// </summary>
public static class QuoteValidator
{
    /// <summary>
    /// Validates a Boltz quote response to ensure values are reasonable.
    /// Protects against malicious or buggy API responses.
    /// </summary>
    /// <param name="quote">The quote response from Boltz API.</param>
    /// <param name="invoiceAmount">The requested invoice amount in satoshis.</param>
    /// <returns>A validation result indicating if the quote is valid.</returns>
    public static QuoteValidationResult ValidateQuoteResponse(BoltzReverseQuoteResponse quote, long invoiceAmount)
    {
        // Check for negative or zero onchain amount
        if (quote.OnchainAmount <= 0)
        {
            return new QuoteValidationResult(false,
                $"Invalid quote: onchain amount ({quote.OnchainAmount}) must be positive.");
        }

        // Check for negative fees
        if (quote.MinerFee < 0 || quote.ServiceFee < 0)
        {
            return new QuoteValidationResult(false,
                $"Invalid quote: fees cannot be negative (miner: {quote.MinerFee}, service: {quote.ServiceFee}).");
        }

        // Check that fees don't exceed the invoice amount
        var totalFees = quote.MinerFee + quote.ServiceFee;
        if (totalFees >= invoiceAmount)
        {
            return new QuoteValidationResult(false,
                $"Invalid quote: total fees ({totalFees}) exceed invoice amount ({invoiceAmount}).");
        }

        // Check that onchain amount + fees roughly equals invoice amount (within 1% tolerance for rounding)
        var expectedOnchain = invoiceAmount - totalFees;
        var tolerance = invoiceAmount * 0.01m; // 1% tolerance
        if (Math.Abs(quote.OnchainAmount - expectedOnchain) > tolerance)
        {
            return new QuoteValidationResult(false,
                $"Invalid quote: onchain amount ({quote.OnchainAmount}) doesn't match expected ({expectedOnchain}) after fees.");
        }

        // Check for unreasonably high fee percentage (>10% is suspicious)
        var feePercentage = (totalFees * 100m) / invoiceAmount;
        if (feePercentage > 10)
        {
            return new QuoteValidationResult(false,
                $"Invalid quote: fee percentage ({feePercentage:F2}%) is unreasonably high.");
        }

        return new QuoteValidationResult(true, null);
    }
}
