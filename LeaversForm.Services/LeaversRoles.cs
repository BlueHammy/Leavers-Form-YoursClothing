using System.Security.Claims;
using LeaversForm.Data.Enums;
namespace LeaversForm.Services;

public static class LeaversRoles
{
    public const string SuperUser = nameof(AppRole.SuperUser);
    public const string Admin = nameof(AppRole.Admin);
    public const string User = nameof(AppRole.User);

    public static string? GetEmail(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Email)?.Value
        ?? user.FindFirst("preferred_username")?.Value
        ?? user.FindFirst(ClaimTypes.Upn)?.Value;

    public static bool IsSuperUser(ClaimsPrincipal user) => user.IsInRole(SuperUser);

    public static bool IsAdminOrAbove(ClaimsPrincipal user) =>
        IsSuperUser(user) || user.IsInRole(Admin);

    public static bool CanManageAllRequests(ClaimsPrincipal user) => IsAdminOrAbove(user);

    public static bool CanManageExclusions(ClaimsPrincipal user) => IsSuperUser(user);

    public static bool CanManageAppRoles(ClaimsPrincipal user) => IsSuperUser(user);

    public static bool CanViewRequest(ClaimsPrincipal user, Data.Entities.LeaverRequest request) =>
        request.Status != LeaverStatus.Cancelled
        && (CanManageAllRequests(user)
            || EmailsMatch(GetEmail(user), request.SubmittedByEmail)
            || IsAuthorisingManager(user, request));

    public static bool CanApproveRequest(ClaimsPrincipal user, Data.Entities.LeaverRequest request)
    {
        if (request.Status != LeaverStatus.AwaitingApproval)
            return false;

        if (EmailsMatch(GetEmail(user), request.SubmittedByEmail))
            return false;

        return IsAdminOrAbove(user) || IsAuthorisingManager(user, request);
    }

    private static bool IsAuthorisingManager(ClaimsPrincipal user, Data.Entities.LeaverRequest request)
    {
        var email = GetEmail(user);
        if (string.IsNullOrWhiteSpace(email))
            return false;

        return request.AuthorisingManagers.Any(link =>
            link.AuthorisingManager is { IsActive: true }
            && EmailsMatch(email, link.AuthorisingManager.Email));
    }

    public static bool CanEditRequest(ClaimsPrincipal user, Data.Entities.LeaverRequest request) =>
        IsAdminOrAbove(user)
        && request.Status is LeaverStatus.Scheduled;

    public static bool CanCancelRequest(ClaimsPrincipal user, Data.Entities.LeaverRequest request) =>
        IsAdminOrAbove(user)
        && request.Status is LeaverStatus.Scheduled
            or LeaverStatus.Pending
            or LeaverStatus.Failed
            or LeaverStatus.AwaitingApproval;

    public static bool CanRetryRequest(ClaimsPrincipal user, Data.Entities.LeaverRequest request) =>
        IsSuperUser(user) && request.Status == LeaverStatus.Failed;

    public static string DescribeRole(AppRole role) => role switch
    {
        AppRole.SuperUser => "Super user",
        AppRole.Admin => "Admin",
        _ => "User"
    };

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static bool EmailsMatch(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(NormalizeEmail(left), NormalizeEmail(right), StringComparison.Ordinal);
}
