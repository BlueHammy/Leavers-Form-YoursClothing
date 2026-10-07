using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaversForm.Services;

public sealed class AuthorisingManagerNotificationService(
    IGraphEmailService email,
    IOptions<LeaversFormOptions> options,
    ILeaversFormEmailSettingsService emailSettings,
    ILogger<AuthorisingManagerNotificationService> logger) : IAuthorisingManagerNotificationService
{
    public async Task NotifySelectedManagersAsync(
        LeaverRequest request,
        IEnumerable<AuthorisingManager> managers,
        CancellationToken cancellationToken = default)
    {
        var formOptions = options.Value;
        var recipients = managers
            .Where(manager => manager.IsActive && !string.IsNullOrWhiteSpace(manager.Email))
            .GroupBy(manager => manager.Email.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (recipients.Count == 0)
            return;

        var listUrl = $"{formOptions.BaseUrl.TrimEnd('/')}/Leavers/Details/{request.Id}";
        var awaitingApproval = request.Status == LeaverStatus.AwaitingApproval;
        var subject = awaitingApproval ? "Leaver request needs your approval" : "New leaver submitted";
        var asapNotice = request.DisableAsap
            ? """<div style="padding:12px 14px;background:#fff0ee;border-left:4px solid #b43b2d;color:#8f2f25;font-weight:700;">This user is marked for disabling IMMEDIATELY once approved.</div>"""
            : string.Empty;
        var approvalNotice = awaitingApproval
            ? $"""
              <div style="padding:12px 14px;background:#fff8df;border-left:4px solid #d69e2e;margin-bottom:18px;">
                <strong>Approval required</strong><br>
                This request will not switch off the leaver&apos;s account until it is approved by you, IT, or an admin.
                Open the request and click <strong>Approve</strong> when you are satisfied this is correct.
              </div>
              """
            : string.Empty;
        var intro = awaitingApproval
            ? "A new leaver has been submitted and you have been selected as an authorising manager. Please review and approve this request before their account can be switched off."
            : "A new leaver has been submitted and you have been selected as an authorising manager. This is a notification for your awareness.";
        var equipmentNotes = request.EquipmentNotes ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(request.SpecialRequests))
        {
            var duplicateAnythingElse = $"Anything else: {request.SpecialRequests.Trim()}";
            equipmentNotes = equipmentNotes
                .Replace(duplicateAnythingElse, string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
        }
        var encodedEquipment = EmailTemplate.Encode(equipmentNotes)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);
        var encodedSpecialRequests = EmailTemplate.Encode(request.SpecialRequests)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);
        var body = EmailTemplate.Build(
            awaitingApproval ? "Leaver Request — Approval Required" : "New Leaver Submitted",
            $"""
            <p style="margin:0 0 20px;">{intro}</p>
            {approvalNotice}
            {asapNotice}
            <h3 style="margin:24px 0 10px;color:#0b3d2e;">Leaver details</h3>
            <table role="presentation" style="width:100%;border-collapse:separate;border-spacing:0;border:1px solid #d6e2da;border-radius:8px;overflow:hidden;background:#fff;">
              <tr>
                <td style="padding:12px 14px;width:38%;background:#f1f6f3;border-bottom:1px solid #d6e2da;color:#486257;font-size:13px;font-weight:700;text-transform:uppercase;letter-spacing:.4px;vertical-align:top;">Name</td>
                <td style="padding:12px 14px;border-bottom:1px solid #d6e2da;color:#17352a;font-weight:600;vertical-align:top;">{EmailTemplate.Encode(request.LeaverDisplayName)}</td>
              </tr>
              <tr>
                <td style="padding:12px 14px;background:#f1f6f3;border-bottom:1px solid #d6e2da;color:#486257;font-size:13px;font-weight:700;text-transform:uppercase;letter-spacing:.4px;vertical-align:top;">Leaving date</td>
                <td style="padding:12px 14px;border-bottom:1px solid #d6e2da;color:#17352a;font-weight:600;vertical-align:top;">{request.LeavingDate:dd/MM/yyyy}</td>
              </tr>
              <tr>
                <td style="padding:12px 14px;background:#f1f6f3;border-bottom:1px solid #d6e2da;color:#486257;font-size:13px;font-weight:700;text-transform:uppercase;letter-spacing:.4px;vertical-align:top;">Equipment to be returned</td>
                <td style="padding:12px 14px;border-bottom:1px solid #d6e2da;color:#17352a;line-height:1.55;vertical-align:top;">{encodedEquipment}</td>
              </tr>
              {(string.IsNullOrWhiteSpace(request.SpecialRequests) ? string.Empty : $"""
              <tr>
                <td style="padding:12px 14px;background:#f1f6f3;color:#486257;font-size:13px;font-weight:700;text-transform:uppercase;letter-spacing:.4px;vertical-align:top;">Special requests</td>
                <td style="padding:12px 14px;color:#17352a;line-height:1.55;vertical-align:top;">{encodedSpecialRequests}</td>
              </tr>
              """)}
            </table>
            <p style="margin:22px 0 0;"><a href="{EmailTemplate.Encode(listUrl)}" style="display:inline-block;background:#176044;color:#fff;text-decoration:none;padding:11px 17px;border-radius:6px;font-weight:600;">{(awaitingApproval ? "Review and approve" : "View request")}</a></p>
            """);

        foreach (var manager in recipients)
        {
            try
            {
                await email.SendAsync(new EmailMessage(manager.Email, subject, body, BuildCcRecipients(manager.Email)), cancellationToken);
                logger.LogInformation(
                    "Sent authorising manager notification to {Manager} <{Email}> for request {RequestId}",
                    manager.DisplayName, manager.Email, request.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to send authorising manager notification to {Manager} <{Email}> for request {RequestId}",
                    manager.DisplayName, manager.Email, request.Id);
            }
        }
    }

    private IReadOnlyList<string> BuildCcRecipients(string managerEmail)
    {
        var recipients = new List<string>();
        if (!managerEmail.Equals(emailSettings.ItCcAddress, StringComparison.OrdinalIgnoreCase))
            recipients.Add(emailSettings.ItCcAddress);

        if (emailSettings.CcHrOnSubmissionEmails
            && !managerEmail.Equals(emailSettings.HrCcAddress, StringComparison.OrdinalIgnoreCase)
            && !recipients.Contains(emailSettings.HrCcAddress, StringComparer.OrdinalIgnoreCase))
            recipients.Add(emailSettings.HrCcAddress);

        return recipients;
    }
}
