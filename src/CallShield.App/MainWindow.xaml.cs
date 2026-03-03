using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CallShield.Core;

namespace CallShield.App;

public partial class MainWindow : Window
{
    private readonly ShieldController _shieldController;
    private readonly IFirewallManager _firewallManager;
    private readonly ILogService _logService;
    private readonly DispatcherTimer _scheduleTimer;
    private bool _isShieldEnabled;

    public MainWindow()
    {
        InitializeComponent();

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CallShield");
        _firewallManager = new PowerShellFirewallManager();
        _logService = new JsonlLogService(appData);
        _shieldController = new ShieldController(_firewallManager, _logService);

        LogPathText.Text = $"Log local: {_logService.LogPath}";

        _scheduleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };

        _scheduleTimer.Tick += (_, _) => EvaluateSchedule();
        _scheduleTimer.Start();
    }

    private void ShieldToggle_On(object sender, RoutedEventArgs e)
    {
        EnableShield();
    }

    private void ShieldToggle_Off(object sender, RoutedEventArgs e)
    {
        DisableShield();
    }

    private void ApplyNow_OnClick(object sender, RoutedEventArgs e)
    {
        if (ShieldToggle.IsChecked == true)
        {
            EnableShield();
        }
        else
        {
            DisableShield();
        }
    }

    private void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _shieldController.Disable();
            _isShieldEnabled = false;
            ShieldToggle.IsChecked = false;
            SetStatus(false, "Reglas CallShield_* eliminadas. WebRTC permitido.");
            RuleSummary.Text = "Reset completado: no quedan reglas gestionadas por Call Shield.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void EvaluateSchedule()
    {
        if (ScheduleEnabled.IsChecked != true)
        {
            return;
        }

        if (!TimeOnly.TryParse(StartTimeText.Text, out var start) || !TimeOnly.TryParse(EndTimeText.Text, out var end))
        {
            return;
        }

        var schedule = new ScheduleSettings(true, start, end);
        var shouldEnable = schedule.IsWithinWindow(DateTime.Now);

        if (shouldEnable && !_isShieldEnabled)
        {
            ShieldToggle.IsChecked = true;
            EnableShield();
        }
        else if (!shouldEnable && _isShieldEnabled)
        {
            ShieldToggle.IsChecked = false;
            DisableShield();
        }
    }

    private void EnableShield()
    {
        try
        {
            var mode = GetSelectedMode();
            var targets = GetTargets();

            _shieldController.Enable(mode, targets);
            _isShieldEnabled = true;
            SetStatus(true, "WebRTC bloqueado");

            var rules = _firewallManager.BuildRules(mode, targets);
            RuleSummary.Text = "Reglas activas:\n" + string.Join("\n", rules.Select(r => $"- {r.Name} ({r.Protocol} {r.Direction})"));
            ShieldToggle.Content = "Shield ON";
        }
        catch (Exception ex)
        {
            ShieldToggle.IsChecked = false;
            ShowError(ex);
        }
    }

    private void DisableShield()
    {
        try
        {
            _shieldController.Disable();
            _isShieldEnabled = false;
            SetStatus(false, "WebRTC permitido");
            RuleSummary.Text = "Sin reglas activas.";
            ShieldToggle.Content = "Shield OFF";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private ShieldMode GetSelectedMode()
    {
        return ModeSelector.SelectedIndex switch
        {
            0 => ShieldMode.Light,
            1 => ShieldMode.Strict,
            2 => ShieldMode.Total,
            _ => ShieldMode.Light
        };
    }

    private IReadOnlyList<TargetApp> GetTargets()
    {
        return
        [
            new TargetApp("Chrome", "chrome.exe", ChromeCheck.IsChecked == true),
            new TargetApp("Edge", "msedge.exe", EdgeCheck.IsChecked == true),
            new TargetApp("Firefox", "firefox.exe", FirefoxCheck.IsChecked == true)
        ];
    }

    private void SetStatus(bool blocked, string text)
    {
        StatusLabel.Text = text;
        StatusLabel.Foreground = blocked ? Brushes.DarkRed : Brushes.DarkGreen;
    }

    private static void ShowError(Exception ex)
    {
        MessageBox.Show(ex.Message, "Call Shield", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
