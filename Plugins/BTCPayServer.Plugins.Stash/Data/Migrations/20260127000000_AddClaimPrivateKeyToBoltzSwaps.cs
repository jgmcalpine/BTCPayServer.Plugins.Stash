using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Stash.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimPrivateKeyToBoltzSwaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimPrivateKey",
                schema: "BTCPayServer.Plugins.Stash",
                table: "BoltzSwaps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimPrivateKey",
                schema: "BTCPayServer.Plugins.Stash",
                table: "BoltzSwaps");
        }
    }
}
