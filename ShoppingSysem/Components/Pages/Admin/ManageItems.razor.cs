using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Models;

namespace ShoppingSysem.Components.Pages.Admin
{
    public partial class ManageItems
    {
        List<Shop> shops = new();
        List<Item> items = new();

        int selectedShopId = 0;
        string newItemName = "";
        string newItemPrice = "";
        string newItemStock = "";
        string? errorMessage;

        int? pendingDeleteItemId = null;

        protected override async Task OnInitializedAsync()
        {
            shops = await DbContext.Shops.ToListAsync();
        }

        async Task LoadItems()
        {
            if (selectedShopId == 0)
            {
                items = new();
                return;
            }

            items = await DbContext.Items
                .Where(i => i.ShopId == selectedShopId)
                .ToListAsync();
        }

        async Task AddItem()
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(newItemName))
            {
                errorMessage = "Item name is required.";
                return;
            }

            if (!decimal.TryParse(newItemPrice, out var price) || price < 0)
            {
                errorMessage = "Enter a valid price.";
                return;
            }

            if (!int.TryParse(newItemStock, out var stock) || stock < 0)
            {
                errorMessage = "Enter a valid stock quantity.";
                return;
            }

            var item = new Item
            {
                Name = newItemName.Trim(),
                Price = price,
                Stock = stock,
                ShopId = selectedShopId
            };

            DbContext.Items.Add(item);
            await DbContext.SaveChangesAsync();

            newItemName = "";
            newItemPrice = "";
            newItemStock = "";

            await LoadItems();
        }

        async Task UpdateItem(Item item)
        {
            await DbContext.SaveChangesAsync();
        }

        void RequestDeleteItem(int itemId)
        {
            pendingDeleteItemId = itemId;
        }

        void CancelDeleteItem()
        {
            pendingDeleteItemId = null;
        }

        async Task ConfirmDeleteItem(Item item)
        {
            errorMessage = null;

            var hasSales = await DbContext.Sales.AnyAsync(s => s.ItemId == item.Id);

            if (hasSales)
            {
                errorMessage = $"Cannot delete '{item.Name}': it has recorded sales history.";
                pendingDeleteItemId = null;
                return;
            }

            DbContext.Items.Remove(item);
            await DbContext.SaveChangesAsync();

            pendingDeleteItemId = null;
            await LoadItems();
        }
    }
}