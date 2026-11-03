using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using NjcAI.Monitor.Core;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace NjcAI.Monitor.UI;

internal sealed class CompletionAlertWindow : Window
{
    private readonly TextBlock _heading;
    private readonly TextBlock _subtitle;
    private readonly Button _acknowledgeAll;
    private readonly StackPanel _items;
    private bool _allowClose;
    private string _fingerprint = string.Empty;

    public event Action<string>? Acknowledged;
    public event Action? AcknowledgeAllRequested;

    public CompletionAlertWindow()
    {
        Title = "NJC AI Monitor — AI 任务完成";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 700;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var root = new Border
        {
            Background = PanelGradient(),
            BorderBrush = Brush(166, 82, 24),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(247, 147, 26),
                BlurRadius = 28,
                ShadowDepth = 4,
                Opacity = 0.26,
            },
        };
        var layout = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        _heading = new TextBlock
        {
            Text = "AI 任务已经完成",
            Foreground = Brush(255, 92, 53),
            FontFamily = Font(),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
        };
        heading.Children.Add(_heading);
        _subtitle = new TextBlock
        {
            Foreground = Brush(171, 145, 127),
            FontFamily = Font(),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
        };
        heading.Children.Add(_subtitle);
        Grid.SetColumn(heading, 0);
        header.Children.Add(heading);

        _acknowledgeAll = ActionButton("全部确认", false);
        _acknowledgeAll.Margin = new Thickness(12, 0, 0, 0);
        _acknowledgeAll.Click += (_, _) => AcknowledgeAllRequested?.Invoke();
        Grid.SetColumn(_acknowledgeAll, 1);
        header.Children.Add(_acknowledgeAll);
        layout.Children.Add(header);

        _items = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        layout.Children.Add(new ScrollViewer
        {
            Content = _items,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 500,
        });
        root.Child = layout;
        Content = root;

        Closing += (_, eventArgs) =>
        {
            if (_allowClose)
            {
                return;
            }
            eventArgs.Cancel = true;
            Hide();
        };
    }

    public void UpdateAlerts(IReadOnlyList<AgentCompletionAlert> alerts)
    {
        var ordered = alerts
            .GroupBy(alert => alert.Identity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(alert => alert.CompletedAt).First())
            .OrderByDescending(alert => alert.CompletedAt)
            .ToArray();
        if (ordered.Length == 0)
        {
            _fingerprint = string.Empty;
            Hide();
            return;
        }

        var fingerprint = string.Join('|', ordered.Select(alert =>
            $"{alert.Identity}:{alert.CompletedAt.Ticks}"));
        if (!string.Equals(_fingerprint, fingerprint, StringComparison.Ordinal))
        {
            _fingerprint = fingerprint;
            var kinds = ordered.Select(alert => alert.Kind).Distinct().ToArray();
            var subject = kinds.Length == 1 ? AgentName(kinds[0]) : "多个 AI";
            _heading.Text = $"{subject} 任务已经完成";
            Title = $"NJC AI Monitor — {_heading.Text}";
            _subtitle.Text = ordered.Length == 1
                ? "1 个任务等待确认"
                : $"{ordered.Length} 个任务等待确认";
            _acknowledgeAll.Visibility = ordered.Length > 1
                ? Visibility.Visible
                : Visibility.Collapsed;
            _items.Children.Clear();
            foreach (var alert in ordered)
            {
                _items.Children.Add(CreateAlertCard(alert));
            }
        }

        if (!IsVisible)
        {
            Show();
        }
        _ = Dispatcher.BeginInvoke(PositionAtBottomRight);
    }

    public void HideAlerts()
    {
        _fingerprint = string.Empty;
        Hide();
    }

    public void ClosePermanently()
    {
        if (_allowClose)
        {
            return;
        }
        _allowClose = true;
        Close();
    }

    private Border CreateAlertCard(AgentCompletionAlert alert)
    {
        var card = new Border
        {
            Background = Brush(42, 21, 10),
            BorderBrush = Brush(138, 70, 24),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { MaxWidth = 285 };
        text.Children.Add(new TextBlock
        {
            Text = $"{AgentName(alert.Kind)} · {alert.Project}",
            Foreground = Brush(246, 241, 236),
            FontFamily = Font(),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = alert.Activity,
            Foreground = Brush(216, 196, 181),
            FontFamily = Font(),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 42,
            Margin = new Thickness(0, 6, 0, 0),
        });
        text.Children.Add(new TextBlock
        {
            Text = $"完成于 {alert.CompletedAt:MM-dd HH:mm:ss}",
            Foreground = Brush(171, 145, 127),
            FontFamily = Font(),
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
        });
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var acknowledge = ActionButton("确认", true);
        acknowledge.Margin = new Thickness(12, 0, 0, 0);
        var identity = alert.Identity;
        acknowledge.Click += (_, _) => Acknowledged?.Invoke(identity);
        Grid.SetColumn(acknowledge, 1);
        grid.Children.Add(acknowledge);
        card.Child = grid;
        return card;
    }

    private void PositionAtBottomRight()
    {
        UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left + 12, workArea.Right - ActualWidth - 18);
        Top = Math.Max(workArea.Top + 12, workArea.Bottom - ActualHeight - 18);
    }

    private static Button ActionButton(string text, bool primary) => new()
    {
        Content = text,
        MinWidth = 82,
        Height = 32,
        Padding = new Thickness(10, 0, 10, 0),
        FontFamily = Font(),
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush(246, 241, 236),
        Background = primary ? Brush(217, 106, 0) : Brush(74, 42, 28),
        BorderBrush = primary ? Brush(217, 106, 0) : Brush(138, 70, 24),
        BorderThickness = new Thickness(1),
    };

    private static string AgentName(AgentKind kind) => kind switch
    {
        AgentKind.Claude => "Claude Code",
        AgentKind.Cursor => "Cursor",
        _ => "Codex",
    };

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Color.FromRgb(red, green, blue));

    private static LinearGradientBrush PanelGradient() => new(
        Color.FromRgb(50, 25, 12),
        Color.FromRgb(36, 17, 7),
        new System.Windows.Point(0, 0),
        new System.Windows.Point(1, 1));

    private static FontFamily Font() => new("Microsoft YaHei UI");
}
