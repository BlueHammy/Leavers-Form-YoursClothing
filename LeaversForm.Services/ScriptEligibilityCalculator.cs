namespace LeaversForm.Services;

public sealed class ScriptEligibilityCalculator : IScriptEligibilityCalculator
{
    private readonly TimeZoneInfo _ukTimeZone;

    public ScriptEligibilityCalculator()
    {
        _ukTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "GMT Standard Time" : "Europe/London");
    }

    public DateTime Calculate(DateOnly leavingDate)
    {
        var local = leavingDate.ToDateTime(new TimeOnly(17, 0), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, _ukTimeZone);
    }
}
