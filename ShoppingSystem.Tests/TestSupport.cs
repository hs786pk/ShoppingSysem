using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShoppingSysem.Data;
using ShoppingSysem.Models;
using ShoppingSysem.Services;
using Xunit;

namespace ShoppingSystem.Tests;

public sealed class FakeAuthProvider : AuthenticationStateProvider
{
    public string? UserId { get; set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var identity = UserId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, UserId) }, "test");

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }
}

// One signed-in caller (or anonymous when userId is null) with its own scope,
// exactly like one Blazor circuit.
public sealed class Actor : IDisposable
{
    private readonly IServiceScope _scope;
    public OrderService Orders { get; }
    public RecommendationService Recs { get; }
    public IServiceProvider Services => _scope.ServiceProvider;
    public Actor(IServiceScope scope, string? userId)
    {
        _scope = scope;
        var sp = scope.ServiceProvider;

        var auth = (FakeAuthProvider)sp.GetRequiredService<AuthenticationStateProvider>();
        auth.UserId = userId;

        var db = sp.GetRequiredService<ApplicationDbContext>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var shopAccess = new ShopAccessService(db);
        Orders = new OrderService(db, auth, users, shopAccess, NullLogger<OrderService>.Instance);
        Recs = new RecommendationService(db, auth, users, NullLogger<RecommendationService>.Instance);
    }

    public void Dispose() => _scope.Dispose();
}

public sealed class TestDb : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"shop-test-{Guid.NewGuid():N}.db");

    private readonly ServiceProvider _root;

    public string ConnectionString => $"Data Source={_path}";

    public TestDb()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={_path}"));
        services.AddScoped<AuthenticationStateProvider, FakeAuthProvider>();
        services.AddIdentityCore<ApplicationUser>(o =>
        {
            o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Database.EnsureCreated();
    }

    public Actor As(string? userId) => new(_root.CreateScope(), userId);

    public async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        using var scope = _root.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public async Task<string> AddUserAsync(string email, string role)
    {
        using var scope = _root.CreateScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roles.RoleExistsAsync(role))
            Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);

        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
    }

    public async Task<(int ShopId, int ItemId)> AddItemAsync(
        string shopName, int stock, decimal price = 100m)
    {
        using var scope = _root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var shop = new Shop { Name = shopName, Location = "Test" };
        var item = new Item { Name = "Widget", Price = price, Stock = stock, Shop = shop };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        return (shop.Id, item.Id);
    }

    public async Task AssignAsync(string shopkeeperId, int shopId)
    {
        using var scope = _root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ShopAssignments.Add(new ShopAssignment { ShopkeeperId = shopkeeperId, ShopId = shopId });
        await db.SaveChangesAsync();
    }

    public Task<int> StockAsync(int itemId) =>
        QueryAsync(db => db.Items.Where(i => i.Id == itemId).Select(i => i.Stock).SingleAsync());

    public Task<int> OrderCountAsync() =>
        QueryAsync(db => db.Orders.CountAsync());

    public Task<Order> OrderAsync(int orderId) =>
        QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));

    public Task<int> InterestCountAsync(string userId) =>
        QueryAsync(db => db.CustomerInterests.CountAsync(i => i.UserId == userId));

    public void Dispose()
    {
        _root.Dispose();
        SqliteConnection.ClearAllPools();

        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // Temp file still locked; the OS temp cleanup will remove it.
        }
    }
}