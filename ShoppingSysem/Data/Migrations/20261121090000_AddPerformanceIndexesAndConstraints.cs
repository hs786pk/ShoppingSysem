using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShoppingSysem.Data;

#nullable disable

namespace ShoppingSysem.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261121090000_AddPerformanceIndexesAndConstraints")]
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

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "CustomerInterests",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "CustomerInterests",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            CreateIndexIfMissing(migrationBuilder, "CustomerInterests", "IX_CustomerInterests_ItemId", "[ItemId]");
            CreateIndexIfMissing(migrationBuilder, "CustomerInterests", "IX_CustomerInterests_UserId", "[UserId]");
            CreateIndexIfMissing(
                migrationBuilder,
                "CustomerInterests",
                "IX_CustomerInterests_UserId_ItemId_Type_CreatedAt",
                "[UserId], [ItemId], [Type], [CreatedAt]");

            CreateIndexIfMissing(migrationBuilder, "Items", "IX_Items_Category", "[Category]");
            CreateIndexIfMissing(migrationBuilder, "Items", "IX_Items_Name", "[Name]");
            CreateIndexIfMissing(
                migrationBuilder,
                "Items",
                "IX_Items_ShopId_Name",
                "[ShopId], [Name]",
                unique: true);

            CreateIndexIfMissing(migrationBuilder, "Orders", "IX_Orders_CustomerId_OrderedAt", "[CustomerId], [OrderedAt]");
            CreateIndexIfMissing(migrationBuilder, "Orders", "IX_Orders_ShopId_OrderedAt", "[ShopId], [OrderedAt]");
            CreateIndexIfMissing(migrationBuilder, "Orders", "IX_Orders_ShopId_Status", "[ShopId], [Status]");
            CreateIndexIfMissing(migrationBuilder, "Orders", "IX_Orders_OrderedAt", "[OrderedAt]");
            CreateIndexIfMissing(migrationBuilder, "Orders", "IX_Orders_Status", "[Status]");

            CreateIndexIfMissing(migrationBuilder, "Sales", "IX_Sales_SaleDate", "[SaleDate]");
            CreateIndexIfMissing(migrationBuilder, "Sales", "IX_Sales_ShopkeeperId_SaleDate", "[ShopkeeperId], [SaleDate]");

            CreateIndexIfMissing(
                migrationBuilder,
                "ShopAssignments",
                "IX_ShopAssignments_ShopkeeperId_ShopId",
                "[ShopkeeperId], [ShopId]",
                unique: true);
        }

        private static void CreateIndexIfMissing(
            MigrationBuilder migrationBuilder,
            string table,
            string indexName,
            string columns,
            bool unique = false)
        {
            var uniqueKeyword = unique ? "UNIQUE " : "";
            migrationBuilder.Sql($"""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'{indexName}'
                      AND object_id = OBJECT_ID(N'[{table}]'))
                CREATE {uniqueKeyword}INDEX [{indexName}] ON [{table}] ({columns});
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerInterests_ItemId",
                table: "CustomerInterests");

            migrationBuilder.DropIndex(
                name: "IX_CustomerInterests_UserId",
                table: "CustomerInterests");

            migrationBuilder.DropIndex(
                name: "IX_CustomerInterests_UserId_ItemId_Type_CreatedAt",
                table: "CustomerInterests");

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

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "CustomerInterests",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "CustomerInterests",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);
        }
    }
}
