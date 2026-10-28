using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoppingSysem.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerInterestAndItemCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Items",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerInterests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerInterests", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerInterests");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Items");
        }
    }
}
