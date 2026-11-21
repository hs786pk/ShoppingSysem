using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShoppingSysem.Components.Pages;
using ShoppingSysem.Data;
using ShoppingSysem.Models;
using ShoppingSysem.Services;
using Xunit;

namespace ShoppingSystem.Tests;

// Drives the real MakeSale component without a renderer: services are set on it
// and its private handlers are invoked by name.
internal sealed class SaleHarness : IDisposable
{
    private const BindingFlags All =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly MakeSale _page = new();
    private readonly Actor _actor;

    public SaleHarness(TestDb db, string? userId)
    {
        _actor = db.As(userId);
        var sp = _actor.Services;

        SetProperty("DbContext", sp.GetRequiredService<ApplicationDbContext>());
        SetProperty("UserManager", sp.GetRequiredService<UserManager<ApplicationUser>>());
        SetProperty("Logger", NullLogger<MakeSale>.Instance);
        SetProperty("ShopAccess", new ShopAccessService(sp.GetRequiredService<ApplicationDbContext>()));
        SetProperty("AuthState",
            sp.GetRequiredService<AuthenticationStateProvider>().GetAuthenticationStateAsync());
    }

    private void SetProperty(string name, object value) =>
        typeof(MakeSale).GetProperty(name, All)!.SetValue(_page, value);

    private void SetField(string name, object value) =>
        typeof(MakeSale).GetField(name, All)!.SetValue(_page, value);

    private T GetField<T>(string name) =>
        (T)typeof(MakeSale).GetField(name, All)!.GetValue(_page)!;

    private Task Call(string name) =>
        (Task)typeof(MakeSale).GetMethod(name, All)!.Invoke(_page, null)!;

    public string? Error => GetField<string?>("errorMessage");
    public string? Success => GetField<string?>("successMessage");
    public List<Shop> MyShops => GetField<List<Shop>>("myShops");
    public List<Item> Items => GetField<List<Item>>("items");
    public int SelectedShopId => GetField<int>("selectedShopId");

    public Task InitAsync() => Call("OnInitializedAsync");

    public async Task LoadItemsAsync(int shopId)
    {
        SetField("selectedShopId", shopId);
        await Call("LoadItems");
    }

    public async Task<(string? Error, string? Success)> SellAsync(
        int shopId, int itemId, string quantity)
    {
        SetField("selectedShopId", shopId);
        SetField("selectedItemId", itemId);
        SetField("quantity", quantity);
        await Call("ConfirmSale");
        return (Error, Success);
    }

    public void Dispose() => _actor.Dispose();
}

public sealed class SaleSecurityTests : IDisposable
{
    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();

    private sealed record World(
        string Keeper1, string Keeper2, string Customer, string Admin,
        int Shop1, int Item1, int Shop2, int Item2);

    private async Task<World> SeedAsync(int stock = 5, decimal price = 100m)
    {
        var keeper1 = await _db.AddUserAsync("k1@test.local", "Shopkeeper");
        var keeper2 = await _db.AddUserAsync("k2@test.local", "Shopkeeper");
        var customer = await _db.AddUserAsync("c@test.local", "Customer");
        var admin = await _db.AddUserAsync("admin@test.local", "Admin");
        var (shop1, item1) = await _db.AddItemAsync("Shop1", stock, price);
        var (shop2, item2) = await _db.AddItemAsync("Shop2", stock, price);
        await _db.AssignAsync(keeper1, shop1);
        await _db.AssignAsync(keeper2, shop2);
        return new World(keeper1, keeper2, customer, admin, shop1, item1, shop2, item2);
    }

    private async Task<(string? Error, string? Success)> SellAsync(
        string? userId, int shopId, int itemId, string quantity)
    {
        using var harness = new SaleHarness(_db, userId);
        return await harness.SellAsync(shopId, itemId, quantity);
    }

    private Task<List<Sale>> SalesAsync() =>
        _db.QueryAsync(db => db.Sales.AsNoTracking().ToListAsync());

    // ---- Valid sale ------------------------------------------------------

    [Fact]
    public async Task Sale_Valid_RecordsSaleForCallerAndDeductsStock()
    {
        var w = await SeedAsync();

        var (error, success) = await SellAsync(w.Keeper1, w.Shop1, w.Item1, "2");

        Assert.Null(error);
        Assert.NotNull(success);
        Assert.Equal(3, await _db.StockAsync(w.Item1));

        var sale = Assert.Single(await SalesAsync());
        Assert.Equal(w.Keeper1, sale.ShopkeeperId);
        Assert.Equal(w.Shop1, sale.ShopId);
        Assert.Equal(w.Item1, sale.ItemId);
        Assert.Equal(2, sale.QuantitySold);
        Assert.Equal(200m, sale.TotalPrice);
    }

    // ---- Cross-vendor and manipulated IDs --------------------------------

    [Fact]
    public async Task Sale_InShopNotAssignedToCaller_IsRejected()
    {
        var w = await SeedAsync();

        var (error, success) = await SellAsync(w.Keeper1, w.Shop2, w.Item2, "1");

        Assert.NotNull(error);
        Assert.Null(success);
        Assert.Empty(await SalesAsync());
        Assert.Equal(5, await _db.StockAsync(w.Item2));
    }

    [Fact]
    public async Task Sale_ItemFromAnotherShop_IsRejected()
    {
        var w = await SeedAsync();

        // Own shop selected, but the product ID belongs to the other shop.
        var (error, success) = await SellAsync(w.Keeper1, w.Shop1, w.Item2, "1");

        Assert.NotNull(error);
        Assert.Null(success);
        Assert.Empty(await SalesAsync());
        Assert.Equal(5, await _db.StockAsync(w.Item2));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(999999, 1)]
    public async Task Sale_InvalidShopId_IsRejected(int shopId, int itemId)
    {
        var w = await SeedAsync();

        var (error, success) = await SellAsync(w.Keeper1, shopId, itemId, "1");

        Assert.NotNull(error);
        Assert.Null(success);
        Assert.Empty(await SalesAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    [InlineData(999999)]
    public async Task Sale_InvalidItemId_IsRejected(int itemId)
    {
        var w = await SeedAsync();

        var (error, success) = await SellAsync(w.Keeper1, w.Shop1, itemId, "1");

        Assert.NotNull(error);
        Assert.Null(success);
        Assert.Empty(await SalesAsync());
        Assert.Equal(5, await _db.StockAsync(w.Item1));
    }

    [Fact]
    public async Task LoadItems_ForUnassignedShop_ReturnsNothing()
    {
        var w = await SeedAsync();
        using var harness = new SaleHarness(_db, w.Keeper1);

        await harness.LoadItemsAsync(w.Shop2);

        Assert.Empty(harness.Items);
        Assert.NotNull(harness.Error);
        Assert.Equal(0, harness.SelectedShopId);
    }

    [Fact]
    public async Task Init_ListsOnlyTheCallersShops()
    {
        var w = await SeedAsync();
        using var harness = new SaleHarness(_db, w.Keeper1);

        await harness.InitAsync();

        var shop = Assert.Single(harness.MyShops);
        Assert.Equal(w.Shop1, shop.Id);
    }

    // ---- Unauthorized callers --------------------------------------------

    [Fact]
    public async Task Sale_ByCustomerAdminOrAnonymous_IsRejected()
    {
        var w = await SeedAsync();

        foreach (var userId in new string?[] { w.Customer, w.Admin, null })
        {
            var (error, success) = await SellAsync(userId, w.Shop1, w.Item1, "1");

            Assert.NotNull(error);
            Assert.Null(success);
        }

        Assert.Empty(await SalesAsync());
        Assert.Equal(5, await _db.StockAsync(w.Item1));
    }

    // ---- Invalid quantities and prices -----------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("10001")]
    [InlineData("99999999999")]
    public async Task Sale_InvalidQuantity_IsRejected(string quantity)
    {
        var w = await SeedAsync();

        var (error, success) = await SellAsync(w.Keeper1, w.Shop1, w.Item1, quantity);

        Assert.NotNull(error);
        Assert.Null(success);
        Assert.Empty(await SalesAsync());
        Assert.Equal(5, await _db.StockAsync(w.Item1));
    }

    [Fact]
    public async Task Sale_MoreThanStock_IsRejected()
    {
        var w = await SeedAsync(stock: 2);

        var (error, success) = await SellAsync(w.Keeper1, w.Shop1, w.Item1, "3");

        Assert.NotNull(error);
        Assert.Null(success);
        Assert.Empty(await SalesAsync());
        Assert.Equal(2, await _db.StockAsync(w.Item1));
    }

    [Fact]
    public async Task Sale_NegativePrice_IsRejected()
    {
        var w = await SeedAsync(price: -5m);

        var (error, success) = await SellAsync(w.Keeper1, w.Shop1, w.Item1, "1");

        Assert.NotNull(error);
        Assert.Null(success);
        Assert.Empty(await SalesAsync());
        Assert.Equal(5, await _db.StockAsync(w.Item1));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("100000000.01")]
    [InlineData("12.345")]
    public async Task Sale_InvalidPrice_IsRejected(string priceText)
    {
        var price = decimal.Parse(priceText, System.Globalization.CultureInfo.InvariantCulture);
        var w = await SeedAsync(price: price);

        var (error, success) = await SellAsync(w.Keeper1, w.Shop1, w.Item1, "1");

        Assert.NotNull(error);
        Assert.Null(success);
        Assert.Empty(await SalesAsync());
        Assert.Equal(5, await _db.StockAsync(w.Item1));
    }

    [Fact]
    public async Task Sale_TotalOverflow_IsRejected()
    {
        var w = await SeedAsync(price: decimal.MaxValue);

        var (error, success) = await SellAsync(w.Keeper1, w.Shop1, w.Item1, "2");

        Assert.NotNull(error);
        Assert.Null(success);
        Assert.Empty(await SalesAsync());
        Assert.Equal(5, await _db.StockAsync(w.Item1));
    }

    // ---- Concurrent walk-in sales ----------------------------------------
    // Checks invariants, so a SQLite "database is locked" failure is tolerated.

    [Fact]
    public async Task ConcurrentSales_NeverOversellOrOverRecord()
    {
        var w = await SeedAsync(stock: 3);
        var harnesses = Enumerable.Range(0, 10)
            .Select(_ => new SaleHarness(_db, w.Keeper1)).ToList();

        try
        {
            var results = await Task.WhenAll(harnesses.Select(h =>
                Task.Run(() => h.SellAsync(w.Shop1, w.Item1, "1"))));

            var succeeded = results.Count(r => r.Success is not null);
            var sales = await SalesAsync();

            Assert.InRange(succeeded, 0, 3);
            Assert.Equal(3 - succeeded, await _db.StockAsync(w.Item1));
            Assert.Equal(succeeded, sales.Count);
            Assert.Equal(succeeded, sales.Sum(s => s.QuantitySold));
        }
        finally
        {
            harnesses.ForEach(h => h.Dispose());
        }
    }
}

// Static guards for the external-login page and public registration. The page
// needs a real external provider to run, so these read the source and fail if
// the security-relevant lines are removed. They are not behavioral tests.
public sealed class AccountSourceGuardTests
{
    private static string Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ShoppingSystem.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "ShoppingSysem", relativePath);
        return Regex.Replace(File.ReadAllText(path), @"\s+", "");
    }

    private const string ExternalLogin = "Components/Account/Pages/ExternalLogin.razor";
    private const string Register = "Components/Account/Pages/Register.razor";

    [Fact]
    public void ExternalLogin_DoesNotBypassTwoFactor()
    {
        var source = Read(ExternalLogin);

        Assert.DoesNotContain("bypassTwoFactor:true", source);
        Assert.Contains("bypassTwoFactor:false", source);
        Assert.Contains("result.RequiresTwoFactor", source);
        Assert.Contains("Account/LoginWith2fa", source);
    }

    [Theory]
    [InlineData(ExternalLogin)]
    [InlineData(Register)]
    public void SelfRegistration_AssignsOnlyTheCustomerRole(string page)
    {
        var source = Read(page);

        Assert.Contains("AddToRoleAsync(user,AppRoles.Customer)", source);
        Assert.DoesNotContain("\"Admin\"", source);
        Assert.DoesNotContain("\"Shopkeeper\"", source);
        Assert.DoesNotContain("AddToRolesAsync", source);
    }
}