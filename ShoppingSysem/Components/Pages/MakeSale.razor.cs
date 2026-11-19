using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
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
        List<Shop> myShops = new();
        List<Item> items = new();

        int selectedShopId = 0;
        int selectedItemId = 0;
        string quantity = "";

        string? errorMessage;
        string? successMessage;

        private async Task<ShoppingSysem.Data.ApplicationUser?> GetCurrentShopkeeperAsync()
        {
            if (AuthState is null)
                return null;

            var authState = await AuthState;
            var user = await UserManager.GetUserAsync(authState.User);

            if (user is null || !await UserManager.IsInRoleAsync(user, "Shopkeeper"))
                return null;

            return user;
        }

        private async Task<bool> IsAssignedToShopAsync(string userId, int shopId)
        {
            return await DbContext.ShopAssignments
                .AnyAsync(a => a.ShopkeeperId == userId && a.ShopId == shopId);
        }

        protected override async Task OnInitializedAsync()
        {
            var user = await GetCurrentShopkeeperAsync();

            if (user is null)
            {
                errorMessage = "You are not authorized to make sales.";
                return;
            }

            myShops = await DbContext.ShopAssignments
                .AsNoTracking()
                .Where(a => a.ShopkeeperId == user.Id)
                .Include(a => a.Shop)
                .Select(a => a.Shop)
                .OrderBy(s => s.Name)
                .ToListAsync();
        }

        async Task LoadItems()
        {
            errorMessage = null;
            successMessage = null;
            selectedItemId = 0;

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

            if (!int.TryParse(quantity, out var qty) || qty < 1)
            {
                errorMessage = "Enter a valid quantity.";
                return;
            }

            if (qty > 10000)
            {
                errorMessage = "Quantity cannot exceed 10,000 units.";
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

            if (item.Price < 0 ||
                item.Price > 100_000_000m ||
                decimal.Round(item.Price, 2) != item.Price)
            {
                await tx.RollbackAsync();
                errorMessage = "This item has an invalid price and cannot be sold.";
                return;
            }

            decimal totalPrice;

            try
            {
                totalPrice = checked(item.Price * qty);
            }
            catch (OverflowException)
            {
                await tx.RollbackAsync();
                errorMessage = "The sale total is too large.";
                return;
            }

            // Atomically decrement stock only if sufficient stock remains.
            var updated = await DbContext.Items
                .Where(i =>
                    i.Id == item.Id &&
                    i.ShopId == selectedShopId &&
                    i.Stock >= qty)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(i => i.Stock, i => i.Stock - qty));

            if (updated == 0)
            {
                await tx.RollbackAsync();

                var remainingStock = await DbContext.Items
                    .AsNoTracking()
                    .Where(i => i.Id == item.Id && i.ShopId == selectedShopId)
                    .Select(i => (int?)i.Stock)
                    .FirstOrDefaultAsync();

                errorMessage = remainingStock is null
                    ? "The selected item no longer exists."
                    : $"Not enough stock. Only {remainingStock.Value} available.";

                return;
            }

            var sale = new Sale
            {
                ItemId = item.Id,
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
                "Shopkeeper {ShopkeeperId} recorded sale {SaleId}: item {ItemId} x {Quantity}, shop {ShopId}.",
                user.Id, sale.Id, item.Id, qty, item.ShopId);

            selectedItemId = 0;
            quantity = "";

            await LoadItems();

            // Set after LoadItems so the success message is not cleared by the reload.
            successMessage =
                $"Sale recorded: {qty} x {item.Name} = Rs. {sale.TotalPrice:N2}";
        }
    }
}