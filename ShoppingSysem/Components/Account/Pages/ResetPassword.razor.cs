using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using ShoppingSysem.Data;

namespace ShoppingSysem.Components.Account.Pages
{
    public partial class ResetPassword
    {
        private IEnumerable<IdentityError>? identityErrors;

        [SupplyParameterFromForm]
        private InputModel Input { get; set; } = default!;

        [SupplyParameterFromQuery]
        private string? Code { get; set; }

        private string? Message => identityErrors is null ? null : $"Error: {string.Join(", ", identityErrors.Select(error => error.Description))}";

        protected override void OnInitialized()
        {
            Input ??= new();

            if (string.IsNullOrWhiteSpace(Code))
            {
                RedirectManager.RedirectTo("Account/InvalidPasswordReset");
                return;
            }

            try
            {
                var decodedCode = WebEncoders.Base64UrlDecode(Code);

                Input.Code = Encoding.UTF8.GetString(decodedCode);

                if (string.IsNullOrWhiteSpace(Input.Code))
                {
                    RedirectManager.RedirectTo("Account/InvalidPasswordReset");
                }
            }
            catch (FormatException)
            {
                RedirectManager.RedirectTo("Account/InvalidPasswordReset");
            }
            catch (ArgumentException)
            {
                RedirectManager.RedirectTo("Account/InvalidPasswordReset");
            }
        }

        private async Task OnValidSubmitAsync()
        {
            var user = await UserManager.FindByEmailAsync(Input.Email);
            if (user is null)
            {
                // Don't reveal that the user does not exist
                RedirectManager.RedirectTo("Account/ResetPasswordConfirmation");
                return;
            }

            var result = await UserManager.ResetPasswordAsync(user, Input.Code, Input.Password);
            if (result.Succeeded)
            {
                RedirectManager.RedirectTo("Account/ResetPasswordConfirmation");
                return;
            }

            identityErrors = result.Errors;
        }

        private sealed class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = "";

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 12)]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; } = "";

            [Required]
            public string Code { get; set; } = "";
        }
    }
}
