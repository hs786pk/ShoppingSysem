using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;
using ShoppingSysem.Services;
using System.Security.Claims;

namespace ShoppingSysem.Components.Pages.Customer
{
    public partial class BuyerHome
    {
        [Inject] private AuthenticationStateProvider AuthProvider { get; set; } = default!;
        [Inject] private IRecommendationService Recommender { get; set; } = default!;
        [Inject] private IOrderService Orders { get; set; } = default!;
        [Inject] private ApplicationDbContext Db { get; set; } = default!;

        private string userId = string.Empty;
        private string search = string.Empty;
        private string selectedCategory = string.Empty;
        private string? message;
        private bool messageIsError;
        private bool busy;

        private List<Item> allItems = new();
        private List<string> categories = new();
        private List<ScoredItem> recommended = new();

        private IEnumerable<Item> FilteredQuery =>
            allItems.Where(i =>
                (string.IsNullOrWhiteSpace(search) ||
                    i.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(selectedCategory) ||
                    string.Equals(i.Category ?? "General", selectedCategory, StringComparison.OrdinalIgnoreCase)));

        private List<Item> filtered => FilteredQuery.ToList();

        private List<CatalogPiece> RecommendedPieces =>
            recommended.Select((r, i) => new CatalogPiece(r.Item, r.Reason, i + 1)).ToList();

        private List<CatalogPiece> FloorPieces =>
            filtered.Select((item, i) => new CatalogPiece(item, null, i + 1)).ToList();

        protected override async Task OnInitializedAsync()
        {
            var state = await AuthProvider.GetAuthenticationStateAsync();
            userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

            await ReloadItems();
            await LoadRecommendations();
        }

        private async Task ReloadItems()
        {
            // Soft catalog cap keeps the buyer page responsive as inventory grows.
            const int maxCatalogItems = 1000;

            allItems = await Db.Items
                .AsNoTracking()
                .OrderBy(i => i.Name)
                .Take(maxCatalogItems)
                .ToListAsync();

            categories = allItems
                .Select(i => i.Category ?? "General")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
        }

        private async Task LoadRecommendations()
        {
            recommended = await Recommender.GetRecommendationsAsync(userId, 8);
        }

        private Task RecordView(Item item) => Act(item, InteractionType.View);

        private Task RecordSave(Item item) => Act(item, InteractionType.Wishlist);

        private Task PlaceOrder(Item item) => Buy(item);

        private async Task Act(Item item, InteractionType type)
        {
            if (string.IsNullOrEmpty(userId) || item.Id <= 0) return;

            try
            {
                await Recommender.RecordAsync(userId, item.Id, type);

                messageIsError = false;
                message = type switch
                {
                    InteractionType.Wishlist => $"{item.Name} saved.",
                    _ => $"Viewing {item.Name}."
                };

                await LoadRecommendations();
            }
            catch (Exception)
            {
                messageIsError = true;
                message = "Could not record that action. Please try again.";
            }
        }

        private async Task Buy(Item item)
        {
            if (busy || string.IsNullOrEmpty(userId) || item.Id <= 0 || item.Stock <= 0) return;

            busy = true;
            try
            {
                var result = await Orders.PlaceAsync(userId, item.Id, 1);
                message = result.Message;
                messageIsError = !result.Ok;

                await ReloadItems();
                await LoadRecommendations();
            }
            catch (Exception)
            {
                messageIsError = true;
                message = "We could not place your order. Please try again.";
            }
            finally
            {
                busy = false;
            }
        }

        private sealed record CatalogPiece(Item Item, string? Reason, int Index);
    }
}
