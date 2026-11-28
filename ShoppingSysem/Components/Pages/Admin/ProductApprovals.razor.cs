using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using ShoppingSysem.Models;
using ShoppingSysem.Services;

namespace ShoppingSysem.Components.Pages.Admin
{
    public partial class ProductApprovals
    {
        [Inject] private AuthenticationStateProvider AuthProvider { get; set; } = default!;
        [Inject] private IProductService Products { get; set; } = default!;

        private string userId = string.Empty;
        private string? message;
        private bool messageIsError;
        private bool busy;
        private int? rejectingId;
        private string rejectReason = string.Empty;
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
            products = await Products.GetPendingApprovalAsync(userId);
        }

        private Task Approve(int id) => Run(() => Products.ApproveAsync(userId, id));

        private void StartReject(int id)
        {
            rejectingId = id;
            rejectReason = string.Empty;
            message = null;
        }

        private void CancelReject() => rejectingId = null;

        private async Task ConfirmReject(int id)
        {
            if (rejectingId != id) return;

            // Keep the reason box open if the service refuses (e.g. empty reason).
            if (await Run(() => Products.RejectAsync(userId, id, rejectReason)))
                rejectingId = null;
        }

        // The service verifies the admin role and the product status on every call.
        private async Task<bool> Run(Func<Task<ProductResult>> action)
        {
            if (busy || string.IsNullOrEmpty(userId)) return false;
            busy = true;
            try
            {
                var result = await action();
                message = result.Message;
                messageIsError = !result.Ok;
                await Reload();
                return result.Ok;
            }
            catch (Exception)
            {
                message = "We could not update the product. Please try again.";
                messageIsError = true;
                return false;
            }
            finally
            {
                busy = false;
            }
        }

        private static string? ThumbUrl(Item i)
        {
            var primary = i.Images.FirstOrDefault(p => p.IsPrimary);
            return primary is null ? null : ProductImageService.UrlFor(primary.ThumbnailPath);
        }
    }
}