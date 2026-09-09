using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyStore.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseIdToStockAdjustment1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchaseId",
                table: "StockAdjustments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_PurchaseId",
                table: "StockAdjustments",
                column: "PurchaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockAdjustments_Purchases_PurchaseId",
                table: "StockAdjustments",
                column: "PurchaseId",
                principalTable: "Purchases",
                principalColumn: "PurchaseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockAdjustments_Purchases_PurchaseId",
                table: "StockAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_StockAdjustments_PurchaseId",
                table: "StockAdjustments");

            migrationBuilder.DropColumn(
                name: "PurchaseId",
                table: "StockAdjustments");
        }
    }
}
