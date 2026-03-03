using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace CallShield.Core;

public interface ILogService
{
    void Write(LogEntry entry);
    string LogPath { get; }
}

public sealed class JsonlLogService : ILogService
{
    private readonly object _sync = new();
    public string LogPath { get; }

    public JsonlLogService(string appDataRoot)
    {
        Directory.CreateDirectory(appDataRoot);
        LogPath = Path.Combine(appDataRoot, "events.jsonl");
    }

    public void Write(LogEntry entry)
    {
        var line = JsonSerializer.Serialize(entry);
        lock (_sync)
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
    }
}

public interface IFirewallManager
{
    bool IsElevated();
    void EnsureElevation();
    IReadOnlyList<FirewallRuleSpec> BuildRules(ShieldMode mode, IEnumerable<TargetApp> targets);
    void ApplyRules(IEnumerable<FirewallRuleSpec> rules);
    void ResetRules();
}

public sealed class PowerShellFirewallManager : IFirewallManager
{
    public const string RulePrefix = "CallShield_";

    public bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public void EnsureElevation()
    {
        if (IsElevated())
        {
            return;
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unable to resolve process path.");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas"
        };

        Process.Start(psi);
        Environment.Exit(0);
    }

    public IReadOnlyList<FirewallRuleSpec> BuildRules(ShieldMode mode, IEnumerable<TargetApp> targets)
    {
        var activeTargets = targets.Where(t => t.Enabled).ToList();
        var rules = new List<FirewallRuleSpec>();

        foreach (var target in activeTargets)
        {
            var programPath = $"%ProgramFiles%\\Google\\Chrome\\Application\\{target.ExecutableName}";
            if (target.ExecutableName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
            {
                programPath = "%ProgramFiles(x86)%\\Microsoft\\Edge\\Application\\msedge.exe";
            }

            if (target.ExecutableName.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase))
            {
                programPath = "%ProgramFiles%\\Mozilla Firefox\\firefox.exe";
            }

            rules.AddRange(BuildRulesForProcess(mode, target.ExecutableName, programPath));
        }

        return rules;
    }

    public void ApplyRules(IEnumerable<FirewallRuleSpec> rules)
    {
        foreach (var rule in rules)
        {
            var builder = new StringBuilder();
            builder.Append("New-NetFirewallRule ");
            builder.Append($"-DisplayName '{Escape(rule.Name)}' ");
            builder.Append($"-Direction {rule.Direction} ");
            builder.Append($"-Action {rule.Action} ");
            builder.Append($"-Program '{Escape(rule.ProgramPath)}' ");
            builder.Append($"-Protocol {rule.Protocol} ");

            if (!string.IsNullOrWhiteSpace(rule.RemotePorts))
            {
                builder.Append($"-RemotePort {rule.RemotePorts} ");
            }

            if (!string.IsNullOrWhiteSpace(rule.Description))
            {
                builder.Append($"-Description '{Escape(rule.Description)}' ");
            }

            ExecutePowerShell($"Remove-NetFirewallRule -DisplayName '{Escape(rule.Name)}' -ErrorAction SilentlyContinue; {builder}");
        }
    }

    public void ResetRules()
    {
        ExecutePowerShell($"Get-NetFirewallRule -DisplayName '{RulePrefix}*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule");
    }

    private static IEnumerable<FirewallRuleSpec> BuildRulesForProcess(ShieldMode mode, string executableName, string programPath)
    {
        var normalized = Path.GetFileNameWithoutExtension(executableName).ToLowerInvariant();

        yield return mode switch
        {
            ShieldMode.Light => new FirewallRuleSpec(
                $"{RulePrefix}Light_Block_UDP_Out_{normalized}",
                programPath,
                "Outbound",
                "UDP",
                "Block",
                Description: "Blocks outbound UDP for WebRTC media transport while preserving HTTPS/WebSocket signaling."),
            ShieldMode.Strict => new FirewallRuleSpec(
                $"{RulePrefix}Strict_Block_UDP_Out_{normalized}",
                programPath,
                "Outbound",
                "UDP",
                "Block",
                Description: "Blocks outbound UDP to disrupt ICE/STUN media paths."),
            ShieldMode.Total => new FirewallRuleSpec(
                $"{RulePrefix}Total_Block_All_Out_{normalized}",
                programPath,
                "Outbound",
                "Any",
                "Block",
                Description: "Panic mode: blocks all outbound connectivity for target process."),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

        if (mode == ShieldMode.Strict)
        {
            yield return new FirewallRuleSpec(
                $"{RulePrefix}Strict_Block_TURN_TCP_{normalized}",
                programPath,
                "Outbound",
                "TCP",
                "Block",
                "3478,3479,5349,19302-19309",
                "Blocks common TURN/STUN TCP fallback ports. Compatibility list can exempt apps if needed.");
        }

        if (mode == ShieldMode.Total)
        {
            yield return new FirewallRuleSpec(
                $"{RulePrefix}Total_Block_All_In_{normalized}",
                programPath,
                "Inbound",
                "Any",
                "Block",
                Description: "Panic mode: blocks all inbound connectivity for target process.");
        }
    }

    private static void ExecutePowerShell(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Firewall command failed. Exit={process.ExitCode}. StdOut={stdout}. StdErr={stderr}");
        }
    }

    private static string Escape(string value) => value.Replace("'", "''");
}

public sealed class ShieldController
{
    private readonly IFirewallManager _firewall;
    private readonly ILogService _log;

    public ShieldController(IFirewallManager firewall, ILogService log)
    {
        _firewall = firewall;
        _log = log;
    }

    public void Enable(ShieldMode mode, IReadOnlyList<TargetApp> targets)
    {
        _firewall.EnsureElevation();
        _firewall.ResetRules();
        var rules = _firewall.BuildRules(mode, targets);
        _firewall.ApplyRules(rules);

        foreach (var target in targets.Where(t => t.Enabled))
        {
            _log.Write(new LogEntry(DateTime.UtcNow, target.ExecutableName, mode.ToString(), "N/A", "blocked", "Shield enabled"));
        }
    }

    public void Disable()
    {
        _firewall.EnsureElevation();
        _firewall.ResetRules();
        _log.Write(new LogEntry(DateTime.UtcNow, "all", "Any", "N/A", "allowed", "Shield disabled/reset"));
    }
}
