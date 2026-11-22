using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace ShoppingSysem.Components.Account.Pages
{
    public partial class ResendEmailConfirmation
    {
        private string? message;

        [SupplyParameterFromForm]
        private InputModel Input { get; set; } = default!;

        protected override void OnInitialized()
        {
            Input ??= new();
        }

        private async Task OnValidSubmitAsync()
        {
            const string genericResponse =
                "If an account exists for that email address, confirmation instructions may be sent. Please check your inbox.";

            var user = await UserManager.FindByEmailAsync(Input.Email!);
            if (user is null)
            {
                message = genericResponse;
                return;
            }

            var userId = await UserManager.GetUserIdAsync(user);
            var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = NavigationManager.GetUriWithQueryParameters(
                NavigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
                new Dictionary<string, object?> { ["userId"] = userId, ["code"] = code });
            try
            {
                await EmailSender.SendConfirmationLinkAsync(user, Input.Email, HtmlEncoder.Default.Encode(callbackUrl));
            }
            catch (EmailDeliveryException)
            {
                // Same response either way so the page never reveals whether an account exists.
                Logger.LogWarning("A confirmation email could not be delivered.");
            }

            message = genericResponse;
        }

        private sealed class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = "";
        }
    }
}
