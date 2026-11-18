using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ShoppingSysem.Data;

namespace ShoppingSysem.Components.Account;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Shopping System";
    public bool EnableSsl { get; set; } = true;
}

/// <summary>
/// Thrown when an email could not be delivered. The message is deliberately
/// generic and the SMTP exception is not attached, so no server, port or
/// credential details can reach a page or error response. Details are logged.
/// </summary>
public sealed class EmailDeliveryException()
    : Exception("The email could not be delivered.");

internal sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender<ApplicationUser>
{
    // Callers pass links that are already HTML-encoded.
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(user.Id, email, "Confirm your email",
            $"Please confirm your account by <a href=\"{confirmationLink}\">clicking here</a>.");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(user.Id, email, "Reset your password",
            $"Please reset your password by <a href=\"{resetLink}\">clicking here</a>.");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(user.Id, email, "Reset your password",
            $"Please reset your password using the following code: {HtmlEncoder.Default.Encode(resetCode)}");

    // Failures are logged in full here and surfaced to callers only as a generic
    // EmailDeliveryException, so a failed send is never reported as a success.
    private async Task SendAsync(string userId, string toEmail, string subject, string htmlBody)
    {
        var o = options.Value;

        if (string.IsNullOrWhiteSpace(o.Host) || string.IsNullOrWhiteSpace(o.FromAddress))
        {
            logger.LogError(
                "SMTP is not configured. Email '{Subject}' for user {UserId} was not sent.",
                subject, userId);
            throw new EmailDeliveryException();
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(o.FromAddress, o.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(new MailAddress(toEmail));

            using var client = new SmtpClient(o.Host, o.Port)
            {
                EnableSsl = o.EnableSsl,
                Timeout = 15000
            };

            if (!string.IsNullOrWhiteSpace(o.Username))
                client.Credentials = new NetworkCredential(o.Username, o.Password);

            await client.SendMailAsync(message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Failed to send email '{Subject}' for user {UserId}.",
                subject, userId);
            throw new EmailDeliveryException();
        }
    }
}