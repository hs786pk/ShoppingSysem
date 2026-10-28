using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Models;

namespace ShoppingSysem.Components.Pages.Admin
{
    public partial class ManageShops
    {
        List<Shop> shops = new();
        string newShopName = "";
        string newShopLocation = "";
        string? errorMessage;
        int? pendingDeleteId = null; // tracks which row is showing "Confirm delete?"

        protected override async Task OnInitializedAsync()
        {
            await LoadShops();
        }

        async Task LoadShops()
        {
            shops = await DbContext.Shops.ToListAsync();
        }

        async Task AddShop()
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(newShopName))
            {
                errorMessage = "Shop name is required.";
                return;
            }

            var shop = new Shop
            {
                Name = newShopName.Trim(),
                Location = newShopLocation.Trim()
            };

            DbContext.Shops.Add(shop);
            await DbContext.SaveChangesAsync();

            newShopName = "";
            newShopLocation = "";

            await LoadShops();
        }

        void RequestDelete(int shopId)
        {
            pendingDeleteId = shopId;
        }

        void CancelDelete()
        {
            pendingDeleteId = null;
        }

        async Task ConfirmDelete(Shop shop)
        {
            errorMessage = null;

            var hasItems = await DbContext.Items.AnyAsync(i => i.ShopId == shop.Id);
            var hasAssignments = await DbContext.ShopAssignments.AnyAsync(a => a.ShopId == shop.Id);

            if (hasItems || hasAssignments)
            {
                errorMessage = $"Cannot delete '{shop.Name}': remove its items and shopkeeper assignments first.";
                pendingDeleteId = null;
                return;
            }

            DbContext.Shops.Remove(shop);
            await DbContext.SaveChangesAsync();

            pendingDeleteId = null;
            await LoadShops();
        }
    }
}