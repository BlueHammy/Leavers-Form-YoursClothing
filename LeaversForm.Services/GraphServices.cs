using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace LeaversForm.Services;

public sealed class GraphUserSearchService(
    HttpClient http,
    IConfiguration configuration,
    IOptions<GraphOptions> graphOptions,
    ILogger<GraphUserSearchService> logger) : IGraphUserSearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly GraphOptions _options = graphOptions.Value;

    public async Task<IReadOnlyList<GraphUser>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        if (string.IsNullOrWhiteSpace(configuration["AzureAd:TenantId"]))
            return SearchDevUsers(query);
        try
        {
            var term = query.Trim().Replace("\\", "\\\\").Replace("\"", "\\\"");
            var syncFilter = _options.RequireOnPremisesSyncEnabled
                ? " and onPremisesSyncEnabled eq true"
                : string.Empty;
            var url = "https://graph.microsoft.com/v1.0/users" +
                $"?$search=\"displayName:{term}\" OR \"mail:{term}\" OR \"userPrincipalName:{term}\" OR \"onPremisesSamAccountName:{term}\"" +
                "&$select=id,displayName,userPrincipalName,mail,department,accountEnabled,onPremisesSamAccountName,onPremisesSyncEnabled,userType" +
                "&$filter=accountEnabled eq true and userType eq 'Member'" +
                syncFilter +
                "&$orderby=displayName" +
                "&$top=25";
            var users = JsonSerializer.Deserialize<GraphCollection>(
                await GetJsonAsync(url, cancellationToken, advancedQuery: true), JsonOptions)?.Value ?? [];

            var filtered = users
                .Where(user => user.AccountEnabled && !string.IsNullOrWhiteSpace(user.Id))
                .Where(IsAllowedInternalUser)
                .Take(20)
                .ToList();

            logger.LogInformation("Graph user search for {Query} returned {Count} users after internal filtering", query, filtered.Count);
            return filtered;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph user search failed for {Query}", query);
            return [];
        }
    }

    private bool IsAllowedInternalUser(GraphUser user)
    {
        if (user.UserPrincipalName.Contains("#EXT#", StringComparison.OrdinalIgnoreCase))
            return false;

        if (_options.RequireOnPremisesSyncEnabled && user.OnPremisesSyncEnabled != true)
            return false;

        var emailDomain = GetEmailDomain(user.Mail ?? user.UserPrincipalName);
        if (string.IsNullOrWhiteSpace(emailDomain))
            return false;

        return _options.AllowedEmailDomains.Any(domain =>
            emailDomain.Equals(domain.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string GetEmailDomain(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;

        var at = address.LastIndexOf('@');
        return at < 0 ? string.Empty : address[(at + 1)..].Trim();
    }

    private static IReadOnlyList<GraphUser> SearchDevUsers(string query) =>
        DevUsers.Where(u => u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || u.UserPrincipalName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || u.Mail?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
            || u.OnPremisesSamAccountName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true).ToList();

    private static readonly GraphUser[] DevUsers =
    [
        new("dev-user-001", "Alex Johnson", "alex.johnson@yoursclothing.co.uk", "alex.johnson@yoursclothing.co.uk", "IT", true, "alex.johnson", true),
        new("dev-user-002", "Sam Taylor", "sam.taylor@yoursclothing.co.uk", "sam.taylor@yoursclothing.co.uk", "Warehouse", true, "sam.taylor", true),
        new("dev-user-003", "Jordan Lee", "jordan.lee@yoursclothing.co.uk", "jordan.lee@yoursclothing.co.uk", "Retail", true, "jordan.lee", true)
    ];

    public async Task<GraphUser?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = JsonSerializer.Deserialize<GraphUser>(
                await GetJsonAsync($"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(id)}?$select=id,displayName,userPrincipalName,mail,department,accountEnabled,onPremisesSamAccountName,onPremisesSyncEnabled,userType", cancellationToken),
                JsonOptions);
            return user is { AccountEnabled: true } && IsAllowedInternalUser(user) ? user : null;
        }
        catch (Exception ex) { logger.LogError(ex, "Graph user lookup failed for {Id}", id); return null; }
    }

    private async Task<string> GetJsonAsync(string url, CancellationToken cancellationToken, bool advancedQuery = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellationToken));
        if (advancedQuery)
            request.Headers.Add("ConsistencyLevel", "eventual");
        return await (await http.SendAsync(request, cancellationToken)).EnsureSuccessStatusCode().Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var section = configuration.GetSection("AzureAd");
        var app = ConfidentialClientApplicationBuilder.Create(section["ClientId"]).WithClientSecret(section["ClientSecret"]).WithAuthority($"{section["Instance"]}{section["TenantId"]}").Build();
        return (await app.AcquireTokenForClient(["https://graph.microsoft.com/.default"]).ExecuteAsync(cancellationToken)).AccessToken;
    }

    private sealed class GraphCollection
    {
        [JsonPropertyName("value")]
        public List<GraphUser> Value { get; set; } = [];
    }
}

public sealed class GraphEmailService(
    HttpClient http,
    IConfiguration configuration,
    GraphOptions options,
    ILeaversFormEmailSettingsService emailSettings,
    ILogger<GraphEmailService> logger) : IGraphEmailService
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var redirectTarget = emailSettings.RedirectAllEmailsTo;
        var redirecting = !string.IsNullOrWhiteSpace(redirectTarget);
        if (options.EmailTestMode && !redirecting)
        {
            logger.LogInformation("Email test mode: {To} {Subject}", message.To, message.Subject);
            return;
        }
        var originalTo = message.To;
        var originalCc = message.Cc;
        var destination = redirecting ? redirectTarget : message.To;
        var subject = redirecting ? $"[EMAIL TEST - originally for {originalTo}] {message.Subject}" : message.Subject;
        var body = redirecting
            ? $"""
              <div style="padding:12px 14px;background:#fff8df;border-left:4px solid #d69e2e;margin-bottom:18px;font-family:Segoe UI,Arial,sans-serif;">
                <strong>Leavers Form email test</strong><br>
                Originally intended for: {System.Net.WebUtility.HtmlEncode(originalTo)}
                {(originalCc is null || originalCc.Count == 0 ? string.Empty : $"<br>Original CC: {System.Net.WebUtility.HtmlEncode(string.Join(", ", originalCc))}")}
              </div>
              {message.Body}
              """
            : message.Body;
        var ccRecipients = redirecting
            ? Array.Empty<string>()
            : BuildCcRecipients(message);
        var payload = new
        {
            message = new
            {
                subject,
                body = new { contentType = "HTML", content = body },
                toRecipients = new[] { new { emailAddress = new { address = destination } } },
                ccRecipients = ccRecipients.Select(address => new { emailAddress = new { address } }).ToArray()
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(configuration["Graph:SenderMailbox"] ?? options.SenderMailbox)}/sendMail")
        { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellationToken));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "Graph sendMail failed ({StatusCode}) for {To}: {ErrorBody}",
                (int)response.StatusCode,
                destination,
                errorBody);
            response.EnsureSuccessStatusCode();
        }
    }

    private IReadOnlyList<string> BuildCcRecipients(EmailMessage message)
    {
        if (message.To.Equals(emailSettings.ErrorAlertMailbox, StringComparison.OrdinalIgnoreCase))
            return message.Cc ?? [];

        var recipients = new List<string>();
        if (message.Cc is { Count: > 0 })
            recipients.AddRange(message.Cc);

        if (recipients.Count == 0
            && !message.To.Equals(emailSettings.ItCcAddress, StringComparison.OrdinalIgnoreCase))
            recipients.Add(emailSettings.ItCcAddress);

        return recipients
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Where(address => !address.Equals(message.To, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var section = configuration.GetSection("AzureAd");
        var app = ConfidentialClientApplicationBuilder.Create(section["ClientId"]).WithClientSecret(section["ClientSecret"]).WithAuthority($"{section["Instance"]}{section["TenantId"]}").Build();
        return (await app.AcquireTokenForClient(["https://graph.microsoft.com/.default"]).ExecuteAsync(cancellationToken)).AccessToken;
    }
}
