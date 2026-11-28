using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoppingSysem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ShortDescription",
                table: "Items",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(250)",
                oldMaxLength: 250,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "Items",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SKU",
                table: "Items",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Subcategory",
                table: "Items",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_ShopId_SKU",
                table: "Items",
                columns: new[] { "ShopId", "SKU" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_Subcategory",
                table: "Items",
                column: "Subcategory");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_ShopId_SKU",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_Subcategory",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "SKU",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Subcategory",
                table: "Items");

            migrationBuilder.AlterColumn<string>(
                name: "ShortDescription",
                table: "Items",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300,
                oldNullable: true);
        }
    }
}
