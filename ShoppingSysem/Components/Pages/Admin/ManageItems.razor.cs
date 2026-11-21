using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;

namespace ShoppingSysem.Components.Pages.Admin
{
    public partial class ManageItems
    {
        private const decimal MaxPrice = 100_000_000m;
        private const int MaxStock = 1_000_000;
        private const int MaxCategoryLength = 100;

        [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
        [Inject] private UserManager<ApplicationUser> UserManager { get; set; } = default!;
        [Inject] private ILogger<ManageItems> Logger { get; set; } = default!;

        List<Shop> shops = new();
        List<Item> items = new();

        int selectedShopId = 0;
        string newItemName = "";
        string newItemPrice = "";
        string newItemStock = "";
        string newItemCategory = "";
        string? errorMessage;
        string? adminId;

        List<string> categorySuggestions = new();
        int? pendingDeleteItemId = null;

        private async Task<bool> IsAdminAsync()
        {
            var state = await AuthStateProvider.GetAuthenticationStateAsync();
            var user = await UserManager.GetUserAsync(state.User);
            adminId = user?.Id;

            if (user is not null && await UserManager.IsInRoleAsync(user, AppRoles.Admin))
                return true;

            Logger.LogWarning(
                "Denied admin item operation for user {UserId}.",
                adminId ?? "anonymous");

            return false;
        }

        // Single place for field rules shared by add and update.
        private static string? ValidateFields(decimal price, int stock, string? category)
        {
            if (price < 0 || price > MaxPrice || decimal.Round(price, 2) != price)
                return $"Price must be between 0 and {MaxPrice:N0} with at most 2 decimal places.";

            if (stock < 0 || stock > MaxStock)
                return $"Stock must be between 0 and {MaxStock:N0}.";

            if (category is { Length: > MaxCategoryLength })
                return $"Category cannot exceed {MaxCategoryLength} characters.";

            return null;
        }

        // Saves changes; on failure logs, clears the failed changes so later
        // saves in this circuit are not poisoned, and shows a generic message.
        private async Task<bool> TrySaveAsync(string action)
        {
            try
            {
                await DbContext.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex)
            {
                DbContext.ChangeTracker.Clear();

                Logger.LogError(
                    ex,
                    "Admin {AdminId} failed to {Action}.",
                    adminId,
                    action);

                errorMessage = $"Unable to {action}. Please try again.";
                return false;
            }
        }

        protected override async Task OnInitializedAsync()
        {
            if (!await IsAdminAsync())
            {
                errorMessage = "You are not authorized to manage items.";
                return;
            }

            shops = await DbContext.Shops
                .AsNoTracking()
                .OrderBy(s => s.Name)
                .ToListAsync();

            await LoadCategorySuggestions();
        }

        private async Task LoadCategorySuggestions()
        {
            categorySuggestions = await DbContext.Items
                .AsNoTracking()
                .Where(i => i.Category != null && i.Category != "")
                .Select(i => i.Category!)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();
        }

        async Task LoadItems()
        {
            if (!await IsAdminAsync())
            {
                items = new();
                errorMessage = "You are not authorized to manage items.";
                return;
            }

            if (selectedShopId <= 0 ||
                !await DbContext.Shops.AnyAsync(s => s.Id == selectedShopId))
            {
                items = new();
                return;
            }

            items = await DbContext.Items
                .AsNoTracking()
                .Where(i => i.ShopId == selectedShopId)
                .OrderBy(i => i.Name)
                .ToListAsync();
        }

        async Task AddItem()
        {
            errorMessage = null;

            if (!await IsAdminAsync())
            {
                errorMessage = "You are not authorized to add items.";
                return;
            }

            if (selectedShopId <= 0 ||
                !await DbContext.Shops.AnyAsync(s => s.Id == selectedShopId))
            {
                errorMessage = "Select a valid shop.";
                return;
            }

            if (string.IsNullOrWhiteSpace(newItemName))
            {
                errorMessage = "Item name is required.";
                return;
            }

            var name = newItemName.Trim();

            if (name.Length > 100)
            {
                errorMessage = "Item name cannot exceed 100 characters.";
                return;
            }

            if (!decimal.TryParse(newItemPrice, out var price))
            {
                errorMessage = "Enter a valid price.";
                return;
            }

            if (!int.TryParse(newItemStock, out var stock))
            {
                errorMessage = "Enter a valid stock quantity.";
                return;
            }

            var category = string.IsNullOrWhiteSpace(newItemCategory)
                ? null
                : newItemCategory.Trim();

            var fieldError = ValidateFields(price, stock, category);

            if (fieldError is not null)
            {
                errorMessage = fieldError;
                return;
            }
            if (await DbContext.Items.AnyAsync(i =>
                  i.ShopId == selectedShopId && i.Name == name))
            {
                errorMessage = "An item with this name already exists in the shop.";
                return;
            }
            var item = new Item
            {
                Name = name,
                Price = price,
                Stock = stock,
                Category = category,
                ShopId = selectedShopId
            };

            DbContext.Items.Add(item);

            if (!await TrySaveAsync("add the item"))
                return;

            Logger.LogInformation(
                "Admin {AdminId} created item {ItemId} in shop {ShopId}.",
                adminId,
                item.Id,
                item.ShopId);

            newItemName = "";
            newItemPrice = "";
            newItemStock = "";
            newItemCategory = "";

            await LoadItems();
            await LoadCategorySuggestions();
        }

        async Task UpdateItem(Item item)
        {
            errorMessage = null;

            if (!await IsAdminAsync())
            {
                errorMessage = "You are not authorized to update items.";
                return;
            }

            if (item is null || item.Id <= 0 || selectedShopId <= 0)
            {
                errorMessage = "Invalid item selection.";
                return;
            }

            var category = string.IsNullOrWhiteSpace(item.Category)
                ? null
                : item.Category.Trim();

            var fieldError = ValidateFields(item.Price, item.Stock, category);

            if (fieldError is not null)
            {
                errorMessage = fieldError;
                return;
            }

            // Load a fresh tracked entity and update only fields editable in the UI.
            var existingItem = await DbContext.Items
                .FirstOrDefaultAsync(i =>
                    i.Id == item.Id && i.ShopId == selectedShopId);

            if (existingItem is null)
            {
                errorMessage = "The item was not found in the selected shop.";
                return;
            }

            existingItem.Price = item.Price;
            existingItem.Stock = item.Stock;
            existingItem.Category = category;

            if (!await TrySaveAsync("update the item"))
                return;

            Logger.LogInformation(
                "Admin {AdminId} updated item {ItemId} in shop {ShopId}.",
                adminId,
                existingItem.Id,
                existingItem.ShopId);

            await LoadItems();
            await LoadCategorySuggestions();
        }

        void RequestDeleteItem(int itemId)
        {
            pendingDeleteItemId = itemId;
            errorMessage = null;
        }

        void CancelDeleteItem()
        {
            pendingDeleteItemId = null;
        }

        async Task ConfirmDeleteItem(Item item)
        {
            errorMessage = null;

            if (!await IsAdminAsync())
            {
                errorMessage = "You are not authorized to delete items.";
                pendingDeleteItemId = null;
                return;
            }

            if (item is null ||
                item.Id <= 0 ||
                item.Id != pendingDeleteItemId ||
                selectedShopId <= 0)
            {
                errorMessage = "Invalid item selection.";
                pendingDeleteItemId = null;
                return;
            }

            var existingItem = await DbContext.Items
                .FirstOrDefaultAsync(i =>
                    i.Id == item.Id && i.ShopId == selectedShopId);

            if (existingItem is null)
            {
                errorMessage = "The item was not found in the selected shop.";
                pendingDeleteItemId = null;
                return;
            }

            var hasSales = await DbContext.Sales
                .AnyAsync(s => s.ItemId == existingItem.Id);

            if (hasSales)
            {
                errorMessage = $"Cannot delete '{existingItem.Name}': it has recorded sales history.";
                pendingDeleteItemId = null;
                return;
            }

            var hasOrders = await DbContext.Orders
                .AnyAsync(o => o.ItemId == existingItem.Id);

            if (hasOrders)
            {
                errorMessage = $"Cannot delete '{existingItem.Name}': it has order history.";
                pendingDeleteItemId = null;
                return;
            }

            var deletedId = existingItem.Id;
            DbContext.Items.Remove(existingItem);

            pendingDeleteItemId = null;

            if (!await TrySaveAsync("delete the item"))
                return;

            Logger.LogInformation(
                "Admin {AdminId} deleted item {ItemId}.",
                adminId,
                deletedId);

            await LoadItems();
        }
    }
}