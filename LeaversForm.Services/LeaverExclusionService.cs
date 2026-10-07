using LeaversForm.Data;
using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace LeaversForm.Services;

public sealed record LeaverExclusionCheck(
    string? DisplayName,
    string? Email,
    string? UserPrincipalName,
    string? SamAccountName = null);

public sealed record LeaverExclusionEvaluation(bool IsExcluded, LeaverExclusion? MatchedRule);

public sealed class LeaverExclusionService(LeaversFormDbContext db) : ILeaverExclusionService
{
    public async Task<IReadOnlyList<LeaverExclusion>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await db.LeaverExclusions.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.MatchType)
            .ThenBy(x => x.MatchValue)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LeaverExclusion>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await db.LeaverExclusions.AsNoTracking()
            .OrderBy(x => x.MatchType)
            .ThenBy(x => x.MatchValue)
            .ToListAsync(cancellationToken);

    public async Task<LeaverExclusion> AddAsync(
        LeaverExclusionMatchType matchType,
        string matchValue,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var value = matchValue.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Enter a value for the exclusion.");

        var exclusion = new LeaverExclusion
        {
            MatchType = matchType,
            MatchValue = value,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            IsActive = true
        };
        db.LeaverExclusions.Add(exclusion);
        await db.SaveChangesAsync(cancellationToken);
        return exclusion;
    }

    public async Task DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var exclusion = await db.LeaverExclusions.FindAsync([id], cancellationToken)
            ?? throw new KeyNotFoundException("Exclusion not found.");
        exclusion.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LeaverExclusionEvaluation> EvaluateAsync(LeaverExclusionCheck candidate, CancellationToken cancellationToken = default)
    {
        var rules = await db.LeaverExclusions.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        foreach (var rule in rules)
        {
            if (IsMatch(rule, candidate))
                return new LeaverExclusionEvaluation(true, rule);
        }

        return new LeaverExclusionEvaluation(false, null);
    }

    public static bool IsMatch(LeaverExclusion rule, LeaverExclusionCheck candidate)
    {
        var displayName = candidate.DisplayName?.Trim() ?? string.Empty;
        var email = candidate.Email?.Trim() ?? string.Empty;
        var upn = candidate.UserPrincipalName?.Trim() ?? string.Empty;
        var sam = candidate.SamAccountName?.Trim() ?? string.Empty;
        var value = rule.MatchValue.Trim();

        return rule.MatchType switch
        {
            LeaverExclusionMatchType.AccountPrefix =>
                StartsWithPrefix(upn, value)
                || StartsWithPrefix(sam, value)
                || StartsWithPrefix(GetAccountName(upn), value),
            LeaverExclusionMatchType.DisplayNameExact => displayName.Equals(value, StringComparison.OrdinalIgnoreCase),
            LeaverExclusionMatchType.EmailExact => email.Equals(value, StringComparison.OrdinalIgnoreCase)
                || upn.Equals(value, StringComparison.OrdinalIgnoreCase),
            LeaverExclusionMatchType.TextContains => ContainsIgnoreCase(displayName, value)
                || ContainsIgnoreCase(email, value)
                || ContainsIgnoreCase(upn, value)
                || ContainsIgnoreCase(sam, value),
            _ => false
        };
    }

    private static bool StartsWithPrefix(string input, string prefix) =>
        !string.IsNullOrWhiteSpace(input)
        && input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static string GetAccountName(string upn)
    {
        if (string.IsNullOrWhiteSpace(upn))
            return string.Empty;

        var at = upn.IndexOf('@');
        return at < 0 ? upn : upn[..at];
    }

    private static bool ContainsIgnoreCase(string input, string value) =>
        !string.IsNullOrWhiteSpace(input)
        && input.Contains(value, StringComparison.OrdinalIgnoreCase);
}
