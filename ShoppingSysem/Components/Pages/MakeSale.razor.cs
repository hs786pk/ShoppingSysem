using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Models;

namespace ShoppingSysem.Components.Pages
{
    public partial class MakeSale
    {
        [CascadingParameter]
        private Task<AuthenticationState>? AuthState { get; set; }

        List<Shop> myShops = new();
        List<Item> items = new();

        int selectedShopId = 0;
        int selectedItemId = 0;
        string quantity = "";

        string? errorMessage;
        string? successMessage;

        protected override async Task OnInitializedAsync()
        {
            var userId = await GetCurrentUserId();

            // Only load shops assigned to THIS shopkeeper - the key security filter
            myShops = await DbContext.ShopAssignments
                .Where(a => a.ShopkeeperId == userId)
                .Include(a => a.Shop)
                .Select(a => a.Shop)
                .ToListAsync();
        }

        async Task<string> GetCurrentUserId()
        {
            var authState = await AuthState!;
            var userId = UserManager.GetUserId(authState.User);
            return userId!;
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

        async Task ConfirmSale()
        {
            errorMessage = null;
            successMessage = null;

            if (selectedItemId == 0)
            {
                errorMessage = "Please select an item.";
                return;
            }

            if (!int.TryParse(quantity, out var qty) || qty <= 0)
            {
                errorMessage = "Enter a valid quantity.";
                return;
            }

            var item = await DbContext.Items.FindAsync(selectedItemId);
            if (item is null)
            {
                errorMessage = "Item not found.";
                return;
            }

            if (item.Stock < qty)
            {
                errorMessage = $"Not enough stock. Only {item.Stock} available.";
                return;
            }

            var userId = await GetCurrentUserId();

            var sale = new Sale
            {
                ItemId = item.Id,
                QuantitySold = qty,
                TotalPrice = item.Price * qty,
                SaleDate = DateTime.Now,
                ShopkeeperId = userId,
                ShopId = selectedShopId
            };

            item.Stock -= qty; // reduce stock

            DbContext.Sales.Add(sale);
            await DbContext.SaveChangesAsync();

            successMessage = $"Sale recorded: {qty} x {item.Name} = Rs. {sale.TotalPrice}";
            selectedItemId = 0;
            quantity = "";

            await LoadItems(); // refresh stock display
        }
    }
}