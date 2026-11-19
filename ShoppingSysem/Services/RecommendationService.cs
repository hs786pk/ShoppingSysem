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

        var history = await _db.CustomerInterests
            .AsNoTracking()
            .Where(i => i.UserId == userId)
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

        var popRows = await _db.CustomerInterests
            .AsNoTracking()
            .GroupBy(i => new { i.ItemId, i.Type })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.Type,
                Count = g.Count()
            })
            .ToListAsync();

        var popularity = popRows
            .GroupBy(r => r.ItemId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(r => r.Count * TypeWeight(r.Type)));

        var maxPop = popularity.Count > 0
            ? popularity.Values.Max()
            : 0;

        var items = await _db.Items
            .AsNoTracking()
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