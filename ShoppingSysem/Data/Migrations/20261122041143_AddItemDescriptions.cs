using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoppingSysem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemDescriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Items");

            migrationBuilder.AddColumn<string>(
                name: "FullDescription",
                table: "Items",
                type: "nvarchar(max)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortDescription",
                table: "Items",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FullDescription",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ShortDescription",
                table: "Items");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Items",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }
    }
}
