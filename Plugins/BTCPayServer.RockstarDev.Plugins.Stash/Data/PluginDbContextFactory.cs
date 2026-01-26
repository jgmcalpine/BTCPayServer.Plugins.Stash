#nullable enable
using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.RockstarDev.Plugins.Stash.Data;

public class PluginDbContextFactory(IOptions<DatabaseOptions> options)
    : BaseDbContextFactory<PluginDbContext>(options, "BTCPayServer.RockstarDev.Plugins.Stash")
{
    public override PluginDbContext CreateContext(
        Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<PluginDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new PluginDbContext(builder.Options);
    }
}

