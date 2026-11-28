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
        // Limits come from ValidationLimits so forms stay aligned with models.

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
        string newItemSubcategory = "";
        string newItemSku = "";
        string newItemBrand = "";
        string newItemShortDescription = "";
        string newItemFullDescription = "";
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
        private static string? ValidateFields(decimal price, int stock, string? category, string? subcategory, string? sku, string? brand, string? shortDescription)
        {
            if (price < 0 || price > ValidationLimits.MaxPrice || decimal.Round(price, 2) != price)
                return $"Price must be between 0 and {ValidationLimits.MaxPrice:N0} with at most 2 decimal places.";

            if (stock < 0 || stock > ValidationLimits.MaxStock)
                return $"Stock must be between 0 and {ValidationLimits.MaxStock:N0}.";

            if (category is { Length: > ValidationLimits.CategoryMaxLength })
                return $"Category cannot exceed {ValidationLimits.CategoryMaxLength} characters.";

            if (subcategory is { Length: > ValidationLimits.CategoryMaxLength })
                return $"Subcategory cannot exceed {ValidationLimits.CategoryMaxLength} characters.";
            if (string.IsNullOrWhiteSpace(sku) || sku.Length > ValidationLimits.SkuMaxLength)
                return $"SKU is required and cannot exceed {ValidationLimits.SkuMaxLength} characters.";
            if (brand is { Length: > ValidationLimits.BrandMaxLength })
                return $"Brand cannot exceed {ValidationLimits.BrandMaxLength} characters.";
            if (shortDescription is { Length: > ValidationLimits.ShortDescriptionMaxLength })
                return $"Short description cannot exceed {ValidationLimits.ShortDescriptionMaxLength} characters.";
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

            if (name.Length > ValidationLimits.ItemNameMaxLength)
            {
                errorMessage = $"Item name cannot exceed {ValidationLimits.ItemNameMaxLength} characters.";
                return;
            }

            if (string.IsNullOrWhiteSpace(newItemPrice) ||
                !decimal.TryParse(newItemPrice.Trim(), out var price))
            {
                errorMessage = "Enter a valid price.";
                return;
            }

            if (string.IsNullOrWhiteSpace(newItemStock) ||
                !int.TryParse(newItemStock.Trim(), out var stock))
            {
                errorMessage = "Enter a valid stock quantity.";
                return;
            }

            var category = string.IsNullOrWhiteSpace(newItemCategory)
                ? null
                : newItemCategory.Trim();

            var sku = newItemSku.Trim();
            var brand = string.IsNullOrWhiteSpace(newItemBrand) ? null : newItemBrand.Trim();
            var subcategory = string.IsNullOrWhiteSpace(newItemSubcategory) ? null : newItemSubcategory.Trim();
            var shortDescription = string.IsNullOrWhiteSpace(newItemShortDescription) ? null : newItemShortDescription.Trim();
            var fullDescription = string.IsNullOrWhiteSpace(newItemFullDescription) ? null : newItemFullDescription.Trim();
            var fieldError = ValidateFields(price, stock, category, subcategory, sku, brand, shortDescription);

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
            if (await DbContext.Items.AnyAsync(i => i.ShopId == selectedShopId && i.SKU == sku))
            {
                errorMessage = "This SKU is already used by another product in the selected shop.";
                return;
            }

            var item = new Item
            {
                Name = name,
                SKU = sku,
                Brand = brand,
                ShortDescription = shortDescription,
                FullDescription = fullDescription,
                Subcategory = subcategory,
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
            newItemSubcategory = "";
            newItemSku = "";
            newItemBrand = "";
            newItemShortDescription = "";
            newItemFullDescription = "";

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

            var subcategory = string.IsNullOrWhiteSpace(item.Subcategory) ? null : item.Subcategory.Trim();
            var sku = item.SKU?.Trim() ?? string.Empty;
            var brand = string.IsNullOrWhiteSpace(item.Brand) ? null : item.Brand.Trim();
            var shortDescription = string.IsNullOrWhiteSpace(item.ShortDescription) ? null : item.ShortDescription.Trim();
            var fieldError = ValidateFields(item.Price, item.Stock, category, subcategory, sku, brand, shortDescription);

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

            if (await DbContext.Items.AnyAsync(i => i.ShopId == selectedShopId && i.Id != item.Id && i.SKU == sku))
            {
                errorMessage = "This SKU is already used by another product in the selected shop.";
                return;
            }

            existingItem.SKU = sku;
            existingItem.Brand = brand;
            existingItem.ShortDescription = shortDescription;
            existingItem.FullDescription = item.FullDescription;
            existingItem.Subcategory = subcategory;
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