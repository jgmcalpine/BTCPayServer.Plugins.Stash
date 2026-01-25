using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Stash.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLiquidAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LiquidAddress",
                schema: "BTCPayServer.Plugins.Stash",
                table: "StashSettings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LiquidAddress",
                schema: "BTCPayServer.Plugins.Stash",
                table: "StashSettings");
        }
    }
}

