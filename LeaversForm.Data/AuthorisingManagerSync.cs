using LeaversForm.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LeaversForm.Data;

public static class AuthorisingManagerSync
{
    public const string ConfigurationSection = "AuthorisingManagers";

    public static Dictionary<string, string> ReadConfiguredManagers(IConfiguration configuration) =>
        configuration.GetSection(ConfigurationSection).Get<Dictionary<string, string>>()
        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static async Task SyncFromConfigurationAsync(
        LeaversFormDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var configured = ReadConfiguredManagers(configuration);
        var existing = await db.AuthorisingManagers.ToListAsync(cancellationToken);
        var configuredNames = new HashSet<string>(configured.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var (displayName, email) in configured.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(email))
                continue;

            var manager = existing.FirstOrDefault(x =>
                x.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));

            if (manager is null)
            {
                db.AuthorisingManagers.Add(new AuthorisingManager
                {
                    DisplayName = displayName.Trim(),
                    Email = email.Trim(),
                    IsActive = true
                });
                continue;
            }

            manager.DisplayName = displayName.Trim();
            manager.Email = email.Trim();
            manager.IsActive = true;
        }

        foreach (var manager in existing)
        {
            if (!configuredNames.Contains(manager.DisplayName))
                manager.IsActive = false;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
