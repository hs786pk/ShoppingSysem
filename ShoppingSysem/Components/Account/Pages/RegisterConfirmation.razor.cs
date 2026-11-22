using Microsoft.AspNetCore.Components;

namespace ShoppingSysem.Components.Account.Pages
{
    public partial class RegisterConfirmation
    {
        [SupplyParameterFromQuery]
        private string? Email { get; set; }

        protected override void OnInitialized()
        {
            if (Email is null)
            {
                RedirectManager.RedirectTo("");
            }
        }
    }
}
