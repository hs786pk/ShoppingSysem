using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;

namespace ShoppingSysem.Components.Pages
{
    public partial class SalesHistory
    {
        [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

        List<Sale> sales = new();
        long totalItemsSold = 0;
        decimal totalRevenue = 0;

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var user = await UserManager.GetUserAsync(authState.User);

            if (user is null || !await UserManager.IsInRoleAsync(user, "Shopkeeper"))
            {
                sales = new();
                return;
            }

            sales = await DbContext.Sales
                .AsNoTracking()
                .Where(s => s.ShopkeeperId == user.Id)
                .Include(s => s.Shop)
                .Include(s => s.Item)
                .OrderByDescending(s => s.SaleDate)
                .ToListAsync();

            totalItemsSold = sales.Sum(s => (long)s.QuantitySold);
            totalRevenue = sales.Sum(s => s.TotalPrice);
        }
    }
}