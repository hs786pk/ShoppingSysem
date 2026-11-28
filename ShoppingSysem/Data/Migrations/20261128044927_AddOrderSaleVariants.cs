using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoppingSysem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderSaleVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "Sales",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariantLabel",
                table: "Sales",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariantLabel",
                table: "Orders",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_VariantId",
                table: "Sales",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_VariantId",
                table: "Orders",
                column: "VariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_ProductVariants_VariantId",
                table: "Orders",
                column: "VariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_ProductVariants_VariantId",
                table: "Sales",
                column: "VariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_ProductVariants_VariantId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_ProductVariants_VariantId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_VariantId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Orders_VariantId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "VariantLabel",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VariantLabel",
                table: "Orders");
        }
    }
}
