using Microsoft.AspNetCore.Components;

namespace ShoppingSysem.Components.Account.Pages.Manage
{
    public partial class PersonalData
    {
        [CascadingParameter]
        private HttpContext HttpContext { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            var user = await UserManager.GetUserAsync(HttpContext.User);
            if (user is null)
            {
                RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            }
        }
    }
}
