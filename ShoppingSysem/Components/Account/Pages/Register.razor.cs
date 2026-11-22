using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using ShoppingSysem.Data;

namespace ShoppingSysem.Components.Account.Pages
{
    public partial class Register
    {
        private IEnumerable<IdentityError>? identityErrors;

        [SupplyParameterFromForm]
        private InputModel Input { get; set; } = default!;

        [SupplyParameterFromQuery]
        private string? ReturnUrl { get; set; }

        private string? Message => identityErrors is null ? null : $"Error: {string.Join(", ", identityErrors.Select(error => error.Description))}";

        protected override void OnInitialized()
        {
            Input ??= new();
        }

        public async Task RegisterUser(EditContext editContext)
        {
            var user = CreateUser();

            await UserStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
            var emailStore = GetEmailStore();
            await emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
            var result = await UserManager.CreateAsync(user, Input.Password);

            if (!result.Succeeded)
            {
                // Do not disclose whether an email address or username is already registered.
                if (result.Errors.Any(error =>
                    error.Code is "DuplicateUserName" or "DuplicateEmail"))
                {
                    identityErrors = new[]
                    {
                        new IdentityError
                        {
                            Description = "Registration could not be completed. If you already have an account, sign in or reset your password."
                        }
                    };
                }
                else
                {
                    identityErrors = result.Errors;
                }

                return;
            }

            var userId = await UserManager.GetUserIdAsync(user);
            Logger.LogInformation("User {UserId} registered a new Customer account.", userId);

            // Public registration must only create Customer accounts.
            // Shopkeeper and Admin accounts are managed separately.
            var roleResult = await UserManager.AddToRoleAsync(
                user,
                AppRoles.Customer);

            if (!roleResult.Succeeded)
            {
                identityErrors = roleResult.Errors;

                var cleanupResult = await UserManager.DeleteAsync(user);

                if (!cleanupResult.Succeeded)
                {
                    Logger.LogCritical(
                        "Failed to clean up user {UserId} after Customer role assignment failed.",
                        userId);
                }

                Logger.LogError(
                    "Customer role assignment failed during registration for user {UserId}.",
                    userId);

                return;
            }

            var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = NavigationManager.GetUriWithQueryParameters(
                NavigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
                new Dictionary<string, object?> { ["userId"] = userId, ["code"] = code, ["returnUrl"] = ReturnUrl });

            try
            {
                await EmailSender.SendConfirmationLinkAsync(user, Input.Email, HtmlEncoder.Default.Encode(callbackUrl));
            }
            catch (EmailDeliveryException)
            {
                // The account exists; the sender has already logged the details.
                identityErrors = new[]
                {
                    new IdentityError
                    {
                        Description = "Your account was created, but we could not send the confirmation email. " +
                                      "Please use \"Resend email confirmation\" in a few minutes."
                    }
                };
                return;
            }

            if (UserManager.Options.SignIn.RequireConfirmedAccount)
            {
                RedirectManager.RedirectTo(
                    "Account/RegisterConfirmation",
                    new() { ["email"] = Input.Email, ["returnUrl"] = ReturnUrl });
            }
            else
            {
                await SignInManager.SignInAsync(user, isPersistent: false);
                RedirectManager.RedirectTo(ReturnUrl);
            }
        }

        private ApplicationUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<ApplicationUser>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(ApplicationUser)}'. " +
                    $"Ensure that '{nameof(ApplicationUser)}' is not an abstract class and has a parameterless constructor.");
            }
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!UserManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<ApplicationUser>)UserStore;
        }

        private sealed class InputModel
        {
            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; } = "";

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 12)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; } = "";

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; } = "";
        }
    }
}
