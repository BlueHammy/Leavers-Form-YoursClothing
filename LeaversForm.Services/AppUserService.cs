using LeaversForm.Data;
using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace LeaversForm.Services;

public sealed class AppUserService(LeaversFormDbContext db) : IAppUserService
{
    public async Task<AppRole> GetRoleByEmailAsync(string? email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return AppRole.User;

        var normalized = LeaversRoles.NormalizeEmail(email);
        var user = await db.AppUsers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IsActive && x.Email == normalized, cancellationToken);

        return user?.Role ?? AppRole.User;
    }

    public async Task<IReadOnlyList<AppUser>> ListElevatedUsersAsync(CancellationToken cancellationToken = default) =>
        await db.AppUsers.AsNoTracking()
            .Where(x => x.IsActive && x.Role != AppRole.User)
            .OrderByDescending(x => x.Role)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

    public async Task<AppUser> AssignRoleAsync(
        string email,
        string displayName,
        AppRole role,
        CancellationToken cancellationToken = default)
    {
        if (role == AppRole.User)
            throw new ArgumentException("Use remove to return someone to a normal user.");

        var normalized = LeaversRoles.NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Enter an email address.");

        var name = displayName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Enter a display name.");

        var existing = await db.AppUsers.SingleOrDefaultAsync(x => x.Email == normalized, cancellationToken);
        if (existing is null)
        {
            existing = new AppUser
            {
                Email = normalized,
                DisplayName = name,
                Role = role,
                IsActive = true
            };
            db.AppUsers.Add(existing);
        }
        else
        {
            existing.DisplayName = name;
            existing.Role = role;
            existing.IsActive = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task RemoveElevatedAccessAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await db.AppUsers.FindAsync([id], cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");

        if (user.Role == AppRole.SuperUser)
        {
            var remainingSuperUsers = await db.AppUsers.CountAsync(
                x => x.IsActive && x.Role == AppRole.SuperUser && x.Id != id,
                cancellationToken);
            if (remainingSuperUsers == 0)
                throw new InvalidOperationException("At least one super user must remain.");
        }

        user.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
    }
}
