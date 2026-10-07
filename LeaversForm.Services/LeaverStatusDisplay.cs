using LeaversForm.Data.Enums;

namespace LeaversForm.Services;

public static class LeaverStatusDisplay
{
    public static string ToLabel(LeaverStatus status) => status switch
    {
        LeaverStatus.Scheduled => "Waiting",
        LeaverStatus.Pending => "Processing",
        LeaverStatus.Completed => "Done",
        LeaverStatus.Failed => "Needs IT help",
        LeaverStatus.AwaitingApproval => "Needs approval",
        LeaverStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };
}
