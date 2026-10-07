using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace LeaversForm.Services;

public sealed class AdministrativeNotificationService(
    IGraphEmailService email,
    ILeaversFormEmailSettingsService emailSettings,
    ILogger<AdministrativeNotificationService> logger) : IAdministrativeNotificationService
{
    public async Task NotifyAsync(
        string subject,
        string title,
        string details,
        ClaimsPrincipal performer,
        CancellationToken cancellationToken = default)
    {
        var performerName = performer.Identity?.Name ?? "Unknown user";
        var performerEmail = LeaversRoles.GetEmail(performer) ?? "Unknown email";

        var body = EmailTemplate.Build(
            title,
            $"""
            <p>A Leavers Form administration change was made.</p>
            <div class="notice">
              <strong>Changed by</strong><br>
              {EmailTemplate.Encode(performerName)} &lt;{EmailTemplate.Encode(performerEmail)}&gt;
            </div>
            <h3>Change details</h3>
            <div>{details}</div>
            """);

        try
        {
            await email.SendAsync(new EmailMessage(emailSettings.ErrorAlertMailbox, subject, body), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send administrative notification email.");
        }
    }
}

public static class EmailTemplate
{
    public static string Encode(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    public static string Build(string title, string content) => $"""
        <!doctype html>
        <html>
        <body style="margin:0;background:#eef4f0;font-family:Segoe UI,Arial,sans-serif;color:#17352a;">
          <div style="max-width:680px;margin:24px auto;background:#fff;border-radius:12px;overflow:hidden;border:1px solid #d6e2da;">
            <div style="background:linear-gradient(135deg,#0b3d2e,#176044);padding:24px 28px;color:#fff;">
              <div style="font-size:12px;letter-spacing:1.5px;text-transform:uppercase;opacity:.8;">YOURS CLOTHING</div>
              <div style="font-size:24px;font-weight:700;margin-top:7px;">Leavers Form</div>
            </div>
            <div style="padding:28px;">
              <h2 style="margin:0 0 18px;color:#0b3d2e;">{Encode(title)}</h2>
              <div style="font-size:15px;line-height:1.55;">{content}</div>
            </div>
            <div style="padding:16px 28px;background:#f5f8f6;color:#60756b;font-size:12px;">
              This message was sent automatically by Leavers Form.
            </div>
          </div>
        </body>
        </html>
        """;
}
