using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShoppingSysem.Components.Account;
using ShoppingSysem.Data;
using ShoppingSysem.Models;
using Xunit;

namespace ShoppingSystem.Tests;

public sealed class OrderSecurityTests : IDisposable
{
    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();

    private sealed record World(
        string CustA, string CustB, string Keeper1, string Keeper2,
        int Shop1, int Item1, int Shop2, int Item2);

    // Two customers, two shopkeepers each assigned to their own shop, one item per shop.
    private async Task<World> SeedAsync(int stock = 5)
    {
        var custA = await _db.AddUserAsync("a@test.local", "Customer");
        var custB = await _db.AddUserAsync("b@test.local", "Customer");
        var keeper1 = await _db.AddUserAsync("k1@test.local", "Shopkeeper");
        var keeper2 = await _db.AddUserAsync("k2@test.local", "Shopkeeper");
        var (shop1, item1) = await _db.AddItemAsync("Shop1", stock);
        var (shop2, item2) = await _db.AddItemAsync("Shop2", stock);
        await _db.AssignAsync(keeper1, shop1);
        await _db.AssignAsync(keeper2, shop2);
        return new World(custA, custB, keeper1, keeper2, shop1, item1, shop2, item2);
    }

    private async Task<int> PlaceAsync(string customerId, int itemId, int qty = 1)
    {
        using var actor = _db.As(customerId);
        var result = await actor.Orders.PlaceAsync(customerId, itemId, qty);
        Assert.True(result.Ok, result.Message);
        return result.Order!.Id;
    }

    // ---- Manipulated IDs / identity -------------------------------------

    [Fact]
    public async Task Place_AsAnotherCustomer_IsDenied()
    {
        var w = await SeedAsync();
        using var actor = _db.As(w.CustA);

        var result = await actor.Orders.PlaceAsync(w.CustB, w.Item1, 1);

        Assert.False(result.Ok);
        Assert.Equal(0, await _db.OrderCountAsync());
        Assert.Equal(5, await _db.StockAsync(w.Item1));
    }

    [Fact]
    public async Task Place_Anonymous_IsDenied()
    {
        var w = await SeedAsync();
        using var actor = _db.As(null);

        var result = await actor.Orders.PlaceAsync(w.CustA, w.Item1, 1);

        Assert.False(result.Ok);
        Assert.Equal(0, await _db.OrderCountAsync());
    }

    [Fact]
    public async Task Place_AsShopkeeper_IsDenied()
    {
        var w = await SeedAsync();
        using var actor = _db.As(w.Keeper1);

        var result = await actor.Orders.PlaceAsync(w.Keeper1, w.Item1, 1);

        Assert.False(result.Ok);
        Assert.Equal(0, await _db.OrderCountAsync());
    }

    [Fact]
    public async Task ListCustomerOrders_WithOtherCustomersId_ReturnsNothing()
    {
        var w = await SeedAsync();
        await PlaceAsync(w.CustB, w.Item1);

        using var attacker = _db.As(w.CustA);
        Assert.Empty(await attacker.Orders.GetForCustomerAsync(w.CustB));

        using var owner = _db.As(w.CustB);
        Assert.Single(await owner.Orders.GetForCustomerAsync(w.CustB));
    }

    [Fact]
    public async Task CancelByCustomer_OtherCustomersOrder_IsDenied()
    {
        var w = await SeedAsync();
        var orderId = await PlaceAsync(w.CustB, w.Item1, 2);

        using var attacker = _db.As(w.CustA);
        var result = await attacker.Orders.CancelByCustomerAsync(w.CustA, orderId);

        Assert.False(result.Ok);
        Assert.Equal(OrderStatus.Placed, (await _db.OrderAsync(orderId)).Status);
        Assert.Equal(3, await _db.StockAsync(w.Item1));
    }

    // ---- Cross-vendor access --------------------------------------------

    [Fact]
    public async Task Shopkeeper_CannotFulfillOtherShopsOrder()
    {
        var w = await SeedAsync();
        var orderId = await PlaceAsync(w.CustA, w.Item2); // belongs to Shop2

        using var keeper1 = _db.As(w.Keeper1);
        Assert.False((await keeper1.Orders.FulfillAsync(w.Keeper1, orderId)).Ok);
        Assert.False((await keeper1.Orders.CancelByShopkeeperAsync(w.Keeper1, orderId)).Ok);
        // Claiming to be the other shopkeeper must also fail.
        Assert.False((await keeper1.Orders.FulfillAsync(w.Keeper2, orderId)).Ok);

        Assert.Equal(OrderStatus.Placed, (await _db.OrderAsync(orderId)).Status);
        Assert.Equal(4, await _db.StockAsync(w.Item2));
    }

    [Fact]
    public async Task Shopkeeper_SeesOnlyOwnShopOrders()
    {
        var w = await SeedAsync();
        await PlaceAsync(w.CustA, w.Item1);
        await PlaceAsync(w.CustA, w.Item2);

        using var keeper1 = _db.As(w.Keeper1);
        var own = await keeper1.Orders.GetForShopkeeperAsync(w.Keeper1);
        Assert.Single(own);
        Assert.Equal(w.Shop1, own[0].ShopId);

        Assert.Empty(await keeper1.Orders.GetForShopkeeperAsync(w.Keeper2));
    }

    [Fact]
    public async Task Customer_CannotFulfillOrders()
    {
        var w = await SeedAsync();
        var orderId = await PlaceAsync(w.CustA, w.Item1);

        using var actor = _db.As(w.CustA);
        Assert.False((await actor.Orders.FulfillAsync(w.CustA, orderId)).Ok);
        Assert.Equal(OrderStatus.Placed, (await _db.OrderAsync(orderId)).Status);
    }

    // ---- Invalid input ---------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task Place_InvalidQuantity_IsRejected(int quantity)
    {
        var w = await SeedAsync();
        using var actor = _db.As(w.CustA);

        Assert.False((await actor.Orders.PlaceAsync(w.CustA, w.Item1, quantity)).Ok);
        Assert.Equal(0, await _db.OrderCountAsync());
        Assert.Equal(5, await _db.StockAsync(w.Item1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(999999)]
    public async Task Place_InvalidItemId_IsRejected(int itemId)
    {
        var w = await SeedAsync();
        using var actor = _db.As(w.CustA);

        Assert.False((await actor.Orders.PlaceAsync(w.CustA, itemId, 1)).Ok);
        Assert.Equal(0, await _db.OrderCountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(999999)]
    public async Task StatusChange_InvalidOrderId_IsRejected(int orderId)
    {
        var w = await SeedAsync();
        using var keeper = _db.As(w.Keeper1);
        using var customer = _db.As(w.CustA);

        Assert.False((await keeper.Orders.FulfillAsync(w.Keeper1, orderId)).Ok);
        Assert.False((await customer.Orders.CancelByCustomerAsync(w.CustA, orderId)).Ok);
    }

    [Fact]
    public async Task Place_MoreThanStock_IsRejected_AndStockUnchanged()
    {
        var w = await SeedAsync(stock: 2);
        using var actor = _db.As(w.CustA);

        Assert.False((await actor.Orders.PlaceAsync(w.CustA, w.Item1, 3)).Ok);
        Assert.Equal(2, await _db.StockAsync(w.Item1));
        Assert.Equal(0, await _db.OrderCountAsync());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("100000000.01")]
    [InlineData("12.345")]
    public async Task Place_InvalidProductPrice_IsRejected(string priceText)
    {
        var price = decimal.Parse(priceText, System.Globalization.CultureInfo.InvariantCulture);
        var customer = await _db.AddUserAsync("price-test@test.local", "Customer");
        var (_, itemId) = await _db.AddItemAsync("InvalidPriceShop", stock: 5, price: price);
        using var actor = _db.As(customer);

        var result = await actor.Orders.PlaceAsync(customer, itemId, 1);

        Assert.False(result.Ok);
        Assert.Equal(0, await _db.OrderCountAsync());
        Assert.Equal(5, await _db.StockAsync(itemId));
    }

    [Fact]
    public async Task Place_Valid_DeductsStock_AndUsesServerSidePriceAndShop()
    {
        var w = await SeedAsync();

        var orderId = await PlaceAsync(w.CustA, w.Item1, 2);

        var order = await _db.OrderAsync(orderId);
        Assert.Equal(3, await _db.StockAsync(w.Item1));
        Assert.Equal(w.CustA, order.CustomerId);
        Assert.Equal(w.Shop1, order.ShopId);
        Assert.Equal(100m, order.UnitPrice);
        Assert.Equal(200m, order.TotalPrice);
    }

    // ---- Duplicate operations -------------------------------------------

    [Fact]
    public async Task Cancel_Twice_RestoresStockOnlyOnce()
    {
        var w = await SeedAsync();
        var orderId = await PlaceAsync(w.CustA, w.Item1, 2);

        using var actor = _db.As(w.CustA);
        Assert.True((await actor.Orders.CancelByCustomerAsync(w.CustA, orderId)).Ok);
        Assert.False((await actor.Orders.CancelByCustomerAsync(w.CustA, orderId)).Ok);

        Assert.Equal(5, await _db.StockAsync(w.Item1));
    }

    [Fact]
    public async Task FulfilledOrder_CannotBeCancelled()
    {
        var w = await SeedAsync();
        var orderId = await PlaceAsync(w.CustA, w.Item1, 2);

        using var keeper = _db.As(w.Keeper1);
        Assert.True((await keeper.Orders.FulfillAsync(w.Keeper1, orderId)).Ok);
        Assert.False((await keeper.Orders.CancelByShopkeeperAsync(w.Keeper1, orderId)).Ok);

        using var customer = _db.As(w.CustA);
        Assert.False((await customer.Orders.CancelByCustomerAsync(w.CustA, orderId)).Ok);

        Assert.Equal(OrderStatus.Fulfilled, (await _db.OrderAsync(orderId)).Status);
        Assert.Equal(3, await _db.StockAsync(w.Item1));
    }

    // ---- Concurrency (checks invariants, so lock errors are tolerated) ---

    [Fact]
    public async Task ConcurrentOrders_ForLastUnit_NeverOversell()
    {
        var w = await SeedAsync(stock: 1);
        var actors = Enumerable.Range(0, 8).Select(_ => _db.As(w.CustA)).ToList();

        try
        {
            var results = await Task.WhenAll(actors.Select(a =>
                Task.Run(() => a.Orders.PlaceAsync(w.CustA, w.Item1, 1))));

            var succeeded = results.Count(r => r.Ok);

            Assert.InRange(succeeded, 0, 1);
            Assert.Equal(1 - succeeded, await _db.StockAsync(w.Item1));
            Assert.Equal(succeeded, await _db.OrderCountAsync());
        }
        finally
        {
            actors.ForEach(a => a.Dispose());
        }
    }

    [Fact]
    public async Task ConcurrentCancels_RestoreStockAtMostOnce()
    {
        var w = await SeedAsync();
        var orderId = await PlaceAsync(w.CustA, w.Item1, 2); // stock 3
        var actors = Enumerable.Range(0, 6).Select(_ => _db.As(w.CustA)).ToList();

        try
        {
            var results = await Task.WhenAll(actors.Select(a =>
                Task.Run(() => a.Orders.CancelByCustomerAsync(w.CustA, orderId))));

            var succeeded = results.Count(r => r.Ok);

            Assert.InRange(succeeded, 0, 1);
            Assert.Equal(3 + 2 * succeeded, await _db.StockAsync(w.Item1));
            Assert.Equal(
                succeeded == 1 ? OrderStatus.Cancelled : OrderStatus.Placed,
                (await _db.OrderAsync(orderId)).Status);
        }
        finally
        {
            actors.ForEach(a => a.Dispose());
        }
    }

    // ---- Recommendation interactions (batch 3) ---------------------------

    [Fact]
    public async Task RecordInterest_PurchaseType_IsIgnored()
    {
        var w = await SeedAsync();
        using var actor = _db.As(w.CustA);

        await actor.Recs.RecordAsync(w.CustA, w.Item1, InteractionType.Purchase);

        Assert.Equal(0, await _db.InterestCountAsync(w.CustA));
    }

    [Fact]
    public async Task RecordInterest_RepeatedWithinWindow_StoresOneRow()
    {
        var w = await SeedAsync();
        using var actor = _db.As(w.CustA);

        for (var i = 0; i < 5; i++)
            await actor.Recs.RecordAsync(w.CustA, w.Item1, InteractionType.View);

        Assert.Equal(1, await _db.InterestCountAsync(w.CustA));
    }

    [Fact]
    public async Task RecordInterest_ForAnotherUser_IsIgnored()
    {
        var w = await SeedAsync();
        using var actor = _db.As(w.CustA);

        await actor.Recs.RecordAsync(w.CustB, w.Item1, InteractionType.View);

        Assert.Equal(0, await _db.InterestCountAsync(w.CustB));
    }
}

public sealed class SmtpFailureTests
{
    private sealed class CapturingProvider : ILoggerProvider
    {
        public List<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(Lines);
        public void Dispose() { }

        private sealed class Capture(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state,
                Exception? ex, Func<TState, Exception?, string> formatter) =>
                lines.Add($"{level}: {formatter(state, ex)} {ex}");
        }
    }

    private const string Password = "s3cret-PW-value!";

    // SmtpEmailSender is internal, so it is created through DI by type name.
    private static (IEmailSender<ApplicationUser> Sender, CapturingProvider Logs) Create(SmtpOptions options)
    {
        var logs = new CapturingProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logs));
        services.AddSingleton(Options.Create(options));

        var type = typeof(SmtpOptions).Assembly.GetType(
            "ShoppingSysem.Components.Account.SmtpEmailSender", throwOnError: true)!;

        var sender = (IEmailSender<ApplicationUser>)ActivatorUtilities.CreateInstance(
            services.BuildServiceProvider(), type);

        return (sender, logs);
    }

    private static readonly ApplicationUser User = new() { Id = "user-1" };

    [Fact]
    public async Task NotConfigured_Throws_AndLogsError()
    {
        var (sender, logs) = Create(new SmtpOptions());

        await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            sender.SendConfirmationLinkAsync(User, "a@test.local", "https://x/confirm"));

        Assert.Contains(logs.Lines, l => l.StartsWith("Error") && l.Contains("not configured"));
    }

    [Fact]
    public async Task UnreachableServer_Throws_WithoutLeakingSmtpDetails()
    {
        var (sender, logs) = Create(new SmtpOptions
        {
            Host = "127.0.0.1",
            Port = 1,
            EnableSsl = false,
            Username = "smtp-user",
            Password = Password,
            FromAddress = "shop@test.local"
        });

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            sender.SendPasswordResetLinkAsync(User, "a@test.local", "https://x/reset"));

        Assert.Null(ex.InnerException);
        Assert.DoesNotContain("127.0.0.1", ex.Message);
        Assert.DoesNotContain(Password, ex.Message);
        Assert.Contains(logs.Lines, l => l.StartsWith("Error"));
        Assert.DoesNotContain(logs.Lines, l => l.Contains(Password));
    }

    [Fact]
    public async Task InvalidRecipient_Throws_InsteadOfReportingSuccess()
    {
        var (sender, _) = Create(new SmtpOptions
        {
            Host = "127.0.0.1",
            Port = 1,
            EnableSsl = false,
            FromAddress = "shop@test.local"
        });

        await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            sender.SendConfirmationLinkAsync(User, "not-an-email", "https://x/confirm"));
    }
}