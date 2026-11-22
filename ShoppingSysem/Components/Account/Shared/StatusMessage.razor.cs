using Microsoft.AspNetCore.Components;
using ShoppingSysem.Components.Account;

namespace ShoppingSysem.Components.Account.Shared
{
    public partial class StatusMessage
    {
        private string? messageFromCookie;

        [Parameter]
        public string? Message { get; set; }

        [CascadingParameter]
        private HttpContext HttpContext { get; set; } = default!;

        private string? DisplayMessage => Message ?? messageFromCookie;

        private string statusMessageClass =>
            DisplayMessage is not null && DisplayMessage.StartsWith("Error") ? "danger" : "success";

        protected override void OnInitialized()
        {
            messageFromCookie = HttpContext.Request.Cookies[IdentityRedirectManager.StatusCookieName];

            if (messageFromCookie is not null)
            {
                HttpContext.Response.Cookies.Delete(IdentityRedirectManager.StatusCookieName);
            }
        }
    }
}
