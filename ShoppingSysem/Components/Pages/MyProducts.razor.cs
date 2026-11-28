using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using ShoppingSysem.Models;
using ShoppingSysem.Services;

namespace ShoppingSysem.Components.Pages
{
    public partial class MyProducts
    {
        [Inject] private AuthenticationStateProvider AuthProvider { get; set; } = default!;
        [Inject] private IProductService Products { get; set; } = default!;

        private string userId = string.Empty;
        private string? message;
        private bool messageIsError;
        private bool busy;
        private int? pendingDeleteId;
        private List<Item> products = new();

        protected override async Task OnInitializedAsync()
        {
            var state = await AuthProvider.GetAuthenticationStateAsync();
            userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            await Reload();
        }

        private async Task Reload()
        {
            if (string.IsNullOrEmpty(userId)) return;
            products = await Products.GetMyProductsAsync(userId);
        }

        private Task Submit(int id) => Run(() => Products.SubmitForApprovalAsync(userId, id));
        private Task Deactivate(int id) => Run(() => Products.DeactivateAsync(userId, id));
        private Task Reactivate(int id) => Run(() => Products.ReactivateAsync(userId, id));

        private void RequestDelete(int id)
        {
            pendingDeleteId = id;
            message = null;
        }

        private void CancelDelete() => pendingDeleteId = null;

        private async Task ConfirmDelete(int id)
        {
            if (pendingDeleteId != id) return;
            pendingDeleteId = null;
            await Run(() => Products.DeleteAsync(userId, id));
        }

        // Every action goes through the service, which does the ownership and status checks.
        private async Task Run(Func<Task<ProductResult>> action)
        {
            if (busy || string.IsNullOrEmpty(userId)) return;
            busy = true;
            try
            {
                var result = await action();
                message = result.Message;
                messageIsError = !result.Ok;
                await Reload();
            }
            catch (Exception)
            {
                message = "We could not update the product. Please try again.";
                messageIsError = true;
            }
            finally
            {
                busy = false;
            }
        }

        private static bool CanEdit(Item i) => i.Status != ProductStatus.Archived;
        private static bool CanSubmit(Item i) => i.Status is ProductStatus.Draft or ProductStatus.Rejected;
        private static bool CanDeactivate(Item i) => i.Status == ProductStatus.Active;
        private static bool CanReactivate(Item i) => i.Status == ProductStatus.Archived;

        private static string? ThumbUrl(Item i)
        {
            var primary = i.Images.FirstOrDefault(p => p.IsPrimary);
            return primary is null ? null : ProductImageService.UrlFor(primary.ThumbnailPath);
        }

        private static string StatusLabel(Item i) => i.Status switch
        {
            ProductStatus.PendingApproval => "Pending approval",
            _ => i.Status.ToString()
        };

        // Reuses the existing order status colours.
        private static string StatusClass(Item i) => i.Status switch
        {
            ProductStatus.PendingApproval => "status-placed",
            ProductStatus.Active => "status-fulfilled",
            ProductStatus.Rejected or ProductStatus.Archived => "status-cancelled",
            _ => ""
        };
    }
}