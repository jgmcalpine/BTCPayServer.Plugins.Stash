using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Stash.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBoltzSwaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BoltzSwaps",
                schema: "BTCPayServer.Plugins.Stash",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BatchId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BoltzSwapId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    BoltzStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Invoice = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    InvoiceAmountSats = table.Column<long>(type: "bigint", nullable: false),
                    OnchainAmountSats = table.Column<long>(type: "bigint", nullable: false),
                    MinerFeeSats = table.Column<long>(type: "bigint", nullable: false),
                    ServiceFeeSats = table.Column<long>(type: "bigint", nullable: false),
                    DestinationAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LockupAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Preimage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ClaimTransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RefundTransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsRetryable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    TimeoutBlockHeight = table.Column<long>(type: "bigint", nullable: false),
                    BlindingKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ClaimPublicKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SwapTreeJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoltzSwaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoltzSwaps_ExecutedBatches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "BTCPayServer.Plugins.Stash",
                        principalTable: "ExecutedBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoltzSwaps_BatchId",
                schema: "BTCPayServer.Plugins.Stash",
                table: "BoltzSwaps",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BoltzSwaps_BoltzSwapId",
                schema: "BTCPayServer.Plugins.Stash",
                table: "BoltzSwaps",
                column: "BoltzSwapId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoltzSwaps_State",
                schema: "BTCPayServer.Plugins.Stash",
                table: "BoltzSwaps",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_BoltzSwaps_StoreId",
                schema: "BTCPayServer.Plugins.Stash",
                table: "BoltzSwaps",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_BoltzSwaps_StoreId_State",
                schema: "BTCPayServer.Plugins.Stash",
                table: "BoltzSwaps",
                columns: new[] { "StoreId", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoltzSwaps",
                schema: "BTCPayServer.Plugins.Stash");
        }
    }
}

