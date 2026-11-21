using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;

namespace ShoppingSysem.Services;

public interface IShopAccessService
{
    Task<bool> IsShopkeeperAssignedAsync(string shopkeeperId, int shopId);
    Task<List<int>> GetAssignedShopIdsAsync(string shopkeeperId);
}

/// <summary>
/// Central place for shop-assignment checks so pages and services
/// cannot drift on cross-shop access rules.
/// </summary>
public sealed class ShopAccessService : IShopAccessService
{
    private readonly ApplicationDbContext _db;

    public ShopAccessService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<bool> IsShopkeeperAssignedAsync(string shopkeeperId, int shopId)
    {
        if (string.IsNullOrWhiteSpace(shopkeeperId) || shopId <= 0)
            return false;

        return await _db.ShopAssignments
            .AsNoTracking()
            .AnyAsync(a => a.ShopkeeperId == shopkeeperId && a.ShopId == shopId);
    }

    public async Task<List<int>> GetAssignedShopIdsAsync(string shopkeeperId)
    {
        if (string.IsNullOrWhiteSpace(shopkeeperId))
            return new List<int>();

        return await _db.ShopAssignments
            .AsNoTracking()
            .Where(a => a.ShopkeeperId == shopkeeperId)
            .Select(a => a.ShopId)
            .ToListAsync();
    }
}
