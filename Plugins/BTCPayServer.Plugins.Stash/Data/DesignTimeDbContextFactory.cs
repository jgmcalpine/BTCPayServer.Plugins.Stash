using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BTCPayServer.Plugins.Stash.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PluginDbContext>
{
    public PluginDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<PluginDbContext>();

        builder.UseNpgsql("Host=localhost;Database=btcpayserver;Username=postgres;Password=postgres");

        return new PluginDbContext(builder.Options, true);
    }
}

