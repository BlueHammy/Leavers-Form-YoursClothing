namespace LeaversForm.Web;

public static class AuthMode
{
    public static bool IsSemiLive(IWebHostEnvironment environment) =>
        environment.EnvironmentName.Equals("SemiLive", StringComparison.OrdinalIgnoreCase);

    public static bool UseDevelopmentBypass(IWebHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() &&
        string.IsNullOrWhiteSpace(configuration["AzureAd:TenantId"]) &&
        !configuration.GetValue("LeaversForm:RequireEntraSso", false);

    public static bool UseEntraSso(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["AzureAd:TenantId"]);
}
