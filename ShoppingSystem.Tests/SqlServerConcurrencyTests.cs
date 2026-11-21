using System.Reflection;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShoppingSysem.Components.Pages;
using ShoppingSysem.Data;
using ShoppingSysem.Models;
using Xunit;

namespace ShoppingSystem.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(SqlServerTestDatabase.ConnectionStringVariable)))
        {
            Skip = $"Set {SqlServerTestDatabase.ConnectionStringVariable} to run SQL Server integration tests.";
        }
    }
}

public sealed class SqlServerConcurrencyTests : IAsyncLifetime
{
    private SqlServerTestDatabase? _db;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            SqlServerTestDatabase.ConnectionStringVariable);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            _db = new SqlServerTestDatabase(connectionString);
            await _db.InitializeAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
            await _db.DisposeAsync();
    }

    [SqlServerFact]
    public async Task ConcurrentOrders_OnSqlServer_NeverOversellOrOverRecord()
    {
        var db = Assert.IsType<SqlServerTestDatabase>(_db);
        var customerId = await db.AddUserAsync("sql-customer@test.local", "Customer");
        var (shopId, itemId) = await db.AddItemAsync("SQL Order Shop", stock: 3);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var actor = db.As(customerId);
            return await actor.Orders.PlaceAsync(customerId, itemId, 1);
        }));

        var succeeded = results.Count(result => result.Ok);
        Assert.Equal(3, succeeded);
        Assert.Equal(0, await db.StockAsync(itemId));
        Assert.Equal(3, await db.OrderCountAsync());
        Assert.Equal(shopId, (await db.OrdersAsync()).Select(order => order.ShopId).Distinct().Single());
    }

    [SqlServerFact]
    public async Task ConcurrentWalkInSales_OnSqlServer_NeverOversellOrOverRecord()
    {
        var db = Assert.IsType<SqlServerTestDatabase>(_db);
        var shopkeeperId = await db.AddUserAsync("sql-shopkeeper@test.local", "Shopkeeper");
        var (shopId, itemId) = await db.AddItemAsync("SQL Sale Shop", stock: 3);
        await db.AssignAsync(shopkeeperId, shopId);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var sale = new SqlServerSaleHarness(db, shopkeeperId);
            return await sale.SellAsync(shopId, itemId, "1");
        }));

        var succeeded = results.Count(result => result.Success is not null);
        Assert.Equal(3, succeeded);
        Assert.Equal(0, await db.StockAsync(itemId));
        Assert.Equal(3, await db.SaleCountAsync());
        Assert.Equal(3, (await db.SalesAsync()).Sum(sale => sale.QuantitySold));
    }
}

internal sealed class SqlServerTestDatabase : IAsyncDisposable
{
    public const string ConnectionStringVariable = "SHOPPING_TEST_SQLSERVER_CONNECTION_STRING";

    private readonly string _databaseName = $"ShoppingConcurrency_{Guid.NewGuid():N}";
    private readonly string _serverConnectionString;
    private string _testConnectionString = string.Empty;
    private ServiceProvider? _root;

    public SqlServerTestDatabase(string connectionString)
    {
        _serverConnectionString = connectionString;
    }

    public async Task InitializeAsync()
    {
        var master = new SqlConnectionStringBuilder(_serverConnectionString)
        {
            InitialCatalog = "master"
        };

        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{_databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        _testConnectionString = new SqlConnectionStringBuilder(_serverConnectionString)
        {
            InitialCatalog = _databaseName
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(_testConnectionString));
        services.AddScoped<AuthenticationStateProvider, FakeAuthProvider>();
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        _root = services.BuildServiceProvider();

        await using var scope = _root.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Database.EnsureCreatedAsync();
    }

    public Actor As(string? userId)
    {
        if (_root is null)
            throw new InvalidOperationException("The SQL Server test database is not initialized.");

        return new Actor(_root.CreateScope(), userId);
    }

    public async Task<string> AddUserAsync(string email, string role)
    {
        await using var scope = _root!.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roles.RoleExistsAsync(role))
            Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
    }

    public async Task<(int ShopId, int ItemId)> AddItemAsync(
        string shopName, int stock, decimal price = 100m)
    {
        await using var scope = _root!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shop = new Shop { Name = shopName, Location = "SQL Server integration test" };
        var item = new Item { Name = "Concurrency item", Price = price, Stock = stock, Shop = shop };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return (shop.Id, item.Id);
    }

    public async Task AssignAsync(string shopkeeperId, int shopId)
    {
        await using var scope = _root!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ShopAssignments.Add(new ShopAssignment
        {
            ShopkeeperId = shopkeeperId,
            ShopId = shopId
        });
        await db.SaveChangesAsync();
    }

    public Task<int> StockAsync(int itemId) => QueryAsync(db =>
        db.Items.Where(item => item.Id == itemId).Select(item => item.Stock).SingleAsync());

    public Task<int> OrderCountAsync() => QueryAsync(db => db.Orders.CountAsync());
    public Task<int> SaleCountAsync() => QueryAsync(db => db.Sales.CountAsync());
    public Task<List<Order>> OrdersAsync() => QueryAsync(db => db.Orders.AsNoTracking().ToListAsync());
    public Task<List<Sale>> SalesAsync() => QueryAsync(db => db.Sales.AsNoTracking().ToListAsync());

    private async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = _root!.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        if (_root is not null)
        {
            await _root.DisposeAsync();
            _root = null;
        }

        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(_serverConnectionString)
        {
            InitialCatalog = "master"
        };

        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]";
        await command.ExecuteNonQueryAsync();
    }
}

internal sealed class SqlServerSaleHarness : IDisposable
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Actor _actor;
    private readonly MakeSale _page = new();

    public SqlServerSaleHarness(SqlServerTestDatabase db, string userId)
    {
        _actor = db.As(userId);
        var services = _actor.Services;
        SetProperty("DbContext", services.GetRequiredService<ApplicationDbContext>());
        SetProperty("UserManager", services.GetRequiredService<UserManager<ApplicationUser>>());
        SetProperty("Logger", NullLogger<MakeSale>.Instance);
        SetProperty("ShopAccess", new ShoppingSysem.Services.ShopAccessService(
            services.GetRequiredService<ApplicationDbContext>()));
        SetProperty("AuthState", services.GetRequiredService<AuthenticationStateProvider>()
            .GetAuthenticationStateAsync());
    }

    public async Task<(string? Error, string? Success)> SellAsync(
        int shopId, int itemId, string quantity)
    {
        typeof(MakeSale).GetField("selectedShopId", All)!.SetValue(_page, shopId);
        typeof(MakeSale).GetField("selectedItemId", All)!.SetValue(_page, itemId);
        typeof(MakeSale).GetField("quantity", All)!.SetValue(_page, quantity);
        var handler = (Task)typeof(MakeSale).GetMethod("ConfirmSale", All)!.Invoke(_page, null)!;
        await handler;
        return (
            (string?)typeof(MakeSale).GetField("errorMessage", All)!.GetValue(_page),
            (string?)typeof(MakeSale).GetField("successMessage", All)!.GetValue(_page));
    }

    private void SetProperty(string name, object value) =>
        typeof(MakeSale).GetProperty(name, All)!.SetValue(_page, value);

    public void Dispose() => _actor.Dispose();
}
