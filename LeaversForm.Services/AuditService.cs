using System.Security.Claims;
using LeaversForm.Data;
using LeaversForm.Data.Entities;

namespace LeaversForm.Services;

public sealed class AuditService(LeaversFormDbContext db) : IAuditService
{
    public async Task AddAsync(string action, ClaimsPrincipal? performer, int? leaverRequestId = null,
        string? oldValue = null, string? newValue = null, string? notes = null, CancellationToken cancellationToken = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            LeaverRequestId = leaverRequestId,
            PerformedByName = performer?.Identity?.Name ?? "System",
            PerformedByEmail = performer?.FindFirst(ClaimTypes.Email)?.Value ?? "system",
            PerformedAt = DateTime.UtcNow,
            OldValue = oldValue,
            NewValue = newValue,
            Notes = notes
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
