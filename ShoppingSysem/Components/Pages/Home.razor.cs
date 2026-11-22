using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using ShoppingSysem.Data;

namespace ShoppingSysem.Components.Pages
{
    public partial class Home
    {
        [CascadingParameter]
        private Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            var state = await AuthenticationStateTask;

            if (state.User.Identity?.IsAuthenticated == true)
            {
                RedirectBasedOnRole(state);
                return;
            }

            NavManager.NavigateTo("Account/Login", forceLoad: false);
        }

        void RedirectBasedOnRole(AuthenticationState context)
        {
            if (context.User.IsInRole(AppRoles.Admin))
            {
                NavManager.NavigateTo("/admin");
            }
            else if (context.User.IsInRole(AppRoles.Shopkeeper))
            {
                NavManager.NavigateTo("/shopkeeper");
            }
            else if (context.User.IsInRole(AppRoles.Customer))
            {
                NavManager.NavigateTo("/buyer");
            }
        }
    }
}
