using System.Security.Claims;
using ShoppingSysem.Models;

namespace ShoppingSysem.Components.Pages
{
    public partial class ShopOrders
    {
        private string userId = string.Empty;
        private string? message;
        private bool messageIsError;
        private bool busy;
        private List<Order> orders = new();

        protected override async Task OnInitializedAsync()
        {
            var state = await AuthProvider.GetAuthenticationStateAsync();
            userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            await Reload();
        }

        private async Task Reload()
        {
            if (string.IsNullOrEmpty(userId)) return;
            orders = await Orders.GetForShopkeeperAsync(userId);
        }

        private async Task Fulfill(int orderId) => await Act(orderId, fulfill: true);

        private async Task Cancel(int orderId) => await Act(orderId, fulfill: false);

        private async Task Act(int orderId, bool fulfill)
        {
            if (busy || string.IsNullOrEmpty(userId) || orderId <= 0) return;
            busy = true;
            try
            {
                var result = fulfill
                    ? await Orders.FulfillAsync(userId, orderId)
                    : await Orders.CancelByShopkeeperAsync(userId, orderId);
                message = result.Message;
                messageIsError = !result.Ok;
                await Reload();
            }
            catch (Exception)
            {
                message = "We could not update the order. Please try again.";
                messageIsError = true;
            }
            finally
            {
                busy = false;
            }
        }
    }
}
