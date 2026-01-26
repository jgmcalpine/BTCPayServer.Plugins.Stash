using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.RockstarDev.Plugins.Stash.Data;

internal class PluginMigrationRunner(
    PluginDbContextFactory dbContextFactory,
    ILogger<PluginMigrationRunner> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stash plugin: Starting database migration...");
        try
        {
            await using var ctx = dbContextFactory.CreateContext();
            
            // Log all migrations known to EF Core
            var allMigrations = ctx.Database.GetMigrations();
            logger.LogInformation("Stash plugin: All known migrations: {Migrations}", 
                string.Join(", ", allMigrations));
            
            // Log applied migrations
            var appliedMigrations = await ctx.Database.GetAppliedMigrationsAsync(cancellationToken);
            logger.LogInformation("Stash plugin: Applied migrations: {Migrations}", 
                string.Join(", ", appliedMigrations));
            
            var pendingMigrations = await ctx.Database.GetPendingMigrationsAsync(cancellationToken);
            var pendingList = pendingMigrations.ToList();
            logger.LogInformation("Stash plugin: {Count} pending migrations found: {Migrations}", 
                pendingList.Count, string.Join(", ", pendingList));
            
            await ctx.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Stash plugin: Database migration completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stash plugin: Database migration failed");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

