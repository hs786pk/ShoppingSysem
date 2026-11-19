using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ShoppingSysem.Data;

namespace ShoppingSysem.Components.Account;

public sealed class DevelopmentEmailOptions
{
    /// <summary>
    /// Directory where captured emails are written. Relative paths are resolved
    /// against the content root. Defaults to "DevEmails" under the content root.
    /// </summary>
    public string OutputDirectory { get; set; } = "DevEmails";
}

/// <summary>
/// Development-only email sender that writes messages to disk so confirmation
/// and password-reset links can be opened without a real SMTP server.
/// </summary>
internal sealed class DevelopmentEmailSender(
    IOptions<DevelopmentEmailOptions> options,
    IWebHostEnvironment environment,
    ILogger<DevelopmentEmailSender> logger) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        WriteAsync(user.Id, email, "Confirm your email",
            $"Please confirm your account by <a href=\"{confirmationLink}\">clicking here</a>.");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        WriteAsync(user.Id, email, "Reset your password",
            $"Please reset your password by <a href=\"{resetLink}\">clicking here</a>.");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        WriteAsync(user.Id, email, "Reset your password",
            $"Please reset your password using the following code: {HtmlEncoder.Default.Encode(resetCode)}");

    private async Task WriteAsync(string userId, string toEmail, string subject, string htmlBody)
    {
        var root = options.Value.OutputDirectory;
        if (string.IsNullOrWhiteSpace(root))
            root = "DevEmails";

        var directory = Path.IsPathRooted(root)
            ? root
            : Path.Combine(environment.ContentRootPath, root);

        Directory.CreateDirectory(directory);

        var safeEmail = string.Join("_",
            toEmail.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{safeEmail}.html";
        var path = Path.Combine(directory, fileName);

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html><head><meta charset=\"utf-8\"/>");
        html.AppendLine($"<title>{HtmlEncoder.Default.Encode(subject)}</title></head><body>");
        html.AppendLine($"<p><strong>To:</strong> {HtmlEncoder.Default.Encode(toEmail)}</p>");
        html.AppendLine($"<p><strong>Subject:</strong> {HtmlEncoder.Default.Encode(subject)}</p>");
        html.AppendLine($"<p><strong>UserId:</strong> {HtmlEncoder.Default.Encode(userId)}</p>");
        html.AppendLine("<hr/>");
        html.AppendLine(htmlBody);
        html.AppendLine("</body></html>");

        await File.WriteAllTextAsync(path, html.ToString(), Encoding.UTF8);

        logger.LogInformation(
            "Development email captured. Subject '{Subject}' for user {UserId} written to {Path}",
            subject, userId, path);
    }
}
