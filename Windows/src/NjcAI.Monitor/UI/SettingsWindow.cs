using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NjcAI.Monitor.Core;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using TextBox = System.Windows.Controls.TextBox;

namespace NjcAI.Monitor.UI;

internal sealed class SettingsWindow : Window
{
    private readonly CheckBox _preview;
    private readonly CheckBox _launch;
    private readonly CheckBox _completionNotification;
    private readonly CheckBox _completionSound;
    private readonly CheckBox _rotate;
    private readonly Slider _quality;
    private readonly TextBlock _qualityValue;
    private readonly TextBox _weatherLocation;
    private readonly TextBox _weatherLatitude;
    private readonly TextBox _weatherLongitude;
    private readonly ComboBox _colorTheme;
    private AppSettings _settings;

    public event Action<AppSettings>? Saved;
    public event Action? DiagnoseRequested;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "NJC AI Monitor 设置";
        Width = 470;
        Height = 750;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(20, 26, 40));
        Foreground = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(Label("NJC AI Monitor", 24, true, Color.FromRgb(92, 171, 255)));
        root.Children.Add(Label("利民 Trofeo Vision 11.3 · 1920×400", 13, false, Color.FromRgb(150, 160, 182)));
        root.Children.Add(Separator(18));

        root.Children.Add(Label("动态五栏布局", 17, true));
        root.Children.Add(Label("AI 优先 · MARKETS · NEWS · WEATHER · AI QUOTA · MEMORY", 13, true,
            Color.FromRgb(40, 213, 234)));
        root.Children.Add(Label("右侧 MEMORY 固定；前四栏按活跃会话自动补位", 12, false,
            Color.FromRgb(150, 160, 182)));
        root.Children.Add(Separator(12));

        root.Children.Add(Label("颜色主题", 15, true));
        _colorTheme = new ComboBox
        {
            Height = 34,
            Margin = new Thickness(0, 7, 0, 8),
            Padding = new Thickness(8, 3, 8, 3),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(34, 43, 61)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(61, 72, 94)),
        };
        foreach (var theme in Enum.GetValues<ColorTheme>())
        {
            _colorTheme.Items.Add(new ComboBoxItem
            {
                Content = theme.DisplayName(),
                Tag = theme,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(34, 43, 61)),
            });
        }
        root.Children.Add(_colorTheme);
        root.Children.Add(Separator(8));

        root.Children.Add(Label("天气位置（无活跃 AI 时显示）", 15, true));
        _weatherLocation = CreateTextInput();
        _weatherLocation.Margin = new Thickness(0, 7, 0, 8);
        root.Children.Add(_weatherLocation);
        var coordinates = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        coordinates.ColumnDefinitions.Add(new ColumnDefinition());
        coordinates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        coordinates.ColumnDefinitions.Add(new ColumnDefinition());
        var latitudeStack = new StackPanel();
        latitudeStack.Children.Add(Label("纬度", 12, false, Color.FromRgb(150, 160, 182)));
        _weatherLatitude = CreateTextInput();
        latitudeStack.Children.Add(_weatherLatitude);
        var longitudeStack = new StackPanel();
        longitudeStack.Children.Add(Label("经度", 12, false, Color.FromRgb(150, 160, 182)));
        _weatherLongitude = CreateTextInput();
        longitudeStack.Children.Add(_weatherLongitude);
        Grid.SetColumn(latitudeStack, 0);
        Grid.SetColumn(longitudeStack, 2);
        coordinates.Children.Add(latitudeStack);
        coordinates.Children.Add(longitudeStack);
        root.Children.Add(coordinates);
        root.Children.Add(Separator(8));

        _preview = Check("LCD 未连接时自动打开预览");
        _launch = Check("登录 Windows 后自动启动");
        _completionNotification = Check("AI 任务完成时显示常驻桌面卡片");
        _completionSound = Check("AI 任务完成时播放提示音");
        _rotate = Check("LCD 画面旋转 180°");
        root.Children.Add(_preview);
        root.Children.Add(_launch);
        root.Children.Add(_completionNotification);
        root.Children.Add(_completionSound);
        root.Children.Add(_rotate);

        root.Children.Add(Label("JPEG 质量", 15, true));
        var qualityRow = new DockPanel { Margin = new Thickness(0, 7, 0, 18) };
        _qualityValue = Label("82", 13, true, Color.FromRgb(40, 213, 234));
        DockPanel.SetDock(_qualityValue, Dock.Right);
        _quality = new Slider { Minimum = 50, Maximum = 95, TickFrequency = 1, IsSnapToTickEnabled = true };
        _quality.ValueChanged += (_, _) => _qualityValue.Text = ((int)_quality.Value).ToString();
        qualityRow.Children.Add(_qualityValue);
        qualityRow.Children.Add(_quality);
        root.Children.Add(qualityRow);

        var buttons = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var diagnose = Button("USB 诊断", false);
        diagnose.Click += (_, _) => DiagnoseRequested?.Invoke();
        var cancel = Button("取消", false);
        cancel.Click += (_, _) => Hide();
        var save = Button("保存", true);
        save.Click += (_, _) => Save();
        buttons.Children.Add(diagnose);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);
        Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        Closing += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Hide();
        };
        LoadValues();
    }

    public void Refresh(AppSettings settings)
    {
        _settings = settings;
        LoadValues();
    }

    private void LoadValues()
    {
        _preview.IsChecked = _settings.ShowPreviewWhenDisconnected;
        _launch.IsChecked = _settings.LaunchAtLogin;
        _completionNotification.IsChecked = _settings.ShowAgentCompletionNotification;
        _completionSound.IsChecked = _settings.PlayAgentCompletionSound;
        _rotate.IsChecked = _settings.RotateDisplay180;
        _colorTheme.SelectedItem = _colorTheme.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is ColorTheme theme && theme == _settings.Theme)
            ?? (ComboBoxItem)_colorTheme.Items[0];
        _quality.Value = _settings.JpegQuality;
        _weatherLocation.Text = _settings.WeatherLocationName;
        _weatherLatitude.Text = _settings.WeatherLatitude.ToString("0.####", CultureInfo.InvariantCulture);
        _weatherLongitude.Text = _settings.WeatherLongitude.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private void Save()
    {
        _settings.ShowPreviewWhenDisconnected = _preview.IsChecked == true;
        _settings.LaunchAtLogin = _launch.IsChecked == true;
        _settings.ShowAgentCompletionNotification = _completionNotification.IsChecked == true;
        _settings.PlayAgentCompletionSound = _completionSound.IsChecked == true;
        _settings.RotateDisplay180 = _rotate.IsChecked == true;
        if (_colorTheme.SelectedItem is ComboBoxItem { Tag: ColorTheme theme })
        {
            _settings.Theme = theme;
        }
        _settings.JpegQuality = (int)_quality.Value;
        _settings.WeatherLocationName = string.IsNullOrWhiteSpace(_weatherLocation.Text)
            ? "Sydney"
            : _weatherLocation.Text.Trim();
        if (double.TryParse(_weatherLatitude.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) &&
            latitude is >= -90 and <= 90)
        {
            _settings.WeatherLatitude = latitude;
        }
        if (double.TryParse(_weatherLongitude.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) &&
            longitude is >= -180 and <= 180)
        {
            _settings.WeatherLongitude = longitude;
        }
        _settings.Save();
        Saved?.Invoke(_settings);
        Hide();
    }

    private static CheckBox Check(string text) => new()
    {
        Content = text,
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Microsoft YaHei UI"),
        FontSize = 14,
        Margin = new Thickness(0, 7, 0, 7),
    };

    private static TextBox CreateTextInput() => new()
    {
        Height = 31,
        Padding = new Thickness(8, 4, 8, 4),
        FontFamily = new FontFamily("Microsoft YaHei UI"),
        FontSize = 13,
        Foreground = Brushes.White,
        Background = new SolidColorBrush(Color.FromRgb(34, 43, 61)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(61, 72, 94)),
        BorderThickness = new Thickness(1),
    };

    private static TextBlock Label(string text, double size, bool bold = false, Color? color = null) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Microsoft YaHei UI"),
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = new SolidColorBrush(color ?? Colors.White),
        Margin = new Thickness(0, 2, 0, 2),
    };

    private static Border Separator(double margin) => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Color.FromRgb(48, 57, 77)),
        Margin = new Thickness(0, margin, 0, margin),
    };

    private static Button Button(string text, bool primary) => new()
    {
        Content = text,
        MinWidth = 86,
        Height = 34,
        Margin = new Thickness(8, 12, 0, 0),
        FontFamily = new FontFamily("Microsoft YaHei UI"),
        FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
        Background = new SolidColorBrush(primary ? Color.FromRgb(46, 137, 225) : Color.FromRgb(48, 57, 77)),
        Foreground = Brushes.White,
        BorderThickness = new Thickness(0),
    };
}
