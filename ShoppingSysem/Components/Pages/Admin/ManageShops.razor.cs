using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;

namespace ShoppingSysem.Components.Pages.Admin
{
    public partial class ManageShops
    {
        [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
        [Inject] private UserManager<ApplicationUser> UserManager { get; set; } = default!;

        [Inject] private ILogger<ManageShops> Logger { get; set; } = default!;

        List<Shop> shops = new();
        string? adminId;
        string newShopName = "";
        string newShopLocation = "";
        string? errorMessage;
        int? pendingDeleteId = null;

        private async Task<bool> IsAdminAsync()
        {
            var state = await AuthStateProvider.GetAuthenticationStateAsync();
            var user = await UserManager.GetUserAsync(state.User);

            adminId = user?.Id;

            if (user is not null && await UserManager.IsInRoleAsync(user, AppRoles.Admin))
                return true;

            Logger.LogWarning(
                "Denied admin shop operation for user {UserId}.",
                adminId ?? "anonymous");

            return false;
        }

        protected override async Task OnInitializedAsync()
        {
            if (!await IsAdminAsync())
            {
                errorMessage = "You are not authorized to manage shops.";
                return;
            }

            await LoadShops();
        }

        async Task LoadShops()
        {
            if (!await IsAdminAsync())
            {
                shops = new();
                errorMessage = "You are not authorized to manage shops.";
                return;
            }

            shops = await DbContext.Shops
                .AsNoTracking()
                .OrderBy(s => s.Name)
                .ToListAsync();
        }

        async Task AddShop()
        {
            errorMessage = null;

            if (!await IsAdminAsync())
            {
                errorMessage = "You are not authorized to add shops.";
                return;
            }

            var name = newShopName?.Trim() ?? "";
            var location = newShopLocation?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(name))
            {
                errorMessage = "Shop name is required.";
                return;
            }

            if (name.Length > 100)
            {
                errorMessage = "Shop name cannot exceed 100 characters.";
                return;
            }

            if (location.Length > 200)
            {
                errorMessage = "Location cannot exceed 200 characters.";
                return;
            }
            if (await DbContext.Shops.AnyAsync(s =>
                    s.Name == name && s.Location == location))
            {
                errorMessage = "A shop with this name and location already exists.";
                return;
            }
            var shop = new Shop { Name = name, Location = location };
            DbContext.Shops.Add(shop);

            try
            {
                await DbContext.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                DbContext.ChangeTracker.Clear();
                Logger.LogError(ex, "Admin {AdminId} failed to create a shop.", adminId);
                errorMessage = "Unable to add the shop. Please try again.";
                return;
            }

            Logger.LogInformation("Admin {AdminId} created shop {ShopId}.", adminId, shop.Id);
            newShopName = "";
            newShopLocation = "";

            await LoadShops();
        }

        void RequestDelete(int shopId)
        {
            pendingDeleteId = shopId;
            errorMessage = null;
        }

        void CancelDelete()
        {
            pendingDeleteId = null;
        }

        async Task ConfirmDelete(Shop shop)
        {
            errorMessage = null;

            if (!await IsAdminAsync())
            {
                errorMessage = "You are not authorized to delete shops.";
                pendingDeleteId = null;
                return;
            }

            if (shop is null ||
                shop.Id <= 0 ||
                shop.Id != pendingDeleteId)
            {
                errorMessage = "Invalid shop selection.";
                pendingDeleteId = null;
                return;
            }

            var existingShop = await DbContext.Shops
                .FirstOrDefaultAsync(s => s.Id == shop.Id);

            if (existingShop is null)
            {
                errorMessage = "The selected shop no longer exists.";
                pendingDeleteId = null;
                return;
            }

            var hasItems = await DbContext.Items
                .AnyAsync(i => i.ShopId == existingShop.Id);

            var hasAssignments = await DbContext.ShopAssignments
                .AnyAsync(a => a.ShopId == existingShop.Id);

            var hasOrders = await DbContext.Orders
                .AnyAsync(o => o.ShopId == existingShop.Id);

            var hasSales = await DbContext.Sales
                .AnyAsync(s => s.ShopId == existingShop.Id);

            if (hasItems || hasAssignments || hasOrders || hasSales)
            {
                errorMessage =
                    $"Cannot delete '{existingShop.Name}': it still has items, assignments, orders, or sales history.";

                pendingDeleteId = null;
                return;
            }

            try
            {
                DbContext.Shops.Remove(existingShop);
                await DbContext.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                DbContext.ChangeTracker.Clear();
                Logger.LogError(ex, "Admin {AdminId} failed to delete shop {ShopId}.", adminId, existingShop.Id);
                errorMessage = $"Unable to delete '{existingShop.Name}'. Please try again.";
                pendingDeleteId = null;
                return;
            }

            Logger.LogInformation("Admin {AdminId} deleted shop {ShopId}.", adminId, existingShop.Id);
            pendingDeleteId = null;
            await LoadShops();
        }
    }
}