using LeaversForm.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaversForm.Web.Pages.Leavers;

[Authorize(Policy = "LeaversAdmin")]
public class ExportModel(ILeaverRequestService requests, IExcelExportService export) : PageModel
{
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var rows = await requests.ListAsync(viewer: User, cancellationToken: cancellationToken);
        return File(export.Export(rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"leavers-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }
}
