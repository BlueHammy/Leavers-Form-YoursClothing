using ClosedXML.Excel;
using LeaversForm.Data.Entities;

namespace LeaversForm.Services;

public sealed class ExcelExportService : IExcelExportService
{
    public byte[] Export(IEnumerable<LeaverRequest> requests)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Leavers");
        var headers = new[] { "Name", "Entra ID", "UPN", "Email", "Department", "Leaving Date", "Equipment To Be Returned", "Special Requests", "Disable ASAP", "Status", "Submitted By", "Submitted At" };
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        var row = 2;
        foreach (var item in requests)
        {
            var values = new object?[] { item.LeaverDisplayName, item.LeaverEntraId, item.LeaverUserPrincipalName, item.LeaverEmail, item.Department, item.LeavingDate.ToString("dd/MM/yyyy"), item.EquipmentNotes, item.SpecialRequests, item.DisableAsap ? "Yes" : "No", item.Status.ToString(), item.SubmittedByName, item.SubmittedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm") };
            for (var col = 0; col < values.Length; col++) sheet.Cell(row, col + 1).Value = values[col]?.ToString() ?? string.Empty;
            row++;
        }
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
