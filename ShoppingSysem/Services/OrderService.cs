
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;
using System.Data.Common;

namespace ShoppingSysem.Services;

public sealed record OrderResult(bool Ok, string Message, Order? Order = null);

public interface IOrderService
{
    Task<OrderResult> PlaceAsync(string customerId, int itemId, int quantity = 1);
    Task<List<Order>> GetForCustomerAsync(string customerId);
    Task<List<Order>> GetForShopkeeperAsync(string shopkeeperId);
    Task<OrderResult> FulfillAsync(string shopkeeperId, int orderId);
    Task<OrderResult> CancelByShopkeeperAsync(string shopkeeperId, int orderId);
    Task<OrderResult> CancelByCustomerAsync(string customerId, int orderId);
}

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _db;
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        ApplicationDbContext db,
        AuthenticationStateProvider authenticationStateProvider,
        UserManager<ApplicationUser> userManager,
        ILogger<OrderService> logger)
    {
        _db = db;
        _authenticationStateProvider = authenticationStateProvider;
        _userManager = userManager;
        _logger = logger;
    }

    // Always establish identity from the authenticated server-side principal.
    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var authState =
            await _authenticationStateProvider.GetAuthenticationStateAsync();

        if (authState.User.Identity?.IsAuthenticated != true)
            return null;

        return await _userManager.GetUserAsync(authState.User);
    }

    private async Task<bool> HasRoleAsync(
        ApplicationUser user,
        string role)
    {
        return await _userManager.IsInRoleAsync(user, role);
    }

    public async Task<OrderResult> PlaceAsync(
        string customerId,
        int itemId,
        int quantity = 1)
    {
        try
        {
            var result = await PlaceCoreAsync(customerId, itemId, quantity);

            if (result.Ok)
                _logger.LogInformation(
                    "Order {OrderId} placed by {CustomerId}: item {ItemId} x {Quantity}.",
                    result.Order?.Id, customerId, itemId, quantity);

            return result;
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex,
                "Order placement failed for customer {CustomerId}, item {ItemId}.",
                customerId, itemId);
            return new(false, "We could not place your order. Please try again.");
        }
    }

    private async Task<OrderResult> PlaceCoreAsync(
        string customerId,
        int itemId,
        int quantity)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser == null)
            return new(false, "You must be signed in.");

        // Never trust the customer ID supplied by the component.
        if (currentUser.Id != customerId)
        {
            _logger.LogWarning(
                "Order placement denied: caller {CallerId} tried to act as {RequestedId}.",
                currentUser.Id,
                customerId);
            return new(false, "Unauthorized order request.");
        }

        var isCustomer = await HasRoleAsync(currentUser, "Customer");
        var isAdmin = await HasRoleAsync(currentUser, "Admin");

        if (!isCustomer && !isAdmin)
        {
            _logger.LogWarning(
                "Order placement denied: user {UserId} lacks Customer/Admin role.",
                currentUser.Id);
            return new(false, "You are not authorized to place orders.");
        }

        if (itemId <= 0)
            return new(false, "Invalid product.");

        if (quantity < 1)
            return new(false, "Quantity must be at least 1.");

        if (quantity > 10000)
            return new(false, "Requested quantity exceeds the allowed limit.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        var item = await _db.Items
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == itemId);

        if (item is null)
            return new(false, "That product no longer exists.");

        if (item.Price < 0)
            return new(false, "Invalid product price.");

        decimal totalPrice;

        try
        {
            totalPrice = checked(item.Price * quantity);
        }
        catch (OverflowException)
        {
            return new(false, "The order total is invalid.");
        }

        if (item.ShopId <= 0)
            return new(false, "Invalid product shop.");

        // Atomic stock deduction prevents overselling.
        var taken = await _db.Items
            .Where(i =>
                i.Id == itemId &&
                i.ShopId == item.ShopId &&
                i.Stock >= quantity)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(
                    i => i.Stock,
                    i => i.Stock - quantity));

        if (taken == 0)
        {
            var left = await _db.Items
                .AsNoTracking()
                .Where(i => i.Id == itemId)
                .Select(i => (int?)i.Stock)
                .FirstOrDefaultAsync();

            return new(false, left.HasValue
                ? left.Value <= 0
                    ? $"{item.Name} is out of stock."
                    : $"Only {left.Value} of {item.Name} left."
                : "That product is no longer available.");
        }

        var order = new Order
        {
            CustomerId = currentUser.Id,
            ItemId = item.Id,
            ShopId = item.ShopId,
            Quantity = quantity,
            UnitPrice = item.Price,
            TotalPrice = totalPrice,
            OrderedAt = DateTime.UtcNow,
            Status = OrderStatus.Placed
        };

        _db.Orders.Add(order);

        _db.CustomerInterests.Add(new CustomerInterest
        {
            UserId = currentUser.Id,
            ItemId = item.Id,
            Category = string.IsNullOrWhiteSpace(item.Category)
                ? "General"
                : item.Category!,
            Type = InteractionType.Purchase,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return new(
            true,
            $"Order placed: {quantity} x {item.Name} = Rs. {order.TotalPrice:N0}.",
            order);
    }

    public async Task<List<Order>> GetForCustomerAsync(string customerId)
    {
        var currentUser = await GetCurrentUserAsync();

        if (currentUser == null || currentUser.Id != customerId)
        {
            if (currentUser is not null)
            {
                _logger.LogWarning(
                    "Customer order list denied: caller {CallerId} requested {RequestedId}.",
                    currentUser.Id,
                    customerId);
            }
            return new();
        }

        var isCustomer = await HasRoleAsync(currentUser, "Customer");
        var isAdmin = await HasRoleAsync(currentUser, "Admin");

        if (!isCustomer && !isAdmin)
        {
            _logger.LogWarning(
                "Customer order list denied: user {UserId} lacks Customer/Admin role.",
                currentUser.Id);
            return new();
        }

        return await _db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == currentUser.Id)
            .Include(o => o.Item)
            .Include(o => o.Shop)
            .OrderByDescending(o => o.OrderedAt)
            .ToListAsync();
    }

    public async Task<List<Order>> GetForShopkeeperAsync(string shopkeeperId)
    {
        var currentUser = await GetCurrentUserAsync();

        if (currentUser == null || currentUser.Id != shopkeeperId)
        {
            if (currentUser is not null)
            {
                _logger.LogWarning(
                    "Shopkeeper order list denied: caller {CallerId} requested {RequestedId}.",
                    currentUser.Id,
                    shopkeeperId);
            }
            return new();
        }

        if (!await HasRoleAsync(currentUser, "Shopkeeper"))
        {
            _logger.LogWarning(
                "Shopkeeper order list denied: user {UserId} is not a Shopkeeper.",
                currentUser.Id);
            return new();
        }

        var shopIds = await _db.ShopAssignments
            .AsNoTracking()
            .Where(a => a.ShopkeeperId == currentUser.Id)
            .Select(a => a.ShopId)
            .ToListAsync();

        return await _db.Orders
            .AsNoTracking()
            .Where(o => shopIds.Contains(o.ShopId))
            .Include(o => o.Item)
            .Include(o => o.Shop)
            .Include(o => o.Customer)
            .OrderByDescending(o => o.OrderedAt)
            .ToListAsync();
    }

    public async Task<OrderResult> FulfillAsync(
        string shopkeeperId,
        int orderId)
    {
        return await SetStatusAsync(
            shopkeeperId,
            null,
            orderId,
            OrderStatus.Fulfilled);
    }

    public async Task<OrderResult> CancelByShopkeeperAsync(
        string shopkeeperId,
        int orderId)
    {
        return await SetStatusAsync(
            shopkeeperId,
            null,
            orderId,
            OrderStatus.Cancelled);
    }

    public async Task<OrderResult> CancelByCustomerAsync(
        string customerId,
        int orderId)
    {
        return await SetStatusAsync(
            null,
            customerId,
            orderId,
            OrderStatus.Cancelled);
    }

    private async Task<OrderResult> SetStatusAsync(
        string? shopkeeperId,
        string? customerId,
        int orderId,
        OrderStatus next)
    {
        try
        {
            var result = await SetStatusCoreAsync(
                shopkeeperId, customerId, orderId, next);

            if (result.Ok)
                _logger.LogInformation(
                    "Order {OrderId} set to {Status} by {ActorId}.",
                    orderId, next, shopkeeperId ?? customerId);

            return result;
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex,
                "Order {OrderId} status change to {Status} failed.",
                orderId, next);
            return new(false, "We could not update the order. Please try again.");
        }
    }

    private async Task<OrderResult> SetStatusCoreAsync(
        string? shopkeeperId,
        string? customerId,
        int orderId,
        OrderStatus next)
    {
        if (orderId <= 0) return new(false, "Invalid order.");

        var currentUser = await GetCurrentUserAsync();

        if (currentUser == null)
            return new(false, "You must be signed in.");

        if (shopkeeperId != null)
        {
            if (currentUser.Id != shopkeeperId)
            {
                _logger.LogWarning(
                    "Order status change denied: caller {CallerId} tried to act as shopkeeper {RequestedId} on order {OrderId}.",
                    currentUser.Id,
                    shopkeeperId,
                    orderId);
                return new(false, "Unauthorized request.");
            }

            if (!await HasRoleAsync(currentUser, "Shopkeeper"))
            {
                _logger.LogWarning(
                    "Order status change denied: user {UserId} is not a Shopkeeper (order {OrderId}).",
                    currentUser.Id,
                    orderId);
                return new(false, "You are not authorized to manage orders.");
            }
        }
        else if (customerId != null)
        {
            if (currentUser.Id != customerId)
            {
                _logger.LogWarning(
                    "Order status change denied: caller {CallerId} tried to act as customer {RequestedId} on order {OrderId}.",
                    currentUser.Id,
                    customerId,
                    orderId);
                return new(false, "Unauthorized request.");
            }

            var isCustomer = await HasRoleAsync(currentUser, "Customer");
            var isAdmin = await HasRoleAsync(currentUser, "Admin");

            if (!isCustomer && !isAdmin)
            {
                _logger.LogWarning(
                    "Order status change denied: user {UserId} lacks Customer/Admin role (order {OrderId}).",
                    currentUser.Id,
                    orderId);
                return new(false, "You are not authorized to cancel orders.");
            }
        }
        else
        {
            return new(false, "Invalid authorization request.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync();

        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            return new(false, "Order not found.");
        if (order.Quantity < 1 || order.Quantity > 10000)
        {
            return new(
                false,
                "This order has an invalid quantity and cannot be updated.");
        }

        if (customerId != null && order.CustomerId != currentUser.Id)
        {
            _logger.LogWarning(
                "Order status change denied: user {UserId} does not own order {OrderId}.",
                currentUser.Id,
                orderId);
            return new(false, "That order is not yours.");
        }

        if (shopkeeperId != null)
        {
            var allowed = await _db.ShopAssignments
                .AsNoTracking()
                .AnyAsync(a =>
                    a.ShopkeeperId == currentUser.Id &&
                    a.ShopId == order.ShopId);

            if (!allowed)
            {
                _logger.LogWarning(
                    "Order status change denied: shopkeeper {UserId} is not assigned to shop for order {OrderId}.",
                    currentUser.Id,
                    orderId);
                return new(false, "That order is not for your shop.");
            }
        }

        if (order.Status != OrderStatus.Placed)
            return new(false, $"This order is already {order.Status}.");

        // Only one caller can change a placed order.
        var updated = await _db.Orders
            .Where(o =>
                o.Id == orderId &&
                o.Status == OrderStatus.Placed)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(o => o.Status, next));

        if (updated == 0)
            return new(false, "This order was just updated.");

        if (next == OrderStatus.Cancelled)
        {
            // Restore stock only to the original item's original shop.
            // Also prevent integer overflow.
            var restored = await _db.Items
                .Where(i =>
                    i.Id == order.ItemId &&
                    i.ShopId == order.ShopId &&
                    i.Stock <= int.MaxValue - order.Quantity)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(
                        i => i.Stock,
                        i => i.Stock + order.Quantity));

            if (restored == 0)
            {
                // The transaction will roll back the status update too.
                return new(
                    false,
                    "Unable to restore product stock. The order was not cancelled.");
            }
        }

        await tx.CommitAsync();

        var verb = next == OrderStatus.Fulfilled
            ? "fulfilled"
            : "cancelled";

        return new(
            true,
            $"Order #{order.Id} {verb}. Stock was " +
            $"{(next == OrderStatus.Cancelled ? "restored" : "left as is")}.");
    }
}
