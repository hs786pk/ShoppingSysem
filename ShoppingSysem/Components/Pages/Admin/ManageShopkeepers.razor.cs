
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;
using System.ComponentModel.DataAnnotations;

namespace ShoppingSysem.Components.Pages.Admin
{
    public partial class ManageShopkeepers
    {
        [Inject]
        private ILogger<ManageShopkeepers> Logger { get; set; } = default!;

        [Inject]
        private AuthenticationStateProvider AuthenticationStateProvider
        {
            get;
            set;
        } = default!;

        List<Shop> allShops = new();
        List<ApplicationUser> shopkeepers = new();
        List<ShopAssignment> allAssignments = new();

        string newEmail = "";
        string newPassword = "";
        List<int> selectedShopIds = new();

        string? errorMessage;
        string? successMessage;
        string? adminId;
        protected override async Task OnInitializedAsync()
        {
            await LoadData();
        }

        // Verify administrator permissions against the current Identity user.
        private async Task<bool> IsCurrentUserAdminAsync()
        {
            var authState =
                await AuthenticationStateProvider.GetAuthenticationStateAsync();

            if (authState.User.Identity?.IsAuthenticated != true)
                return false;

            var currentUser =
                await UserManager.GetUserAsync(authState.User);

            if (currentUser == null)
                return false;

            adminId = currentUser.Id;

            if (await UserManager.IsInRoleAsync(currentUser, AppRoles.Admin))
                return true;

            Logger.LogWarning(
                "Denied admin shopkeeper operation for user {UserId}.",
                currentUser.Id);

            return false;
        }

        async Task LoadData()
        {
            if (!await IsCurrentUserAdminAsync())
            {
                errorMessage = "You are not authorized to access this page.";
                return;
            }

            try
            {
                allShops = await DbContext.Shops
                    .AsNoTracking()
                    .OrderBy(s => s.Name)
                    .ToListAsync();

                allAssignments = await DbContext.ShopAssignments
                    .AsNoTracking()
                    .ToListAsync();

                var users =
                    await UserManager.GetUsersInRoleAsync(AppRoles.Shopkeeper);

                shopkeepers = users.ToList();
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Failed to load shopkeeper management data.");

                errorMessage =
                    "Unable to load shopkeeper information. Please try again.";
            }
        }

        void OnShopSelectionChanged(ChangeEventArgs e)
        {
            errorMessage = null;

            if (e.Value is not string[] values)
            {
                selectedShopIds = new();
                return;
            }

            var parsedIds = new List<int>();

            foreach (var value in values)
            {
                if (!int.TryParse(value, out var shopId) || shopId <= 0)
                {
                    selectedShopIds = new();
                    errorMessage = "Invalid shop selection.";
                    return;
                }

                parsedIds.Add(shopId);
            }

            selectedShopIds = parsedIds.Distinct().ToList();
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

            if (!await IsCurrentUserAdminAsync())
            {
                errorMessage = "You are not authorized to perform this operation.";
                return;
            }

            var email = newEmail?.Trim();

            if (string.IsNullOrWhiteSpace(email) ||
                email.Length > ValidationLimits.MaxEmailLength ||
                !new EmailAddressAttribute().IsValid(email))
            {
                errorMessage = "Enter a valid email address.";
                return;
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                errorMessage = "Password is required.";
                return;
            }

            if (newPassword.Length < ValidationLimits.MinPasswordLength)
            {
                errorMessage =
                    $"Password must be at least {ValidationLimits.MinPasswordLength} characters.";
                return;
            }

            if (selectedShopIds.Count == 0)
            {
                errorMessage = "Please select at least one shop to assign.";
                return;
            }

            var shopIds = selectedShopIds
                .Distinct()
                .ToList();

            if (shopIds.Any(id => id <= 0))
            {
                errorMessage = "One or more selected shop IDs are invalid.";
                return;
            }

            try
            {
                // Confirm that every submitted shop ID exists.
                var validShopIds = await DbContext.Shops
                    .AsNoTracking()
                    .Where(s => shopIds.Contains(s.Id))
                    .Select(s => s.Id)
                    .ToListAsync();

                if (validShopIds.Count != shopIds.Count)
                {
                    errorMessage =
                        "One or more selected shops do not exist.";

                    return;
                }

                var user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,

                    // Preserves the existing administrator-created
                    // shopkeeper account workflow.
                    EmailConfirmed = true
                };

                // Account creation, role assignment and shop assignments
                // must succeed together.
                await using var transaction =
                    await DbContext.Database.BeginTransactionAsync();

                try
                {
                    var createResult =
                        await UserManager.CreateAsync(user, newPassword);

                    if (!createResult.Succeeded)
                    {
                        await transaction.RollbackAsync();

                        errorMessage = string.Join(
                            " ",
                            createResult.Errors.Select(e => e.Description));

                        return;
                    }

                    var roleResult =
                        await UserManager.AddToRoleAsync(
                            user,
                            AppRoles.Shopkeeper);

                    if (!roleResult.Succeeded)
                    {
                        await transaction.RollbackAsync();

                        Logger.LogError(
                            "Shopkeeper role assignment failed for a newly created account.");

                        errorMessage =
                            "The shopkeeper account could not be assigned its required role.";

                        return;
                    }

                    foreach (var shopId in validShopIds)
                    {
                        DbContext.ShopAssignments.Add(
                            new ShopAssignment
                            {
                                ShopkeeperId = user.Id,
                                ShopId = shopId
                            });
                    }

                    await DbContext.SaveChangesAsync();

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }

                Logger.LogInformation(
                    "Admin {AdminId} created shopkeeper {ShopkeeperId} with {ShopCount} shop assignment(s).",
                    adminId,
                    user.Id,
                    validShopIds.Count);

                successMessage =
                    $"Shopkeeper '{email}' created and assigned to {validShopIds.Count} shop(s).";
                newEmail = "";
                newPassword = "";
                selectedShopIds = new();

                await LoadData();
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "An unexpected error occurred while creating a shopkeeper account.");

                errorMessage =
                    "Unable to create the shopkeeper account. Please try again.";
            }
        }

        List<ShopAssignment> GetAssignments(string userId)
        {
            return allAssignments
                .Where(a => a.ShopkeeperId == userId)
                .ToList();
        }

        string GetShopName(int shopId)
        {
            return allShops
                .FirstOrDefault(s => s.Id == shopId)?.Name
                ?? "(unknown shop)";
        }

        async Task RemoveAssignment(ShopAssignment assignment)
        {
            errorMessage = null;
            successMessage = null;

            if (!await IsCurrentUserAdminAsync())
            {
                errorMessage =
                    "You are not authorized to perform this operation.";
                return;
            }

            if (assignment == null || assignment.Id <= 0)
            {
                errorMessage = "Invalid shop assignment.";
                return;
            }

            try
            {
                // Reload the assignment using its database ID.
                var existingAssignment =
                    await DbContext.ShopAssignments
                        .FirstOrDefaultAsync(a => a.Id == assignment.Id);

                if (existingAssignment == null)
                {
                    errorMessage =
                        "The selected assignment no longer exists.";

                    await LoadData();
                    return;
                }

                DbContext.ShopAssignments.Remove(existingAssignment);

                await DbContext.SaveChangesAsync();

                Logger.LogInformation(
                    "Admin {AdminId} removed assignment of shopkeeper {ShopkeeperId} from shop {ShopId}.",
                    adminId,
                    existingAssignment.ShopkeeperId,
                    existingAssignment.ShopId);

                successMessage = "Shop assignment removed successfully.";
                await LoadData();
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Failed to remove a shopkeeper assignment.");

                errorMessage =
                    "Unable to remove the shop assignment. Please try again.";
            }
        }
    }
}
