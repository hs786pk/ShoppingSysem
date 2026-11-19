using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using ShoppingSysem.Data;

namespace ShoppingSysem.Components.Account
{
    internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
    {
        public const string StatusCookieName = "Identity.StatusMessage";

        private static readonly CookieBuilder StatusCookieBuilder = new()
        {
            SameSite = SameSiteMode.Strict,
            HttpOnly = true,
            IsEssential = true,
            MaxAge = TimeSpan.FromSeconds(5),
        };

        public void RedirectTo(string? uri)
        {
            uri ??= "";

            if (!IsSafeRelative(uri))
            {
                if (!TryToLocalPath(uri, out var local))
                    local = "";

                uri = local;
            }

            navigationManager.NavigateTo(uri);
        }

        // Same-origin absolute URLs used as ReturnUrl become a relative path.
        // Any other absolute URI, including a different host, is rejected.
        private bool TryToLocalPath(string uri, out string local)
        {
            local = "";

            try
            {
                if (!Uri.TryCreate(uri, UriKind.Absolute, out var absolute) ||
                    absolute.Scheme is not ("http" or "https"))
                {
                    return false;
                }

                local = navigationManager.ToBaseRelativePath(uri);
                return IsSafeRelative(local);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return false;
            }
        }

        private static bool IsSafeRelative(string uri)
        {
            if (uri.Length == 0)
                return true;

            // Blocks //host, /\host, and scheme-bearing values that still count as "relative".
            if (uri.Contains('\\') ||
                uri.Contains("://", StringComparison.Ordinal) ||
                uri.StartsWith("//", StringComparison.Ordinal))
            {
                return false;
            }

            if (uri.StartsWith('/') && uri.Length > 1 && (uri[1] == '/' || uri[1] == '\\'))
                return false;

            return Uri.IsWellFormedUriString(uri, UriKind.Relative);
        }

        public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
        {
            var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
            var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
            RedirectTo(newUri);
        }

        public void RedirectToWithStatus(string uri, string message, HttpContext context)
        {
            context.Response.Cookies.Append(StatusCookieName, message, StatusCookieBuilder.Build(context));
            RedirectTo(uri);
        }

        private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

        public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

        public void RedirectToCurrentPageWithStatus(string message, HttpContext context)
            => RedirectToWithStatus(CurrentPath, message, context);

        public void RedirectToInvalidUser(UserManager<ApplicationUser> userManager, HttpContext context)
            => RedirectToWithStatus("Account/InvalidUser", $"Error: Unable to load user with ID '{userManager.GetUserId(context.User)}'.", context);
    }
}
