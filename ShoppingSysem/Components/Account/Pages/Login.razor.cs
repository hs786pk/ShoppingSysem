using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;

namespace ShoppingSysem.Components.Account.Pages
{
    public partial class Login
    {
        private string? errorMessage;
        private EditContext editContext = default!;

        [CascadingParameter]
        private HttpContext HttpContext { get; set; } = default!;

        [SupplyParameterFromForm]
        private InputModel Input { get; set; } = default!;

        [SupplyParameterFromQuery]
        private string? ReturnUrl { get; set; }

        private string RegisterUrl => NavigationManager.GetUriWithQueryParameters(
            "Account/Register",
            new Dictionary<string, object?> { ["ReturnUrl"] = ReturnUrl });

        protected override async Task OnInitializedAsync()
        {
            Input ??= new();

            editContext = new EditContext(Input);

            if (HttpMethods.IsGet(HttpContext.Request.Method))
            {
                // Clear the existing external cookie to ensure a clean login process
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            }
        }

        public async Task LoginUser()
        {
            // If doing a password sign-in, validate the form.
            if (!editContext.Validate())
            {
                return;
            }

            // Resolve user id for audit logs only. Never log the password or email.
            var user = await UserManager.FindByEmailAsync(Input.Email);
            var userId = user?.Id ?? "unknown";

            var result = await SignInManager.PasswordSignInAsync(
                Input.Email,
                Input.Password,
                Input.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                Logger.LogInformation("User {UserId} logged in.", userId);
                RedirectManager.RedirectTo(ReturnUrl);
            }
            else if (result.RequiresTwoFactor)
            {
                Logger.LogInformation("User {UserId} requires two-factor authentication.", userId);
                RedirectManager.RedirectTo(
                    "Account/LoginWith2fa",
                    new() { ["returnUrl"] = ReturnUrl, ["rememberMe"] = Input.RememberMe });
            }
            else if (result.IsLockedOut)
            {
                Logger.LogWarning("User {UserId} account locked out.", userId);
                RedirectManager.RedirectTo("Account/Lockout");
            }
            else
            {
                // Generic UI message; log only the id so failed attempts are auditable
                // without confirming whether the email exists.
                Logger.LogWarning("Invalid login attempt for user {UserId}.", userId);
                errorMessage = "Error: Invalid login attempt.";
            }
        }

        private sealed class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = "";

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";

            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }
    }
}
