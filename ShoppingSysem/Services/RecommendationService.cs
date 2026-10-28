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

/// <summary>
/// Content-based recommender.
///
/// 1. Every buyer action is stored as a CustomerInterest row.
/// 2. For the current buyer we build a CATEGORY AFFINITY map:
///        affinity[category] = sum( typeWeight * timeDecay )
///    - Purchase counts more than Wishlist, which counts more than View.
///    - Older actions fade out (half-life = HalfLifeDays).
/// 3. Every item gets:
///        score = 0.7 * normalizedCategoryAffinity + 0.3 * normalizedPopularity
///    Popularity = weighted number of actions all buyers made on that item.
/// 4. Cold start (new buyer, no history): popularity only.
/// </summary>
public class RecommendationService : IRecommendationService
{
    private const double HalfLifeDays = 14;
    private const double AffinityWeight = 0.7;
    private const double PopularityWeight = 0.3;

    private static double TypeWeight(InteractionType t) => t switch
    {
        InteractionType.Purchase => 5.0,
        InteractionType.Wishlist => 3.0,
        _ => 1.0
    };

    private readonly ApplicationDbContext _db;

    public RecommendationService(ApplicationDbContext db) => _db = db;

    public async Task RecordAsync(string userId, int itemId, InteractionType type)
    {
        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId);
        if (item is null) return;

        _db.CustomerInterests.Add(new CustomerInterest
        {
            UserId = userId,
            ItemId = itemId,
            Category = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category!,
            Type = type,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<ScoredItem>> GetRecommendationsAsync(string userId, int take = 12)
    {
        var now = DateTime.UtcNow;

        // --- 1. this buyer's category affinity -------------------------------
        var history = await _db.CustomerInterests
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .Select(i => new { i.Category, i.Type, i.CreatedAt })
            .ToListAsync();

        var affinity = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in history)
        {
            var ageDays = Math.Max(0, (now - h.CreatedAt).TotalDays);
            var decay = Math.Pow(0.5, ageDays / HalfLifeDays);
            affinity[h.Category] = affinity.GetValueOrDefault(h.Category) + TypeWeight(h.Type) * decay;
        }
        var maxAffinity = affinity.Count > 0 ? affinity.Values.Max() : 0;

        // --- 2. global popularity per item ----------------------------------
        var popRows = await _db.CustomerInterests
            .AsNoTracking()
            .GroupBy(i => new { i.ItemId, i.Type })
            .Select(g => new { g.Key.ItemId, g.Key.Type, Count = g.Count() })
            .ToListAsync();

        var popularity = popRows
            .GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Count * TypeWeight(r.Type)));
        var maxPop = popularity.Count > 0 ? popularity.Values.Max() : 0;

        // --- 3. score every item --------------------------------------------
        var items = await _db.Items.AsNoTracking().ToListAsync();
        var hasHistory = maxAffinity > 0;

        var scored = items.Select(item =>
        {
            var cat = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category!;
            var a = hasHistory ? affinity.GetValueOrDefault(cat) / maxAffinity : 0;
            var p = maxPop > 0 ? popularity.GetValueOrDefault(item.Id) / maxPop : 0;

            var score = hasHistory ? AffinityWeight * a + PopularityWeight * p : p;
            var reason = !hasHistory ? "Popular right now"
                       : a > 0.5 ? $"Because you like {cat}"
                       : p > 0.5 ? "Trending with other buyers"
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