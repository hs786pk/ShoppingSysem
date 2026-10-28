using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Models;

namespace ShoppingSysem.Components.Pages
{
    public partial class SalesHistory
    {
        [CascadingParameter]
        private Task<AuthenticationState>? AuthState { get; set; }

        List<Sale> sales = new();
        int totalItemsSold = 0;
        decimal totalRevenue = 0;

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthState!;
            var userId = UserManager.GetUserId(authState.User);

            sales = await DbContext.Sales
                .Where(s => s.ShopkeeperId == userId)
                .Include(s => s.Shop)
                .Include(s => s.Item)
                .OrderByDescending(s => s.SaleDate)
                .ToListAsync();

            totalItemsSold = sales.Sum(s => s.QuantitySold);
            totalRevenue = sales.Sum(s => s.TotalPrice);
        }
    }
}