using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NjcAI.Monitor.Core;
using NjcAI.Monitor.Rendering;
using NjcAI.Monitor.Services;
using NjcAI.Monitor.UI;
using NjcAI.Monitor.Usb;
using Application = System.Windows.Application;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace NjcAI.Monitor;

internal sealed class AppController : IDisposable
{
    private readonly Application _application;
    private readonly DashboardWindow _dashboard;
    private readonly SettingsWindow _settingsWindow;
    private readonly CompletionAlertWindow _completionAlertWindow;
    private readonly Icon _applicationIcon;
    private readonly Forms.NotifyIcon _tray;
    private readonly DashboardRenderer _renderer = new();
    private readonly SystemMetricsCollector _systemCollector = new();
    private readonly AgentUsageCollector _agentCollector = new();
    private readonly AgentCompletionTracker _completionTracker = new();
    private readonly MarketDataCollector _marketCollector = new();
    private readonly NewsDataCollector _newsCollector = new();
    private readonly WeatherDataCollector _weatherCollector = new();
    private readonly UsbDisplayService _usb = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _informationRefresh = new(1, 1);
    private readonly bool _demoMode;
    private readonly bool _idleDemo;
    private readonly bool _completedDemo;
    private readonly bool _forcePreview;
    private AppSettings _settings;
    private Task? _renderLoop;
    private Task? _marketLoop;
    private Task? _informationLoop;
    private SystemSnapshot _system = SystemSnapshot.Empty;
    private AgentsSnapshot _agents = AgentsSnapshot.Empty;
    private BitmapSource? _latestFrame;
    private bool _autoPreviewShown;
    private bool _disposed;

    public AppController(Application application, IReadOnlyList<string> arguments)
    {
        _application = application;
        _settings = AppSettings.Load();
        _idleDemo = arguments.Contains("--demo-idle", StringComparer.OrdinalIgnoreCase);
        _completedDemo = arguments.Contains("--demo-completed", StringComparer.OrdinalIgnoreCase);
        _demoMode = _idleDemo || _completedDemo ||
                    arguments.Contains("--demo", StringComparer.OrdinalIgnoreCase);
        _forcePreview = arguments.Contains("--preview", StringComparer.OrdinalIgnoreCase);
        _dashboard = new DashboardWindow();
        _settingsWindow = new SettingsWindow(_settings);
        _completionAlertWindow = new CompletionAlertWindow(_settings.Theme);
        _dashboard.UsbDeviceChanged += () => Task.Run(_usb.Reconnect);
        _settingsWindow.Saved += settings =>
        {
            _settings = settings;
            _settingsWindow.Refresh(settings);
            _completionAlertWindow.ApplyTheme(settings.Theme);
            RefreshCompletionAlertWindow();
            _ = Task.Run(() => RefreshInformationOnceAsync(_shutdown.Token));
        };
        _settingsWindow.DiagnoseRequested += ShowUsbDiagnosis;
        _completionAlertWindow.Acknowledged += AcknowledgeCompletionAlert;
        _completionAlertWindow.AcknowledgeAllRequested += ClearCompletionAlerts;

        _applicationIcon = LoadApplicationIcon();
        _tray = new Forms.NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "NJC AI Monitor",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };
        _tray.DoubleClick += (_, _) => ShowPreview();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
            {
                var icon = Icon.ExtractAssociatedIcon(executable);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // A missing shell icon must not prevent the tray application from starting.
        }
        return (Icon)SystemIcons.Application.Clone();
    }

    public void Start()
    {
        Log.Info("NJC AI Monitor starting");
        RefreshCompletionAlertWindow();
        if (_forcePreview)
        {
            ShowPreview();
        }
        if (!_demoMode)
        {
            _marketLoop = Task.Run(() => MarketLoopAsync(_shutdown.Token));
            _informationLoop = Task.Run(() => InformationLoopAsync(_shutdown.Token));
        }
        _renderLoop = Task.Run(() => RenderLoopAsync(_shutdown.Token));
    }

    private async Task MarketLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _ = await _marketCollector.CollectAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Log.Warn($"Market update failed: {exception.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task InformationLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshInformationOnceAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Log.Warn($"News/weather update failed: {exception.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RefreshInformationOnceAsync(CancellationToken cancellationToken)
    {
        await _informationRefresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = _settings;
            await Task.WhenAll(
                _newsCollector.CollectAsync(cancellationToken),
                _weatherCollector.CollectAsync(
                    settings.WeatherLocationName,
                    settings.WeatherLatitude,
                    settings.WeatherLongitude,
                    cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            _informationRefresh.Release();
        }
    }

    private async Task RenderLoopAsync(CancellationToken cancellationToken)
    {
        var lastMetrics = DateTime.MinValue;
        var lastAgents = DateTime.MinValue;
        while (!cancellationToken.IsCancellationRequested)
        {
            var frameStarted = DateTime.UtcNow;
            try
            {
                DashboardSnapshot snapshot;
                if (_demoMode)
                {
                    snapshot = DemoData.Create(_idleDemo, _completedDemo);
                    _system = snapshot.System;
                    _agents = snapshot.Agents;
                }
                else
                {
                    if ((frameStarted - lastMetrics).TotalMilliseconds >= 900)
                    {
                        _system = _systemCollector.Collect();
                        lastMetrics = frameStarted;
                    }
                    if ((frameStarted - lastAgents).TotalMilliseconds >= 1500)
                    {
                        var completionUpdate = _completionTracker.Update(
                            _agentCollector.Collect(), DateTime.Now);
                        _agents = completionUpdate.Snapshot;
                        if (completionUpdate.CompletedAgents.Count > 0 || completionUpdate.PendingChanged)
                        {
                            var completed = completionUpdate.CompletedAgents.ToArray();
                            var pending = completionUpdate.PendingAlerts.ToArray();
                            _ = _application.Dispatcher.BeginInvoke(() =>
                            {
                                HandleNewAgentCompletions(completed);
                                RefreshCompletionAlertWindow(pending);
                            });
                        }
                        lastAgents = frameStarted;
                    }
                    snapshot = new DashboardSnapshot(
                        _system,
                        _agents,
                        _marketCollector.Current,
                        _newsCollector.Current,
                        _weatherCollector.Current);
                }

                var connected = _usb.EnsureConnected();
                var status = connected
                    ? $"{_usb.Status} · {_usb.FramesSent:N0} frames · {_usb.LastFrameBytes / 1024:N0} KB"
                    : _usb.Status;
                using var bitmap = _renderer.Render(snapshot, _settings, status);
                var source = DashboardWindow.ToBitmapSource(bitmap);
                _latestFrame = source;

                if (connected)
                {
                    var jpeg = DashboardRenderer.EncodeJpeg(
                        bitmap, _settings.JpegQuality, _settings.RotateDisplay180);
                    if (jpeg.Length > 0)
                    {
                        _ = _usb.SendFrame(jpeg);
                    }
                }

                _ = _application.Dispatcher.BeginInvoke(() =>
                {
                    if (_disposed)
                    {
                        return;
                    }
                    _dashboard.SetFrame(source, status);
                    UpdateTray(status);
                    if (!connected && _settings.ShowPreviewWhenDisconnected && !_autoPreviewShown)
                    {
                        _autoPreviewShown = true;
                        ShowPreview();
                    }
                });

                var active = _agents.Sessions.Any(agent =>
                                 agent.IsWorking || agent.NeedsAttention ||
                                 agent.CompletedAt is not null) ||
                             _system.CpuPercent >= 70;
                var fps = Math.Max(1, active ? _settings.ActiveFramesPerSecond : _settings.IdleFramesPerSecond);
                var frameBudget = TimeSpan.FromSeconds(1d / fps);
                var elapsed = DateTime.UtcNow - frameStarted;
                if (elapsed < frameBudget)
                {
                    await Task.Delay(frameBudget - elapsed, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Log.Error("Render loop failed", exception);
                try
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Preview", null, (_, _) => Dispatch(ShowPreview));
        menu.Items.Add("Settings", null, (_, _) => Dispatch(ShowSettings));
        menu.Items.Add("Reconnect LCD", null, (_, _) => Task.Run(_usb.Reconnect));
        menu.Items.Add("Clear Completion Alerts", null, (_, _) => Dispatch(ClearCompletionAlerts));
        menu.Items.Add("USB Diagnostics", null, (_, _) => Dispatch(ShowUsbDiagnosis));
        menu.Items.Add("Export Current Frame…", null, (_, _) => Dispatch(ExportSnapshot));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatch(Exit));
        return menu;
    }

    private void Dispatch(Action action)
    {
        if (_application.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _application.Dispatcher.BeginInvoke(action);
        }
    }

    private void ShowPreview()
    {
        if (!_dashboard.IsVisible)
        {
            _dashboard.Show();
        }
        if (_dashboard.WindowState == WindowState.Minimized)
        {
            _dashboard.WindowState = WindowState.Normal;
        }
        _dashboard.Activate();
    }

    private void ShowSettings()
    {
        _settingsWindow.Refresh(_settings);
        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
    }

    private void ShowUsbDiagnosis()
    {
        var diagnosis = _usb.Diagnose();
        MessageBox.Show(diagnosis, "NJC AI Monitor — USB Diagnostics", MessageBoxButton.OK,
            diagnosis.StartsWith("Detected", StringComparison.Ordinal)
                ? MessageBoxImage.Information
                : MessageBoxImage.Warning);
    }

    private void ExportSnapshot()
    {
        if (_latestFrame is null)
        {
            MessageBox.Show("No frame has been generated yet.", "NJC AI Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Export 1920×400 Frame",
            Filter = "PNG Image|*.png|JPEG Image|*.jpg",
            FileName = $"NJC-AI-Monitor-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            AddExtension = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        BitmapEncoder encoder = string.Equals(Path.GetExtension(dialog.FileName), ".jpg", StringComparison.OrdinalIgnoreCase)
            ? new JpegBitmapEncoder { QualityLevel = _settings.JpegQuality }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_latestFrame));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
    }

    private void UpdateTray(string status)
    {
        var value = status.Length > 63 ? status[..63] : status;
        try { _tray.Text = value; } catch { }
    }

    private void HandleNewAgentCompletions(IReadOnlyList<AgentSnapshot> completedAgents)
    {
        if (_disposed || completedAgents.Count == 0)
        {
            return;
        }
        if (_settings.PlayAgentCompletionSound)
        {
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
        }
    }

    private void RefreshCompletionAlertWindow(IReadOnlyList<AgentCompletionAlert>? alerts = null)
    {
        if (_disposed)
        {
            return;
        }
        var pending = alerts ?? _completionTracker.PendingAlerts;
        if (!_settings.ShowAgentCompletionNotification || pending.Count == 0)
        {
            _completionAlertWindow.HideAlerts();
            return;
        }
        _completionAlertWindow.UpdateAlerts(pending);
    }

    private void AcknowledgeCompletionAlert(string identity)
    {
        if (!_completionTracker.Acknowledge(identity))
        {
            return;
        }
        ClearCompletionMarkers(agent =>
            string.Equals(agent.Identity, identity, StringComparison.OrdinalIgnoreCase));
        RefreshCompletionAlertWindow();
        Log.Info($"Acknowledged completed AI alert: {identity}");
    }

    private void ClearCompletionAlerts()
    {
        var cleared = _completionTracker.AcknowledgeAll();
        if (cleared == 0)
        {
            return;
        }

        ClearCompletionMarkers(_ => true);
        RefreshCompletionAlertWindow();
        Log.Info($"Acknowledged {cleared} completed AI alert(s)");
    }

    private void ClearCompletionMarkers(Func<AgentSnapshot, bool> predicate)
    {
        AgentSnapshot Clear(AgentSnapshot agent) => predicate(agent)
            ? agent with { CompletedAt = null }
            : agent;
        _agents = _agents with
        {
            Claude = Clear(_agents.Claude),
            Codex = Clear(_agents.Codex),
            Cursor = Clear(_agents.Cursor),
            CodexSessions = _agents.CodexSessions?.Select(Clear).ToArray(),
            GrokAgent = Clear(_agents.Grok),
        };
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode != PowerModes.Resume)
        {
            return;
        }
        Log.Info("Windows resumed; scheduling USB reconnect");
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000).ConfigureAwait(false);
            _usb.Reconnect();
        });
    }

    private void Exit()
    {
        _shutdown.Cancel();
        _tray.Visible = false;
        _settingsWindow.Hide();
        _completionAlertWindow.ClosePermanently();
        _dashboard.ClosePermanently();
        _application.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _shutdown.Cancel();
        try { _renderLoop?.Wait(TimeSpan.FromSeconds(3)); } catch { }
        try { _marketLoop?.Wait(TimeSpan.FromSeconds(3)); } catch { }
        try { _informationLoop?.Wait(TimeSpan.FromSeconds(3)); } catch { }
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        try { _completionAlertWindow.ClosePermanently(); } catch { }
        _tray.Visible = false;
        _tray.Dispose();
        _applicationIcon.Dispose();
        _usb.Dispose();
        _systemCollector.Dispose();
        _marketCollector.Dispose();
        _newsCollector.Dispose();
        _weatherCollector.Dispose();
        _informationRefresh.Dispose();
        _shutdown.Dispose();
        Log.Info("NJC AI Monitor stopped");
    }
}
