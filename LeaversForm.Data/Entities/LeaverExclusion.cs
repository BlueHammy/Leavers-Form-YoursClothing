using LeaversForm.Data.Enums;

namespace LeaversForm.Data.Entities;

public class LeaverExclusion
{
    public int Id { get; set; }
    public LeaverExclusionMatchType MatchType { get; set; }
    public string MatchValue { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
