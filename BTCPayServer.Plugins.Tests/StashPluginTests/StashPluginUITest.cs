using BTCPayServer.Tests;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests;

[Collection("Plugin Tests")]
[Trait("Category", "PlaywrightUITest")]
public class StashPluginUITest : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public StashPluginUITest(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null)
            _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }

    // [Fact]
    // public async Task CanConfigureStashSettingsAsync()
    // {
    //     await InitializePlaywright(ServerTester);
    //     var account = ServerTester.NewAccount();
    //     await account.GrantAccessAsync();
    //     await account.MakeAdmin(true);
    //
    //     await GoToUrl("/login");
    //     await LogIn(account.RegisterDetails.Email, account.RegisterDetails.Password);
    //
    //     var storeId = account.StoreId;
    //
    //     // Navigate to Stash plugin
    //     await GoToUrl($"/plugins/{storeId}/stash");
    //     
    //     // Should see the dashboard - initially no settings configured
    //     await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Info);
    //     TestLogs.LogInformation("✓ Stash dashboard loaded");
    //
    //     // Go to settings
    //     await GoToUrl($"/plugins/{storeId}/stash/settings");
    //     
    //     // Configure settings
    //     await Page.Locator("#IsEnabled").SetCheckedAsync(true);
    //     await Page.FillAsync("#AllocationPercentage", "20");
    //     await Page.FillAsync("#FiatCurrency", "USD");
    //     await Page.FillAsync("#BatchThresholdFiat", "100");
    //     await Page.FillAsync("#MinimumBatchSats", "100000");
    //     await Page.SelectOptionAsync("#DestinationType", "ColdStorage");
    //     await Page.FillAsync("#DestinationAddress", "bcrt1qcpf40xrswnmtsugt3xwpd4mrh8cv872jm3l2je");
    //     
    //     await Page.ClickAsync("button[type='submit']");
    //     await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);
    //     TestLogs.LogInformation("✓ Stash settings saved successfully");
    //
    //     // Verify settings persisted
    //     await GoToUrl($"/plugins/{storeId}/stash/settings");
    //     var allocationValue = await Page.InputValueAsync("#AllocationPercentage");
    //     Assert.Equal("20", allocationValue);
    //     TestLogs.LogInformation("✓ Settings persisted correctly");
    // }

    // [Fact]
    // public async Task CanViewDashboardWithPendingAllocationsAsync()
    // {
    //     await InitializePlaywright(ServerTester);
    //     var account = ServerTester.NewAccount();
    //     await account.GrantAccessAsync();
    //     await account.MakeAdmin(true);
    //
    //     await GoToUrl("/login");
    //     await LogIn(account.RegisterDetails.Email, account.RegisterDetails.Password);
    //
    //     // Set up wallet and invoice
    //     await ServerTester.ExplorerNode.GenerateAsync(1);
    //     var walletId = await account.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true);
    //     var storeId = account.StoreId;
    //
    //     // Configure Stash
    //     await GoToUrl($"/plugins/{storeId}/stash/settings");
    //     await Page.Locator("#IsEnabled").SetCheckedAsync(true);
    //     await Page.FillAsync("#AllocationPercentage", "20");
    //     await Page.FillAsync("#FiatCurrency", "USD");
    //     await Page.FillAsync("#BatchThresholdFiat", "1000"); // High threshold so no auto-execution
    //     await Page.FillAsync("#MinimumBatchSats", "100000");
    //     await Page.SelectOptionAsync("#DestinationType", "ColdStorage");
    //     await Page.FillAsync("#DestinationAddress", "bcrt1qcpf40xrswnmtsugt3xwpd4mrh8cv872jm3l2je");
    //     await Page.ClickAsync("button[type='submit']");
    //     await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);
    //     TestLogs.LogInformation("✓ Stash configured with high threshold");
    //
    //     // Create and pay an invoice
    //     var invoice = await account.BitPay.CreateInvoiceAsync(new Invoice(100, "USD"));
    //     await account.PayInvoiceAsync(invoice);
    //     await ServerTester.ExplorerNode.GenerateAsync(1);
    //     TestLogs.LogInformation("✓ Invoice paid");
    //
    //     // Check dashboard shows pending allocation
    //     await GoToUrl($"/plugins/{storeId}/stash");
    //     
    //     var pendingSats = Page.Locator("[data-testid='pending-sats']");
    //     var pendingText = await pendingSats.InnerTextAsync();
    //     Assert.NotEqual("0", pendingText);
    //     TestLogs.LogInformation($"✓ Dashboard shows pending sats: {pendingText}");
    // }

    // [Fact]
    // public async Task CanResetPendingAllocationsAsync()
    // {
    //     await InitializePlaywright(ServerTester);
    //     var account = ServerTester.NewAccount();
    //     await account.GrantAccessAsync();
    //     await account.MakeAdmin(true);
    //
    //     await GoToUrl("/login");
    //     await LogIn(account.RegisterDetails.Email, account.RegisterDetails.Password);
    //
    //     var storeId = account.StoreId;
    //
    //     // Configure Stash
    //     await GoToUrl($"/plugins/{storeId}/stash/settings");
    //     await Page.Locator("#IsEnabled").SetCheckedAsync(true);
    //     await Page.FillAsync("#AllocationPercentage", "20");
    //     await Page.FillAsync("#BatchThresholdFiat", "1000");
    //     await Page.SelectOptionAsync("#DestinationType", "ColdStorage");
    //     await Page.FillAsync("#DestinationAddress", "bcrt1qcpf40xrswnmtsugt3xwpd4mrh8cv872jm3l2je");
    //     await Page.ClickAsync("button[type='submit']");
    //     
    //     // Navigate to dashboard and click Emergency Stop
    //     await GoToUrl($"/plugins/{storeId}/stash");
    //     
    //     var resetButton = Page.Locator("button:has-text('Emergency Stop')");
    //     if (await resetButton.IsVisibleAsync())
    //     {
    //         await resetButton.ClickAsync();
    //         await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);
    //         TestLogs.LogInformation("✓ Emergency stop executed successfully");
    //     }
    //     else
    //     {
    //         TestLogs.LogInformation("✓ No pending allocations to reset (expected for new store)");
    //     }
    // }

    // [Fact]
    // public async Task CanExportBatchHistoryToCsvAsync()
    // {
    //     await InitializePlaywright(ServerTester);
    //     var account = ServerTester.NewAccount();
    //     await account.GrantAccessAsync();
    //     await account.MakeAdmin(true);
    //
    //     await GoToUrl("/login");
    //     await LogIn(account.RegisterDetails.Email, account.RegisterDetails.Password);
    //
    //     var storeId = account.StoreId;
    //
    //     // Go to batches page
    //     await GoToUrl($"/plugins/{storeId}/stash/batches");
    //     
    //     // Check export link exists
    //     var exportLink = Page.Locator("a[href*='export']");
    //     var hasExport = await exportLink.CountAsync() > 0;
    //     Assert.True(hasExport, "Export link should be present");
    //     TestLogs.LogInformation("✓ Export link is available");
    // }

    // [Fact]
    // public async Task ValidatesDestinationAddressAsync()
    // {
    //     await InitializePlaywright(ServerTester);
    //     var account = ServerTester.NewAccount();
    //     await account.GrantAccessAsync();
    //     await account.MakeAdmin(true);
    //
    //     await GoToUrl("/login");
    //     await LogIn(account.RegisterDetails.Email, account.RegisterDetails.Password);
    //
    //     var storeId = account.StoreId;
    //
    //     // Go to settings
    //     await GoToUrl($"/plugins/{storeId}/stash/settings");
    //     
    //     // Try to save with invalid address
    //     await Page.Locator("#IsEnabled").SetCheckedAsync(true);
    //     await Page.FillAsync("#AllocationPercentage", "20");
    //     await Page.FillAsync("#BatchThresholdFiat", "100");
    //     await Page.SelectOptionAsync("#DestinationType", "ColdStorage");
    //     await Page.FillAsync("#DestinationAddress", "invalid-address");
    //     
    //     await Page.ClickAsync("button[type='submit']");
    //     
    //     // Should show validation error
    //     var errorMessage = Page.Locator(".text-danger");
    //     var hasError = await errorMessage.CountAsync() > 0;
    //     Assert.True(hasError, "Should show validation error for invalid address");
    //     TestLogs.LogInformation("✓ Invalid address correctly rejected");
    // }
}
