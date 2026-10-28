using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;

namespace ShoppingSysem.Components.Pages.Admin
{
    public partial class ManageShopkeepers
    {
        List<Shop> allShops = new();
        List<ApplicationUser> shopkeepers = new();
        List<ShopAssignment> allAssignments = new();

        string newEmail = "";
        string newPassword = "";
        List<int> selectedShopIds = new();

        string? errorMessage;
        string? successMessage;

        protected override async Task OnInitializedAsync()
        {
            await LoadData();
        }

        async Task LoadData()
        {
            allShops = await DbContext.Shops.ToListAsync();
            allAssignments = await DbContext.ShopAssignments.ToListAsync();

            // Get all users who are in the "Shopkeeper" role
            shopkeepers = await UserManager.GetUsersInRoleAsync("Shopkeeper")
                is var list ? list.ToList() : new List<ApplicationUser>();
        }

        void OnShopSelectionChanged(ChangeEventArgs e)
        {
            // e.Value contains the selected <option> values from the multi-select
            if (e.Value is string[] values)
            {
                selectedShopIds = values.Select(int.Parse).ToList();
            }
        }

        List<string> GetShopNames(string userId)
        {
            var shopIds = allAssignments
                .Where(a => a.ShopkeeperId == userId)
                .Select(a => a.ShopId);

            return allShops
                .Where(s => shopIds.Contains(s.Id))
                .Select(s => s.Name)
                .ToList();
        }

        async Task CreateShopkeeper()
        {
            errorMessage = null;
            successMessage = null;

            if (string.IsNullOrWhiteSpace(newEmail) || string.IsNullOrWhiteSpace(newPassword))
            {
                errorMessage = "Email and password are required.";
                return;
            }

            if (selectedShopIds.Count == 0)
            {
                errorMessage = "Please select at least one shop to assign.";
                return;
            }

            var user = new ApplicationUser
            {
                UserName = newEmail.Trim(),
                Email = newEmail.Trim(),
                EmailConfirmed = true
            };

            var result = await UserManager.CreateAsync(user, newPassword);

            if (!result.Succeeded)
            {
                errorMessage = string.Join(" ", result.Errors.Select(e => e.Description));
                return;
            }

            await UserManager.AddToRoleAsync(user, "Shopkeeper");

            foreach (var shopId in selectedShopIds)
            {
                DbContext.ShopAssignments.Add(new ShopAssignment
                {
                    ShopkeeperId = user.Id,
                    ShopId = shopId
                });
            }
            await DbContext.SaveChangesAsync();

            successMessage = $"Shopkeeper '{newEmail}' created and assigned to {selectedShopIds.Count} shop(s).";
            newEmail = "";
            newPassword = "";
            selectedShopIds = new();

            await LoadData();
        }
        List<ShopAssignment> GetAssignments(string userId)
        {
            return allAssignments.Where(a => a.ShopkeeperId == userId).ToList();
        }

        string GetShopName(int shopId)
        {
            return allShops.FirstOrDefault(s => s.Id == shopId)?.Name ?? "(unknown shop)";
        }

        async Task RemoveAssignment(ShopAssignment assignment)
        {
            errorMessage = null;

            DbContext.ShopAssignments.Remove(assignment);
            await DbContext.SaveChangesAsync();

            await LoadData();
        }
    }
}