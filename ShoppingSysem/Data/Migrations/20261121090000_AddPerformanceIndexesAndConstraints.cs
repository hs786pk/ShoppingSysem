using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoppingSysem.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexesAndConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "Items",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_Category",
                table: "Items",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Name",
                table: "Items",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ShopId_Name",
                table: "Items",
                columns: new[] { "ShopId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId_OrderedAt",
                table: "Orders",
                columns: new[] { "CustomerId", "OrderedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ShopId_OrderedAt",
                table: "Orders",
                columns: new[] { "ShopId", "OrderedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ShopId_Status",
                table: "Orders",
                columns: new[] { "ShopId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderedAt",
                table: "Orders",
                column: "OrderedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                table: "Orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SaleDate",
                table: "Sales",
                column: "SaleDate");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ShopkeeperId_SaleDate",
                table: "Sales",
                columns: new[] { "ShopkeeperId", "SaleDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopAssignments_ShopkeeperId_ShopId",
                table: "ShopAssignments",
                columns: new[] { "ShopkeeperId", "ShopId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_Category",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_Name",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_ShopId_Name",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CustomerId_OrderedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ShopId_OrderedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ShopId_Status",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_OrderedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Status",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Sales_SaleDate",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_ShopkeeperId_SaleDate",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_ShopAssignments_ShopkeeperId_ShopId",
                table: "ShopAssignments");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "Items",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
