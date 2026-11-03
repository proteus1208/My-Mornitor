using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using NjcAI.Monitor.Core;
using NjcAI.Monitor.Rendering;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using DrawingColor = System.Drawing.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace NjcAI.Monitor.UI;

internal sealed class CompletionAlertWindow : Window
{
    private readonly TextBlock _heading;
    private readonly TextBlock _subtitle;
    private readonly Button _acknowledgeAll;
    private readonly StackPanel _items;
    private readonly Border _root;
    private ThemePalette _theme;
    private bool _allowClose;
    private string _fingerprint = string.Empty;

    public event Action<string>? Acknowledged;
    public event Action? AcknowledgeAllRequested;

    public CompletionAlertWindow(ColorTheme theme = ColorTheme.BitcoinOrange)
    {
        _theme = ThemePalette.For(theme);
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

        _root = new Border
        {
            Background = PanelGradient(),
            BorderBrush = Brush(_theme.NewsSeparator),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Effect = new DropShadowEffect
            {
                Color = MediaColor(_theme.AccentPrimary),
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
            Foreground = Brush(_theme.AccentStrong),
            FontFamily = Font(),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
        };
        heading.Children.Add(_heading);
        _subtitle = new TextBlock
        {
            Foreground = Brush(_theme.SecondaryText),
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
        _root.Child = layout;
        Content = _root;

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

    public void ApplyTheme(ColorTheme theme)
    {
        _theme = ThemePalette.For(theme);
        _root.Background = PanelGradient();
        _root.BorderBrush = Brush(_theme.NewsSeparator);
        _root.Effect = new DropShadowEffect
        {
            Color = MediaColor(_theme.AccentPrimary),
            BlurRadius = 28,
            ShadowDepth = 4,
            Opacity = 0.26,
        };
        _heading.Foreground = Brush(_theme.AccentStrong);
        _subtitle.Foreground = Brush(_theme.SecondaryText);
        StyleActionButton(_acknowledgeAll, false);
        _fingerprint = string.Empty;
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
            Background = Brush(_theme.AlertCard),
            BorderBrush = Brush(_theme.ModuleSeparator),
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
            Foreground = Brush(_theme.PrimaryText),
            FontFamily = Font(),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = alert.Activity,
            Foreground = Brush(_theme.SecondaryTextBright),
            FontFamily = Font(),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 42,
            Margin = new Thickness(0, 6, 0, 0),
        });
        text.Children.Add(new TextBlock
        {
            Text = $"完成于 {alert.CompletedAt:MM-dd HH:mm:ss}",
            Foreground = Brush(_theme.SecondaryText),
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

    private Button ActionButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 82,
            Height = 32,
            Padding = new Thickness(10, 0, 10, 0),
            FontFamily = Font(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(1),
        };
        StyleActionButton(button, primary);
        return button;
    }

    private void StyleActionButton(Button button, bool primary)
    {
        button.Foreground = Brush(_theme.PrimaryText);
        button.Background = Brush(primary ? _theme.Quota : _theme.AlertSecondaryButton);
        button.BorderBrush = Brush(primary ? _theme.Quota : _theme.ModuleSeparator);
    }

    private static string AgentName(AgentKind kind) => kind switch
    {
        AgentKind.Claude => "Claude Code",
        AgentKind.Cursor => "Cursor",
        _ => "Codex",
    };

    private static SolidColorBrush Brush(DrawingColor color) =>
        new(MediaColor(color));

    private LinearGradientBrush PanelGradient() => new(
        MediaColor(_theme.AlertBackgroundStart),
        MediaColor(_theme.AlertBackgroundEnd),
        new System.Windows.Point(0, 0),
        new System.Windows.Point(1, 1));

    private static Color MediaColor(DrawingColor color) => Color.FromRgb(color.R, color.G, color.B);

    private static FontFamily Font() => new("Microsoft YaHei UI");
}
