#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Plugins.Stash.Data;
using BTCPayServer.Plugins.Stash.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Stash.Services;

public class StashSettingsService(PluginDbContextFactory dbContextFactory)
{
    public async Task<StashSettings?> GetSettingsAsync(string storeId)
    {
        await using var db = dbContextFactory.CreateContext();
        return await db.StashSettings
            .FirstOrDefaultAsync(s => s.StoreId == storeId);
    }

    public async Task<StashSettings> GetOrCreateSettingsAsync(string storeId)
    {
        await using var db = dbContextFactory.CreateContext();
        var settings = await db.StashSettings
            .FirstOrDefaultAsync(s => s.StoreId == storeId);

        if (settings == null)
        {
            settings = new StashSettings
            {
                StoreId = storeId
            };
            db.StashSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        return settings;
    }

    public async Task SaveSettingsAsync(StashSettings settings)
    {
        await using var db = dbContextFactory.CreateContext();
        
        var existing = await db.StashSettings
            .FirstOrDefaultAsync(s => s.StoreId == settings.StoreId);

        if (existing == null)
        {
            db.StashSettings.Add(settings);
        }
        else
        {
            existing.IsEnabled = settings.IsEnabled;
            existing.AllocationPercentage = settings.AllocationPercentage;
            existing.BatchThresholdFiat = settings.BatchThresholdFiat;
            existing.FiatCurrency = settings.FiatCurrency;
            existing.DestinationType = settings.DestinationType;
            existing.DestinationAddress = settings.DestinationAddress;
            existing.LiquidAddress = settings.LiquidAddress;
            existing.XpubDerivationIndex = settings.XpubDerivationIndex;
            existing.MinimumBatchSats = settings.MinimumBatchSats;
            existing.FeeBlockTarget = settings.FeeBlockTarget;
            existing.UseTor = settings.UseTor;
            existing.Updated = System.DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
    }
}

