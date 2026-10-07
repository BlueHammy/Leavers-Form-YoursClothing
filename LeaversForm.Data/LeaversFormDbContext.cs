using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace LeaversForm.Data;

public class LeaversFormDbContext(DbContextOptions<LeaversFormDbContext> options) : DbContext(options)
{
    public DbSet<LeaverRequest> LeaverRequests => Set<LeaverRequest>();
    public DbSet<AuthorisingManager> AuthorisingManagers => Set<AuthorisingManager>();
    public DbSet<LeaverRequestAuthorisingManager> LeaverRequestAuthorisingManagers => Set<LeaverRequestAuthorisingManager>();
    public DbSet<LeaverExclusion> LeaverExclusions => Set<LeaverExclusion>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LeaverRequest>(entity =>
        {
            entity.Property(x => x.LeaverEntraId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LeaverDisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LeaverUserPrincipalName).HasMaxLength(320).IsRequired();
            entity.Property(x => x.LeaverEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.SamAccountName).HasMaxLength(100);
            entity.Property(x => x.Department).HasMaxLength(200);
            entity.Property(x => x.EquipmentNotes).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.SpecialRequests).HasMaxLength(4000);
            entity.Property(x => x.DisableAsap).HasDefaultValue(false);
            entity.Property(x => x.SubmittedByName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SubmittedByEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.ApprovedByName).HasMaxLength(200);
            entity.Property(x => x.ApprovedByEmail).HasMaxLength(320);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.ScriptExecutionStatus).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => new { x.LeaverEntraId, x.Status });
            entity.HasIndex(x => x.ScriptEligibleAt);
            entity.HasMany(x => x.AuditLogs).WithOne(x => x.LeaverRequest).HasForeignKey(x => x.LeaverRequestId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LeaverRequestAuthorisingManager>(entity =>
        {
            entity.HasIndex(x => new { x.LeaverRequestId, x.AuthorisingManagerId }).IsUnique();
            entity.HasOne(x => x.LeaverRequest).WithMany(x => x.AuthorisingManagers).HasForeignKey(x => x.LeaverRequestId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.AuthorisingManager).WithMany(x => x.LeaverRequests).HasForeignKey(x => x.AuthorisingManagerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LeaverExclusion>(entity =>
        {
            entity.Property(x => x.MatchType).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.MatchValue).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.HasIndex(x => x.IsActive);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.IsActive);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PerformedByName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PerformedByEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(4000);
        });

    }
}
