namespace LeaversForm.Data.Entities;

public class AuditLog
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public int? LeaverRequestId { get; set; }
    public string PerformedByName { get; set; } = string.Empty;
    public string PerformedByEmail { get; set; } = string.Empty;
    public DateTime PerformedAt { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Notes { get; set; }
    public LeaverRequest? LeaverRequest { get; set; }
}
