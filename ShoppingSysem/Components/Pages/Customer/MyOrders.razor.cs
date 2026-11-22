using System.Security.Claims;
using ShoppingSysem.Models;

namespace ShoppingSysem.Components.Pages.Customer
{
    public partial class MyOrders
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
            orders = await Orders.GetForCustomerAsync(userId);
        }

        private async Task Cancel(int orderId)
        {
            if (busy || string.IsNullOrEmpty(userId) || orderId <= 0) return;
            busy = true;
            try
            {
                var result = await Orders.CancelByCustomerAsync(userId, orderId);
                message = result.Message;
                messageIsError = !result.Ok;
                await Reload();
            }
            catch (Exception)
            {
                message = "We could not cancel the order. Please try again.";
                messageIsError = true;
            }
            finally
            {
                busy = false;
            }
        }
    }
}
