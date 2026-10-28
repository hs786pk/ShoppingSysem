using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;

namespace ShoppingSysem.Services;

public sealed record OrderResult(bool Ok, string Message, Order? Order = null);

public interface IOrderService
{
    Task<OrderResult> PlaceAsync(string customerId, int itemId, int quantity = 1);
    Task<List<Order>> GetForCustomerAsync(string customerId);
    Task<List<Order>> GetForShopkeeperAsync(string shopkeeperId);
}

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _db;

    public OrderService(ApplicationDbContext db) => _db = db;

    public async Task<OrderResult> PlaceAsync(string customerId, int itemId, int quantity = 1)
    {
        if (string.IsNullOrEmpty(customerId))
            return new(false, "You must be signed in.");

        if (quantity < 1)
            return new(false, "Quantity must be at least 1.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Not tracked. SaveChanges below must not write Items,
        // or it can put the old stock back.
        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId);
        if (item is null)
            return new(false, "That product no longer exists.");

        // One statement: only decrement if enough stock is still there.
        var taken = await _db.Items
            .Where(i => i.Id == itemId && i.Stock >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Stock, i => i.Stock - quantity));

        if (taken == 0)
        {
            var left = await _db.Items.AsNoTracking()
                .Where(i => i.Id == itemId)
                .Select(i => i.Stock)
                .FirstOrDefaultAsync();

            return new(false, left <= 0
                ? $"{item.Name} is out of stock."
                : $"Only {left} of {item.Name} left.");
        }

        var order = new Order
        {
            CustomerId = customerId,
            ItemId = item.Id,
            ShopId = item.ShopId,
            Quantity = quantity,
            UnitPrice = item.Price,
            TotalPrice = item.Price * quantity,
            OrderedAt = DateTime.UtcNow,
            Status = OrderStatus.Placed
        };

        _db.Orders.Add(order);
        _db.CustomerInterests.Add(new CustomerInterest
        {
            UserId = customerId,
            ItemId = item.Id,
            Category = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category!,
            Type = InteractionType.Purchase,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return new(true, $"Order placed: {quantity} x {item.Name} = Rs. {order.TotalPrice:N0}.", order);
    }

    public Task<List<Order>> GetForCustomerAsync(string customerId) =>
        _db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .Include(o => o.Item)
            .Include(o => o.Shop)
            .OrderByDescending(o => o.OrderedAt)
            .ToListAsync();

    public async Task<List<Order>> GetForShopkeeperAsync(string shopkeeperId)
    {
        var shopIds = await _db.ShopAssignments.AsNoTracking()
            .Where(a => a.ShopkeeperId == shopkeeperId)
            .Select(a => a.ShopId)
            .ToListAsync();

        return await _db.Orders.AsNoTracking()
            .Where(o => shopIds.Contains(o.ShopId))
            .Include(o => o.Item)
            .Include(o => o.Shop)
            .Include(o => o.Customer)
            .OrderByDescending(o => o.OrderedAt)
            .ToListAsync();
    }
}