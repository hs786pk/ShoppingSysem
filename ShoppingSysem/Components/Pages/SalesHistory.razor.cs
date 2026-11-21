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
        [Inject] private ILogger<SalesHistory> Logger { get; set; } = default!;

        List<Sale> sales = new();
        long totalItemsSold = 0;
        decimal totalRevenue = 0;

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var user = await UserManager.GetUserAsync(authState.User);

            if (user is null || !await UserManager.IsInRoleAsync(user, AppRoles.Shopkeeper))
            {
                Logger.LogWarning(
                    "Sales history denied for user {UserId}.",
                    user?.Id ?? "anonymous");
                sales = new();
                return;
            }

            try
            {
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
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to load sales history for shopkeeper {UserId}.", user.Id);
                sales = new();
            }
        }
    }
}