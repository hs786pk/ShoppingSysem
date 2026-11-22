using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace ShoppingSysem.Components.Account.Pages
{
    public partial class ConfirmEmail
    {
        private string? statusMessage;

        [CascadingParameter]
        private HttpContext HttpContext { get; set; } = default!;

        [SupplyParameterFromQuery]
        private string? UserId { get; set; }

        [SupplyParameterFromQuery]
        private string? Code { get; set; }

        protected override async Task OnInitializedAsync()
        {
            if (UserId is null || Code is null)
            {
                RedirectManager.RedirectTo("");
                return;
            }

            var user = await UserManager.FindByIdAsync(UserId);
            if (user is null)
            {
                // Do not reveal whether a user ID exists.
                HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                statusMessage = "Unable to confirm this email link.";
                return;
            }

            try
            {
                var code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Code));
                var result = await UserManager.ConfirmEmailAsync(user, code);
                statusMessage = result.Succeeded
                    ? "Thank you for confirming your email."
                    : "Unable to confirm this email link.";
            }
            catch (FormatException)
            {
                statusMessage = "Unable to confirm this email link.";
            }
            catch (ArgumentException)
            {
                statusMessage = "Unable to confirm this email link.";
            }
        }
    }
}
