using LeaversForm.Data.Enums;

namespace LeaversForm.Data.Entities;

public class LeaverRequest
{
    public int Id { get; set; }
    public string LeaverEntraId { get; set; } = string.Empty;
    public string LeaverDisplayName { get; set; } = string.Empty;
    public string LeaverUserPrincipalName { get; set; } = string.Empty;
    public string LeaverEmail { get; set; } = string.Empty;
    public string? SamAccountName { get; set; }
    public string? Department { get; set; }
    public DateOnly LeavingDate { get; set; }
    public string EquipmentNotes { get; set; } = string.Empty;
    public string? SpecialRequests { get; set; }
    public bool DisableAsap { get; set; }
    public LeaverStatus Status { get; set; } = LeaverStatus.Scheduled;
    public string SubmittedByName { get; set; } = string.Empty;
    public string SubmittedByEmail { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string? LastUpdatedByName { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    public DateTime ScriptEligibleAt { get; set; }
    public DateTime? ScriptExecutedAt { get; set; }
    public DateTime? ScriptLastAttemptAt { get; set; }
    public DateTime? ScriptLastFailedAt { get; set; }
    public int ScriptAutoRetryCount { get; set; }
    public ScriptExecutionStatus ScriptExecutionStatus { get; set; } = ScriptExecutionStatus.NotRun;
    public string? ScriptExecutionLogPath { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledByName { get; set; }
    public string? CancelledByEmail { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByName { get; set; }
    public string? ApprovedByEmail { get; set; }
    public ICollection<LeaverRequestAuthorisingManager> AuthorisingManagers { get; set; } = new List<LeaverRequestAuthorisingManager>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
