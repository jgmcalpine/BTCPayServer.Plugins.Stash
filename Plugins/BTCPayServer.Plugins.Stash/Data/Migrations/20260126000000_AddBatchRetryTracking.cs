using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Stash.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchRetryTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                schema: "BTCPayServer.Plugins.Stash",
                table: "ExecutedBatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsRetryable",
                schema: "BTCPayServer.Plugins.Stash",
                table: "ExecutedBatches",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryCount",
                schema: "BTCPayServer.Plugins.Stash",
                table: "ExecutedBatches");

            migrationBuilder.DropColumn(
                name: "IsRetryable",
                schema: "BTCPayServer.Plugins.Stash",
                table: "ExecutedBatches");
        }
    }
}

