using Microsoft.Extensions.Hosting;

namespace LeaversForm.Services;

internal static class LocalAppSettingsPaths
{
    public static string? Resolve(IHostEnvironment environment)
    {
        var contentRoot = environment.ContentRootPath;
        if (environment.EnvironmentName.Equals("SemiLive", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(contentRoot, "appsettings.SemiLive.local.json");

        if (environment.IsDevelopment())
            return Path.Combine(contentRoot, "appsettings.Development.local.json");

        if (environment.IsProduction())
            return Path.Combine(contentRoot, "appsettings.Production.local.json");

        var lanTest = Path.Combine(contentRoot, "appsettings.LanTest.local.json");
        return File.Exists(lanTest) ? lanTest : null;
    }
}
