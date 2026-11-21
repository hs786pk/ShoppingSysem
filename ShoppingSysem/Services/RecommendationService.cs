using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;

namespace ShoppingSysem.Services;

public record ScoredItem(Item Item, double Score, string Reason);

public interface IRecommendationService
{
    Task RecordAsync(string userId, int itemId, InteractionType type);
    Task<List<ScoredItem>> GetRecommendationsAsync(string userId, int take = 12);
}

public class RecommendationService : IRecommendationService
{
    private const double HalfLifeDays = 14;
    private const double AffinityWeight = 0.7;
    private const double PopularityWeight = 0.3;

    private static double TypeWeight(InteractionType type) => type switch
    {
        InteractionType.Purchase => 5.0,
        InteractionType.Wishlist => 3.0,
        _ => 1.0
    };

    private readonly ApplicationDbContext _db;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(
        ApplicationDbContext db,
        AuthenticationStateProvider authStateProvider,
        UserManager<ApplicationUser> userManager,
        ILogger<RecommendationService> logger)
    {
        _db = db;
        _authStateProvider = authStateProvider;
        _userManager = userManager;
        _logger = logger;
    }

    private async Task<bool> CanAccessUserAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var state = await _authStateProvider.GetAuthenticationStateAsync();

        if (state.User.Identity?.IsAuthenticated != true)
            return false;

        var currentUser = await _userManager.GetUserAsync(state.User);

        if (currentUser is null)
            return false;

        if (currentUser.Id != userId)
        {
            _logger.LogWarning(
                "Recommendation access denied: caller {CallerId} requested user {RequestedId}.",
                currentUser.Id,
                userId);
            return false;
        }

        if (await _userManager.IsInRoleAsync(currentUser, AppRoles.Customer)
            || await _userManager.IsInRoleAsync(currentUser, AppRoles.Admin))
            return true;

        _logger.LogWarning(
            "Recommendation access denied: user {UserId} lacks Customer/Admin role.",
            currentUser.Id);
        return false;
    }

    public async Task RecordAsync(string userId, int itemId, InteractionType type)
    {
        if (!await CanAccessUserAsync(userId))
            return;

        // Purchases are recorded only by OrderService when an order is placed.
        if (itemId <= 0 || type is not (InteractionType.View or InteractionType.Wishlist))
            return;

        try
        {
            var item = await _db.Items
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == itemId);

            if (item is null)
                return;

            // Ignore repeats of the same action within 5 minutes.
            var since = DateTime.UtcNow.AddMinutes(-5);
            if (await _db.CustomerInterests.AnyAsync(i =>
                    i.UserId == userId && i.ItemId == itemId &&
                    i.Type == type && i.CreatedAt > since))
                return;

            _db.CustomerInterests.Add(new CustomerInterest
            {
                UserId = userId,
                ItemId = itemId,
                Category = string.IsNullOrWhiteSpace(item.Category)
                    ? "General"
                    : item.Category!,
                Type = type,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(
                ex,
                "Failed to record interest for user {UserId}, item {ItemId}, type {Type}.",
                userId,
                itemId,
                type);
        }
    }

    public async Task<List<ScoredItem>> GetRecommendationsAsync(string userId, int take = 12)
    {
        if (!await CanAccessUserAsync(userId))
            return new List<ScoredItem>();

        take = Math.Clamp(take, 1, 50);

        var now = DateTime.UtcNow;

        // Cap personal history so affinity scoring stays bounded.
        const int maxHistory = 200;
        var history = await _db.CustomerInterests
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(maxHistory)
            .Select(i => new { i.Category, i.Type, i.CreatedAt })
            .ToListAsync();

        var affinity = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in history)
        {
            var ageDays = Math.Max(0, (now - entry.CreatedAt).TotalDays);
            var decay = Math.Pow(0.5, ageDays / HalfLifeDays);

            affinity[entry.Category] =
                affinity.GetValueOrDefault(entry.Category)
                + TypeWeight(entry.Type) * decay;
        }

        var maxAffinity = affinity.Count > 0
            ? affinity.Values.Max()
            : 0;

        // Aggregate popularity in the database instead of materializing every row.
        var popularity = await _db.CustomerInterests
            .AsNoTracking()
            .GroupBy(i => i.ItemId)
            .Select(g => new
            {
                ItemId = g.Key,
                Score = g.Sum(i =>
                    i.Type == InteractionType.Purchase ? 5.0 :
                    i.Type == InteractionType.Wishlist ? 3.0 : 1.0)
            })
            .ToDictionaryAsync(r => r.ItemId, r => r.Score);

        var maxPop = popularity.Count > 0
            ? popularity.Values.Max()
            : 0;

        // Prefer in-stock items; still allow a small out-of-stock sample for ranking.
        var items = await _db.Items
            .AsNoTracking()
            .OrderByDescending(i => i.Stock > 0)
            .ThenBy(i => i.Name)
            .Take(500)
            .ToListAsync();

        var hasHistory = maxAffinity > 0;

        var scored = items.Select(item =>
        {
            var category = string.IsNullOrWhiteSpace(item.Category)
                ? "General"
                : item.Category!;

            var affinityScore = hasHistory
                ? affinity.GetValueOrDefault(category) / maxAffinity
                : 0;

            var popularityScore = maxPop > 0
                ? popularity.GetValueOrDefault(item.Id) / maxPop
                : 0;

            var score = hasHistory
                ? AffinityWeight * affinityScore
                    + PopularityWeight * popularityScore
                : popularityScore;

            var reason = !hasHistory
                ? "Popular right now"
                : affinityScore > 0.5
                    ? $"Because you like {category}"
                    : popularityScore > 0.5
                        ? "Trending with other buyers"
                        : "You might also like";

            return new ScoredItem(item, score, reason);
        });

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Item.Name)
            .Take(take)
            .ToList();
    }
}