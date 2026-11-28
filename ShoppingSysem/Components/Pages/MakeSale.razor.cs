using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;
using System.Data.Common;

namespace ShoppingSysem.Components.Pages
{
    public partial class MakeSale
    {
        [CascadingParameter]
        private Task<AuthenticationState>? AuthState { get; set; }

        [Inject]
        private ILogger<MakeSale> Logger { get; set; } = default!;

        [Inject]
        private ShoppingSysem.Services.IShopAccessService ShopAccess { get; set; } = default!;

        List<Shop> myShops = new();
        List<Item> items = new();
        List<ProductVariant> variants = new();

        int selectedShopId = 0;
        int selectedItemId = 0;
        int selectedVariantId = 0;
        string quantity = "";

        string? errorMessage;
        string? successMessage;

        private async Task<ShoppingSysem.Data.ApplicationUser?> GetCurrentShopkeeperAsync()
        {
            if (AuthState is null)
                return null;

            var authState = await AuthState;
            var user = await UserManager.GetUserAsync(authState.User);

            if (user is null || !await UserManager.IsInRoleAsync(user, AppRoles.Shopkeeper))
                return null;

            return user;
        }

        private Task<bool> IsAssignedToShopAsync(string userId, int shopId)
            => ShopAccess.IsShopkeeperAssignedAsync(userId, shopId);

        // "Large / Red"; falls back to the SKU when the variant has no options.
        private static string VariantDisplay(ProductVariant v)
        {
            var label = string.Join(" / ", new[] { v.Size, v.Colour, v.Model, v.Material }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
            return label.Length > 0 ? label : v.SKU;
        }

        protected override async Task OnInitializedAsync()
        {
            var user = await GetCurrentShopkeeperAsync();

            if (user is null)
            {
                errorMessage = "You are not authorized to make sales.";
                return;
            }

            var assignedShopIds = await ShopAccess.GetAssignedShopIdsAsync(user.Id);

            myShops = assignedShopIds.Count == 0
                ? new List<Shop>()
                : await DbContext.Shops
                    .AsNoTracking()
                    .Where(s => assignedShopIds.Contains(s.Id))
                    .OrderBy(s => s.Name)
                    .ToListAsync();
        }

        async Task LoadItems()
        {
            errorMessage = null;
            successMessage = null;
            selectedItemId = 0;
            selectedVariantId = 0;
            variants = new();

            var user = await GetCurrentShopkeeperAsync();

            if (user is null)
            {
                items = new();
                errorMessage = "You are not authorized to view these items.";
                return;
            }

            if (selectedShopId <= 0 ||
                !await IsAssignedToShopAsync(user.Id, selectedShopId))
            {
                items = new();
                selectedShopId = 0;
                errorMessage = "You are not assigned to the selected shop.";
                return;
            }

            items = await DbContext.Items
                .AsNoTracking()
                .Where(i => i.ShopId == selectedShopId)
                .OrderBy(i => i.Name)
                .ToListAsync();
        }

        async Task LoadVariants()
        {
            errorMessage = null;
            successMessage = null;
            selectedVariantId = 0;

            variants = selectedItemId > 0
                ? await DbContext.ProductVariants
                    .AsNoTracking()
                    .Where(v => v.ItemId == selectedItemId && v.IsActive)
                    .OrderBy(v => v.SKU)
                    .ToListAsync()
                : new();
        }

        async Task ConfirmSale()
        {
            try
            {
                await ConfirmSaleCore();
            }
            catch (Exception ex) when (ex is DbUpdateException or DbException)
            {
                DbContext.ChangeTracker.Clear();
                Logger.LogError(ex,
                    "Sale failed for shop {ShopId}, item {ItemId}.",
                    selectedShopId, selectedItemId);
                errorMessage = "The sale could not be recorded. Please try again.";
            }
        }

        async Task ConfirmSaleCore()
        {
            errorMessage = null; successMessage = null;

            var user = await GetCurrentShopkeeperAsync();

            if (user is null)
            {
                errorMessage = "You are not authorized to make sales.";
                return;
            }

            if (selectedShopId <= 0 ||
                !await IsAssignedToShopAsync(user.Id, selectedShopId))
            {
                errorMessage = "You are not assigned to the selected shop.";
                return;
            }

            if (selectedItemId <= 0)
            {
                errorMessage = "Please select an item.";
                return;
            }

            if (string.IsNullOrWhiteSpace(quantity) ||
                quantity.Trim().Length > 10 ||
                !int.TryParse(quantity.Trim(), out var qty) ||
                qty < 1)
            {
                errorMessage = "Enter a valid quantity.";
                return;
            }

            if (qty > ValidationLimits.MaxQuantityPerTransaction)
            {
                errorMessage = $"Quantity cannot exceed {ValidationLimits.MaxQuantityPerTransaction:N0} units.";
                return;
            }
            await using var tx = await DbContext.Database.BeginTransactionAsync();

            var item = await DbContext.Items
                .AsNoTracking()
                .FirstOrDefaultAsync(i =>
                    i.Id == selectedItemId &&
                    i.ShopId == selectedShopId);

            if (item is null)
            {
                await tx.RollbackAsync();
                errorMessage = "The selected item does not belong to this shop.";
                return;
            }

            // Products with active variants cannot be sold without choosing one.
            var hasVariants = await DbContext.ProductVariants
                .AnyAsync(v => v.ItemId == item.Id && v.IsActive);

            ProductVariant? variant = null;

            if (hasVariants)
            {
                if (selectedVariantId <= 0)
                {
                    await tx.RollbackAsync();
                    errorMessage = "Please select an option.";
                    return;
                }

                variant = await DbContext.ProductVariants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v =>
                        v.Id == selectedVariantId &&
                        v.ItemId == item.Id &&
                        v.IsActive);

                if (variant is null)
                {
                    await tx.RollbackAsync();
                    errorMessage = "That option is not available.";
                    return;
                }
            }

            var unitPrice = variant?.Price ?? item.Price;

            if (unitPrice < 0 ||
                unitPrice > ValidationLimits.MaxPrice ||
                decimal.Round(unitPrice, 2) != unitPrice)
            {
                await tx.RollbackAsync();
                errorMessage = "This item has an invalid price and cannot be sold.";
                return;
            }

            decimal totalPrice;

            try
            {
                totalPrice = checked(unitPrice * qty);
            }
            catch (OverflowException)
            {
                await tx.RollbackAsync();
                errorMessage = "The sale total is too large.";
                return;
            }

            // Atomically decrement stock (variant-level when a variant is chosen,
            // item-level otherwise) only if sufficient stock remains.
            int updated;
            if (variant is not null)
            {
                var variantId = variant.Id;
                updated = await DbContext.ProductVariants
                    .Where(v =>
                        v.Id == variantId &&
                        v.ItemId == item.Id &&
                        v.IsActive &&
                        v.Stock >= qty)
                    .ExecuteUpdateAsync(setters =>
                        setters.SetProperty(v => v.Stock, v => v.Stock - qty));
            }
            else
            {
                updated = await DbContext.Items
                    .Where(i =>
                        i.Id == item.Id &&
                        i.ShopId == selectedShopId &&
                        i.Stock >= qty)
                    .ExecuteUpdateAsync(setters =>
                        setters.SetProperty(i => i.Stock, i => i.Stock - qty));
            }

            if (updated == 0)
            {
                await tx.RollbackAsync();

                var remainingStock = variant is not null
                    ? await DbContext.ProductVariants
                        .AsNoTracking()
                        .Where(v => v.Id == variant.Id)
                        .Select(v => (int?)v.Stock)
                        .FirstOrDefaultAsync()
                    : await DbContext.Items
                        .AsNoTracking()
                        .Where(i => i.Id == item.Id && i.ShopId == selectedShopId)
                        .Select(i => (int?)i.Stock)
                        .FirstOrDefaultAsync();

                errorMessage = remainingStock is null
                    ? "The selected item no longer exists."
                    : $"Not enough stock. Only {remainingStock.Value} available.";

                return;
            }

            var variantLabel = variant is null ? null : VariantDisplay(variant);

            var sale = new Sale
            {
                ItemId = item.Id,
                VariantId = variant?.Id,
                VariantLabel = variantLabel,
                QuantitySold = qty,
                TotalPrice = totalPrice,
                SaleDate = DateTime.UtcNow,
                ShopkeeperId = user.Id,
                ShopId = item.ShopId
            };

            DbContext.Sales.Add(sale);
            await DbContext.SaveChangesAsync();

            await tx.CommitAsync();
            Logger.LogInformation(
                "Shopkeeper {ShopkeeperId} recorded sale {SaleId}: item {ItemId} variant {VariantId} x {Quantity}, shop {ShopId}.",
                user.Id, sale.Id, item.Id, variant?.Id, qty, item.ShopId);

            selectedItemId = 0;
            selectedVariantId = 0;
            quantity = "";

            await LoadItems();

            // Set after LoadItems so the success message is not cleared by the reload.
            successMessage =
                $"Sale recorded: {qty} x {item.Name}{(variantLabel is null ? "" : $" ({variantLabel})")} = Rs. {sale.TotalPrice:N2}";
        }
    }
}