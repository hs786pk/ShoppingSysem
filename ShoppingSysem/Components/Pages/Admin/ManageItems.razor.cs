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
        string newItemCategory = "";
        string? errorMessage;

        // existing categories, offered as suggestions in the Category inputs
        List<string> categorySuggestions = new();

        async Task LoadCategorySuggestions()
        {
            categorySuggestions = await DbContext.Items
                .Where(i => i.Category != null && i.Category != "")
                .Select(i => i.Category!)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();
        }

        int? pendingDeleteItemId = null;

        protected override async Task OnInitializedAsync()
        {
            shops = await DbContext.Shops.ToListAsync();
            await LoadCategorySuggestions();
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
                Category = string.IsNullOrWhiteSpace(newItemCategory) ? null : newItemCategory.Trim(),
                ShopId = selectedShopId
            };

            DbContext.Items.Add(item);
            await DbContext.SaveChangesAsync();

            newItemName = "";
            newItemPrice = "";
            newItemStock = "";
            newItemCategory = "";

            await LoadItems();
            await LoadCategorySuggestions();
        }

        async Task UpdateItem(Item item)
        {
            if (string.IsNullOrWhiteSpace(item.Category))
                item.Category = null;
            else
                item.Category = item.Category.Trim();

            await DbContext.SaveChangesAsync();
            await LoadCategorySuggestions();
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