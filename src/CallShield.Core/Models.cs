namespace CallShield.Core;

public enum ShieldMode
{
    Light,
    Strict,
    Total
}

public sealed record TargetApp(string DisplayName, string ExecutableName, bool Enabled = true);

public sealed record ScheduleSettings(bool Enabled, TimeOnly Start, TimeOnly End)
{
    public bool IsWithinWindow(DateTime now)
    {
        if (!Enabled)
        {
            return false;
        }

        var time = TimeOnly.FromDateTime(now);
        if (Start <= End)
        {
            return time >= Start && time < End;
        }

        return time >= Start || time < End;
    }
}

public sealed record ShieldSettings(
    bool IsOn,
    ShieldMode Mode,
    IReadOnlyList<TargetApp> TargetApps,
    ScheduleSettings Schedule);

public sealed record FirewallRuleSpec(
    string Name,
    string ProgramPath,
    string Direction,
    string Protocol,
    string Action,
    string? RemotePorts = null,
    string? Description = null);

public sealed record LogEntry(
    DateTime Timestamp,
    string Process,
    string Protocol,
    string Destination,
    string Action,
    string Detail);
