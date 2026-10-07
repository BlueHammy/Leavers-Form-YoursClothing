using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using LeaversForm.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaversForm.Web.Pages.Leavers;

public class ListModel(ILeaverRequestService requests, ILeaverRequestStatusPresenter statusPresenter) : PageModel
{
    public IReadOnlyList<LeaverRequest> Requests { get; private set; } = [];
    public IReadOnlyDictionary<int, string?> StatusDetails { get; private set; } = new Dictionary<int, string?>();
    public string? Search { get; set; }
    public int? HighlightRequestId { get; set; }
    public bool ShowExpanded { get; set; }
    public bool CanManageRequests => LeaversRoles.IsAdminOrAbove(User);

    public bool CanEditRequest(LeaverRequest request) => LeaversRoles.CanEditRequest(User, request);
    public bool CanRetryRequest(LeaverRequest request) => LeaversRoles.CanRetryRequest(User, request);

    public async Task OnGetAsync(string? search, int? highlight, bool expanded = false)
    {
        Search = search;
        ShowExpanded = expanded && CanManageRequests;
        HighlightRequestId = highlight;
        if (!HighlightRequestId.HasValue && TempData["HighlightRequestId"] is int tempId)
            HighlightRequestId = tempId;
        else if (!HighlightRequestId.HasValue && TempData["HighlightRequestId"] is string tempText && int.TryParse(tempText, out var parsed))
            HighlightRequestId = parsed;

        Requests = await requests.ListAsync(search, viewer: User);
        StatusDetails = Requests.ToDictionary(
            request => request.Id,
            request => statusPresenter.BuildStatusDetail(request));
    }
}
