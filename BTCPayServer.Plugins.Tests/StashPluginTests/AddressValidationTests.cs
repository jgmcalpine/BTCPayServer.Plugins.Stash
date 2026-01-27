#nullable enable
using BTCPayServer.Plugins.Stash.Data.Models;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Plugins.Tests.StashPluginTests;

/// <summary>
/// Unit tests for address validation logic.
/// Tests Bitcoin and Liquid address validation across mainnet, testnet, and regtest.
/// </summary>
public class AddressValidationTests
{
    #region Bitcoin Mainnet Address Validation

    [Theory]
    [InlineData("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4")] // Bech32
    [InlineData("bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq")] // Bech32
    [InlineData("1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2")] // P2PKH
    [InlineData("3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy")] // P2SH
    public void ValidateBitcoinAddressForNetwork_AcceptsValidMainnetAddresses(string address)
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);

        // Act
        var result = validator.ValidateBitcoinAddressForNetwork(address);

        // Assert
        Assert.True(result.IsValid, $"Expected {address} to be valid on mainnet");
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData("tb1qw508d6qejxtdg4y5r3zarvary0c5xw7kxpjzsx")] // Testnet bech32
    [InlineData("mzBc4XEFSdzCDcTxAgf6EZXgsZWpztRhef")] // Testnet P2PKH
    [InlineData("2MzQwSSnBHWHqSAqtTVQ6v47XtaisrJa1Vc")] // Testnet P2SH
    public void ValidateBitcoinAddressForNetwork_RejectsTestnetAddressesOnMainnet(string address)
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);

        // Act
        var result = validator.ValidateBitcoinAddressForNetwork(address);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("testnet", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateBitcoinAddressForNetwork_RejectsRegtestAddressOnMainnet()
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);
        var regtestAddress = "bcrt1qcpf40xrswnmtsugt3xwpd4mrh8cv872jm3l2je";

        // Act
        var result = validator.ValidateBitcoinAddressForNetwork(regtestAddress);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("regtest", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Bitcoin Testnet Address Validation

    [Theory]
    [InlineData("tb1qw508d6qejxtdg4y5r3zarvary0c5xw7kxpjzsx")] // Bech32
    [InlineData("mzBc4XEFSdzCDcTxAgf6EZXgsZWpztRhef")] // P2PKH (m prefix)
    [InlineData("n3ZddxzLvAY9o7184TB4c6FJasAybsw4HZ")] // P2PKH (n prefix)
    [InlineData("2MzQwSSnBHWHqSAqtTVQ6v47XtaisrJa1Vc")] // P2SH
    public void ValidateBitcoinAddressForNetwork_AcceptsValidTestnetAddresses(string address)
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Testnet);

        // Act
        var result = validator.ValidateBitcoinAddressForNetwork(address);

        // Assert
        Assert.True(result.IsValid, $"Expected {address} to be valid on testnet");
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4")] // Mainnet bech32
    [InlineData("1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2")] // Mainnet P2PKH
    public void ValidateBitcoinAddressForNetwork_RejectsMainnetAddressesOnTestnet(string address)
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Testnet);

        // Act
        var result = validator.ValidateBitcoinAddressForNetwork(address);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("mainnet", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Bitcoin Regtest Address Validation

    [Theory]
    [InlineData("bcrt1qcpf40xrswnmtsugt3xwpd4mrh8cv872jm3l2je")] // Bech32
    [InlineData("bcrt1qw508d6qejxtdg4y5r3zarvary0c5xw7kygt080")] // Bech32
    [InlineData("mzBc4XEFSdzCDcTxAgf6EZXgsZWpztRhef")] // P2PKH (m prefix - shared with testnet)
    [InlineData("2MzQwSSnBHWHqSAqtTVQ6v47XtaisrJa1Vc")] // P2SH (2 prefix - shared with testnet)
    public void ValidateBitcoinAddressForNetwork_AcceptsValidRegtestAddresses(string address)
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Regtest);

        // Act
        var result = validator.ValidateBitcoinAddressForNetwork(address);

        // Assert
        Assert.True(result.IsValid, $"Expected {address} to be valid on regtest");
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4")] // Mainnet bech32
    [InlineData("1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2")] // Mainnet P2PKH
    public void ValidateBitcoinAddressForNetwork_RejectsMainnetAddressesOnRegtest(string address)
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Regtest);

        // Act
        var result = validator.ValidateBitcoinAddressForNetwork(address);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("mainnet", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region XPUB Validation

    [Theory]
    [InlineData("xpub661MyMwAqRbcFtXgS5sYJABqqG9YLmC4Q1Rdap9gSE8NqtwybGhePY2gZ29ESFjqJoCu1Rupje8YtGqsefD265TMg7usUDFdp6W1EGMcet8")]
    [InlineData("ypub6Ww3ibxVfGzLrAH1PNcjyAWenMTbbAosGNB6VvmSEgytSER9azLDWCxoJwW7Ke7icmizBMXrzBx9979FfaHxHcrArf3zbeJJJUZPf663zsP")]
    [InlineData("zpub6rFR7y4Q2AijBEqTUquhVz398htDFrtymD9xYYfG1m4wAcvPhXNfE3EfH1r1ADqtfSdVCToUG868RvUUkgDKf31mGDtKsAYz2oz2AGutZYs")]
    public void ValidateBitcoinAddressForNetwork_AcceptsXpubOnAllNetworks(string xpub)
    {
        // Test on all networks - XPUBs are network-agnostic
        foreach (var network in new[] { ChainName.Mainnet, ChainName.Testnet, ChainName.Regtest })
        {
            // Arrange
            var validator = TestHelpers.CreateAddressValidator(network);

            // Act
            var result = validator.ValidateBitcoinAddressForNetwork(xpub);

            // Assert
            Assert.True(result.IsValid, $"Expected XPUB {xpub[..20]}... to be valid on {network}");
        }
    }

    [Fact]
    public void ValidateXpub_AcceptsValidXpubs()
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);
        var xpub = "xpub661MyMwAqRbcFtXgS5sYJABqqG9YLmC4Q1Rdap9gSE8NqtwybGhePY2gZ29ESFjqJoCu1Rupje8YtGqsefD265TMg7usUDFdp6W1EGMcet8";

        // Act
        var result = validator.ValidateXpub(xpub);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-xpub")]
    [InlineData("xpub123")] // Too short
    public void ValidateXpub_RejectsInvalidXpubs(string xpub)
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);

        // Act
        var result = validator.ValidateXpub(xpub);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Liquid Address Validation

    [Theory]
    [InlineData("ex1q2wsxp8k7l8skxqt6v7kj0c7ylxs7w6w5yjxf6l")] // Mainnet blech32
    [InlineData("lq1qqgp8k7l8skxqt6v7kj0c7ylxs7w6w5yjxf6l0l")] // Mainnet blech32m
    public void ValidateLiquidAddressForNetwork_AcceptsValidMainnetAddresses(string address)
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);

        // Act
        var result = validator.ValidateLiquidAddressForNetwork(address);

        // Assert
        Assert.True(result.IsValid, $"Expected {address} to be valid Liquid mainnet address");
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData("tex1q2wsxp8k7l8skxqt6v7kj0c7ylxs7w6w5yjxf6l")] // Testnet blech32
    [InlineData("tlq1qqgp8k7l8skxqt6v7kj0c7ylxs7w6w5yjxf6l0l")] // Testnet blech32m
    [InlineData("ert1qtest1234567890abcdefghijklmnop")] // Regtest
    public void ValidateLiquidAddressForNetwork_AcceptsValidTestnetAddresses(string address)
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Testnet);

        // Act
        var result = validator.ValidateLiquidAddressForNetwork(address);

        // Assert
        Assert.True(result.IsValid, $"Expected {address} to be valid Liquid testnet address");
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateLiquidAddressForNetwork_RejectsTestnetAddressOnMainnet()
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);
        var testnetAddress = "tex1q2wsxp8k7l8skxqt6v7kj0c7ylxs7w6w5yjxf6l";

        // Act
        var result = validator.ValidateLiquidAddressForNetwork(testnetAddress);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("testnet", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateLiquidAddressForNetwork_RejectsMainnetAddressOnTestnet()
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Testnet);
        var mainnetAddress = "ex1q2wsxp8k7l8skxqt6v7kj0c7ylxs7w6w5yjxf6l";

        // Act
        var result = validator.ValidateLiquidAddressForNetwork(mainnetAddress);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("mainnet", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Empty/Invalid Input Handling

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateBitcoinAddressForNetwork_RejectsEmptyInput(string? address)
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);

        // Act
        var result = validator.ValidateBitcoinAddressForNetwork(address!);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateLiquidAddressForNetwork_RejectsEmptyInput(string? address)
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);

        // Act
        var result = validator.ValidateLiquidAddressForNetwork(address!);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("definitely-not-an-address")]
    [InlineData("123456789")]
    [InlineData("hello world")]
    public void ValidateBitcoinAddressForNetwork_RejectsGarbageInput(string address)
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);

        // Act
        var result = validator.ValidateBitcoinAddressForNetwork(address);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Invalid", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Destination Validation for Execution

    [Fact]
    public void ValidateDestinationForExecution_ValidatesCorrectAddressType_ForColdStorage()
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);
        var settings = new StashSettings
        {
            DestinationType = StashDestinationType.ColdStorage,
            DestinationAddress = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4"
        };

        // Act
        var result = validator.ValidateDestinationForExecution(settings);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateDestinationForExecution_ValidatesCorrectAddressType_ForLiquidSwap()
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);
        var settings = new StashSettings
        {
            DestinationType = StashDestinationType.LiquidSwap,
            LiquidAddress = "ex1q2wsxp8k7l8skxqt6v7kj0c7ylxs7w6w5yjxf6l"
        };

        // Act
        var result = validator.ValidateDestinationForExecution(settings);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateDestinationForExecution_FailsWithNoAddress_ForColdStorage()
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);
        var settings = new StashSettings
        {
            DestinationType = StashDestinationType.ColdStorage,
            DestinationAddress = null
        };

        // Act
        var result = validator.ValidateDestinationForExecution(settings);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("destination address", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateDestinationForExecution_FailsWithNoAddress_ForLiquidSwap()
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);
        var settings = new StashSettings
        {
            DestinationType = StashDestinationType.LiquidSwap,
            LiquidAddress = null
        };

        // Act
        var result = validator.ValidateDestinationForExecution(settings);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Liquid address", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateDestinationForExecution_FailsWithWrongNetworkAddress()
    {
        // Arrange
        var validator = TestHelpers.CreateAddressValidator(ChainName.Mainnet);
        var settings = new StashSettings
        {
            DestinationType = StashDestinationType.ColdStorage,
            DestinationAddress = "tb1qw508d6qejxtdg4y5r3zarvary0c5xw7kxpjzsx" // Testnet address
        };

        // Act
        var result = validator.ValidateDestinationForExecution(settings);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("testnet", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
