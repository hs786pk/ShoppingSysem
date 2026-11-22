using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShoppingSysem.Data;
using Xunit;

namespace ShoppingSystem.Tests;

/// <summary>
/// Guards schema drift: migration files, snapshot content, and apply-on-SQL-Server.
/// SQLite unit tests use EnsureCreated; SQL Server integration uses MigrateAsync.
/// </summary>
public sealed class MigrationVerificationTests
{
    private static readonly string MigrationsDir = LocateMigrationsDirectory();

    private static string LocateMigrationsDirectory()
    {
        // Tests run from bin/...; walk up until the source Migrations folder is found.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "ShoppingSysem", "Data", "Migrations");
            if (Directory.Exists(candidate))
                return candidate;

            candidate = Path.Combine(
                dir.FullName, "ShoppingSysem", "ShoppingSysem", "Data", "Migrations");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        // Fallback: relative to the test project content root when running from source.
        var fromCwd = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(),
                "..", "ShoppingSysem", "Data", "Migrations"));
        if (Directory.Exists(fromCwd))
            return fromCwd;

        throw new DirectoryNotFoundException(
            "Could not locate Data/Migrations for verification tests.");
    }

    [Fact]
    public void Migration_files_exist_and_are_timestamp_ordered()
    {
        var files = Directory.GetFiles(MigrationsDir, "*.cs")
            .Select(Path.GetFileName)
            .Where(n => n is not null &&
                        n != "ApplicationDbContextModelSnapshot.cs" &&
                        !n.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Cast<string>()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(files);
        Assert.Contains(files, f => f.Contains("AddPerformanceIndexesAndConstraints", StringComparison.Ordinal));

        // Lexicographic order of the timestamp prefix must match chronological order.
        var timestamps = files
            .Select(f => f.Split('_', 2)[0])
            .ToList();

        var sorted = timestamps.OrderBy(t => t, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, timestamps);
    }

    [Fact]
    public void Snapshot_includes_performance_indexes_and_unique_constraints()
    {
        var snapshotPath = Path.Combine(MigrationsDir, "ApplicationDbContextModelSnapshot.cs");
        Assert.True(File.Exists(snapshotPath), "Model snapshot is missing.");

        var text = File.ReadAllText(snapshotPath);

        // Items
        Assert.Contains("b.HasIndex(\"ShopId\", \"Name\")", text);
        Assert.Contains("b.HasIndex(\"Category\")", text);
        Assert.Contains("b.HasIndex(\"Name\")", text);
        Assert.Contains("b.Property<string>(\"Description\")", text);
        Assert.Contains(".HasMaxLength(1000)", text);

        // Orders
        Assert.Contains("b.HasIndex(\"CustomerId\", \"OrderedAt\")", text);
        Assert.Contains("b.HasIndex(\"ShopId\", \"OrderedAt\")", text);
        Assert.Contains("b.HasIndex(\"ShopId\", \"Status\")", text);

        // Sales
        Assert.Contains("b.HasIndex(\"ShopkeeperId\", \"SaleDate\")", text);

        // Shop assignments
        Assert.Contains("b.HasIndex(\"ShopkeeperId\", \"ShopId\")", text);

        // Customer interests
        Assert.Contains("b.HasIndex(\"UserId\", \"ItemId\", \"Type\", \"CreatedAt\")", text);
        Assert.Contains(".HasMaxLength(450)", text);
    }

    [Fact]
    public void Latest_migration_up_and_down_are_symmetric_for_new_indexes()
    {
        var path = Directory.GetFiles(MigrationsDir, "*AddPerformanceIndexesAndConstraints.cs")
            .Single();
        var text = File.ReadAllText(path);

        var names = new[]
        {
            "IX_CustomerInterests_ItemId",
            "IX_CustomerInterests_UserId",
            "IX_CustomerInterests_UserId_ItemId_Type_CreatedAt",
            "IX_Items_Category",
            "IX_Items_Name",
            "IX_Items_ShopId_Name",
            "IX_Orders_CustomerId_OrderedAt",
            "IX_Orders_ShopId_OrderedAt",
            "IX_Orders_ShopId_Status",
            "IX_Orders_OrderedAt",
            "IX_Orders_Status",
            "IX_Sales_SaleDate",
            "IX_Sales_ShopkeeperId_SaleDate",
            "IX_ShopAssignments_ShopkeeperId_ShopId"
        };

        foreach (var name in names)
        {
            var occurrences = Regex.Matches(text, Regex.Escape(name)).Count;
            Assert.True(occurrences >= 2, $"{name} should appear in Up and Down, found {occurrences}.");
        }

        Assert.Contains("IF NOT EXISTS", text);
    }

    [Fact]
    public void Compiled_assembly_exposes_all_migration_types()
    {
        var assembly = typeof(ApplicationDbContext).Assembly;
        var migrationTypes = assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Microsoft.EntityFrameworkCore.Migrations.Migration))
                        && !t.IsAbstract)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("AddPerformanceIndexesAndConstraints", migrationTypes);
        Assert.Contains("CreateOrders", migrationTypes);
        Assert.Contains("AddShopManagementTables", migrationTypes);
        Assert.Contains("AddItemDescription", migrationTypes);
    }

    [Fact]
    public async Task Sqlite_EnsureCreated_matches_runtime_model()
    {
        // Unit-test databases use EnsureCreated; this confirms the current model
        // still builds a usable schema for in-memory-style SQLite tests.
        using var db = new TestDb();
        await db.QueryAsync(async ctx =>
        {
            Assert.True(await ctx.Database.CanConnectAsync());
            // Touch core sets so mapping errors surface immediately.
            _ = await ctx.Shops.CountAsync();
            _ = await ctx.Items.CountAsync();
            _ = await ctx.Orders.CountAsync();
            _ = await ctx.Sales.CountAsync();
            _ = await ctx.ShopAssignments.CountAsync();
            return 0;
        });
    }

    [Fact]
    public async Task SqlServer_MigrateAsync_applies_all_migrations_when_configured()
    {
        var cs = Environment.GetEnvironmentVariable(
            SqlServerTestDatabase.ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(cs))
        {
            // Optional integration gate — same pattern as concurrency tests.
            return;
        }

        var databaseName = "ShopMigrateVerify_" + Guid.NewGuid().ToString("N");
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cs)
        {
            InitialCatalog = databaseName
        };

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o =>
            o.UseSqlServer(builder.ConnectionString));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            await db.Database.MigrateAsync();

            var pending = await db.Database.GetPendingMigrationsAsync();
            Assert.Empty(pending);

            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Contains(applied, m => m.Contains("AddPerformanceIndexesAndConstraints"));
            Assert.Contains(applied, m => m.Contains("CreateOrders"));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
