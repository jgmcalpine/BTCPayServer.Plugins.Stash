using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.RockstarDev.Plugins.Stash.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.RockstarDev.Plugins.Stash");

            migrationBuilder.CreateTable(
                name: "StashSettings",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllocationPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    BatchThresholdFiat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FiatCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DestinationType = table.Column<int>(type: "integer", nullable: false),
                    DestinationAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    XpubDerivationIndex = table.Column<int>(type: "integer", nullable: false),
                    MinimumBatchSats = table.Column<long>(type: "bigint", nullable: false),
                    FeeBlockTarget = table.Column<int>(type: "integer", nullable: false),
                    UseTor = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StashSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutedBatches",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExecutionType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalSats = table.Column<long>(type: "bigint", nullable: false),
                    FeeSats = table.Column<long>(type: "bigint", nullable: false),
                    NetSats = table.Column<long>(type: "bigint", nullable: false),
                    FiatValueAtExecution = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FiatCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ExchangeRateAtExecution = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    WeightedAverageCostBasis = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    DestinationAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SwapId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UsdtReceived = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AllocationCount = table.Column<int>(type: "integer", nullable: false),
                    InitiatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutedBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingAllocations",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InvoiceId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TotalReceivedSats = table.Column<long>(type: "bigint", nullable: false),
                    AllocatedSats = table.Column<long>(type: "bigint", nullable: false),
                    FiatValueAtReceipt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FiatCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ExchangeRateAtReceipt = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    IsExecuted = table.Column<bool>(type: "boolean", nullable: false),
                    ExecutedBatchId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingAllocations_ExecutedBatches_ExecutedBatchId",
                        column: x => x.ExecutedBatchId,
                        principalSchema: "BTCPayServer.RockstarDev.Plugins.Stash",
                        principalTable: "ExecutedBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StashSettings_StoreId",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash",
                table: "StashSettings",
                column: "StoreId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutedBatches_InitiatedAt",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash",
                table: "ExecutedBatches",
                column: "InitiatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutedBatches_Status",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash",
                table: "ExecutedBatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutedBatches_StoreId",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash",
                table: "ExecutedBatches",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAllocations_ExecutedBatchId",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash",
                table: "PendingAllocations",
                column: "ExecutedBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAllocations_InvoiceId",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash",
                table: "PendingAllocations",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAllocations_IsExecuted",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash",
                table: "PendingAllocations",
                column: "IsExecuted");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAllocations_StoreId",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash",
                table: "PendingAllocations",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAllocations_StoreId_IsExecuted",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash",
                table: "PendingAllocations",
                columns: new[] { "StoreId", "IsExecuted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingAllocations",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash");

            migrationBuilder.DropTable(
                name: "ExecutedBatches",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash");

            migrationBuilder.DropTable(
                name: "StashSettings",
                schema: "BTCPayServer.RockstarDev.Plugins.Stash");
        }
    }
}

