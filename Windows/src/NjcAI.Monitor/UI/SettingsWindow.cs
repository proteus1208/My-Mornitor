using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using NjcAI.Monitor.Core;
using NjcAI.Monitor.Rendering;
using Binding = System.Windows.Data.Binding;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using MediaColor = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;
using PlacementMode = System.Windows.Controls.Primitives.PlacementMode;
using Popup = System.Windows.Controls.Primitives.Popup;
using TextBox = System.Windows.Controls.TextBox;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using WpfControl = System.Windows.Controls.Control;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

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
    private readonly Grid _shell;
    private readonly Border _header;
    private readonly Border _footer;
    private readonly Border _primarySwatch;
    private readonly Border _middleSwatch;
    private readonly Border _secondarySwatch;
    private readonly TextBlock _themePreviewName;
    private readonly Button _diagnoseButton;
    private readonly Button _cancelButton;
    private readonly Button _saveButton;
    private readonly List<Border> _cards = [];
    private readonly List<Border> _inputFrames = [];
    private readonly List<TextBlock> _accentLabels = [];
    private AppSettings _settings;

    public event Action<AppSettings>? Saved;
    public event Action? DiagnoseRequested;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "NJC AI Monitor Settings";
        Width = 590;
        Height = 820;
        MinWidth = 590;
        MinHeight = 700;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        Foreground = Brushes.White;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _shell = new Grid();
        _shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _shell.RowDefinitions.Add(new RowDefinition());
        _shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _header = BuildHeader();
        Grid.SetRow(_header, 0);
        _shell.Children.Add(_header);

        var content = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };

        _colorTheme = new ComboBox
        {
            Height = 38,
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (var theme in Enum.GetValues<ColorTheme>())
        {
            _colorTheme.Items.Add(new ComboBoxItem
            {
                Content = theme.DisplayName(),
                Tag = theme,
                Padding = new Thickness(8, 5, 8, 5),
            });
        }

        _themePreviewName = MutedText("Preview only — Save to apply", 11);
        _primarySwatch = Swatch();
        _middleSwatch = Swatch();
        _secondarySwatch = Swatch();
        var palettePreview = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        palettePreview.ColumnDefinitions.Add(new ColumnDefinition());
        palettePreview.ColumnDefinitions.Add(new ColumnDefinition());
        palettePreview.ColumnDefinitions.Add(new ColumnDefinition());
        AddToGrid(palettePreview, _primarySwatch, 0);
        AddToGrid(palettePreview, _middleSwatch, 1);
        AddToGrid(palettePreview, _secondarySwatch, 2);

        var interfaceBody = new StackPanel();
        interfaceBody.Children.Add(FieldLabel("Color theme"));
        interfaceBody.Children.Add(InputFrame(_colorTheme));
        interfaceBody.Children.Add(palettePreview);
        interfaceBody.Children.Add(_themePreviewName);
        interfaceBody.Children.Add(InfoStrip(
            "DYNAMIC DECK",
            "AI sessions take priority. Markets, News, Forecast and AI Quota fill open columns; Memory stays fixed on the right."));
        content.Children.Add(Card("Interface", "Theme and five-column display behavior", interfaceBody));

        _weatherLocation = CreateTextInput();
        _weatherLatitude = CreateTextInput();
        _weatherLongitude = CreateTextInput();
        var coordinates = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        coordinates.ColumnDefinitions.Add(new ColumnDefinition());
        coordinates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        coordinates.ColumnDefinitions.Add(new ColumnDefinition());
        var latitudeField = LabeledField("Latitude", _weatherLatitude);
        var longitudeField = LabeledField("Longitude", _weatherLongitude);
        Grid.SetColumn(latitudeField, 0);
        Grid.SetColumn(longitudeField, 2);
        coordinates.Children.Add(latitudeField);
        coordinates.Children.Add(longitudeField);
        var weatherBody = new StackPanel();
        weatherBody.Children.Add(LabeledField("Location", _weatherLocation));
        weatherBody.Children.Add(coordinates);
        content.Children.Add(Card("Weather Source", "Used by Forecast and the fixed system panel", weatherBody));

        _preview = Check(
            "Automatic preview",
            "Open the desktop preview whenever the LCD is disconnected.");
        _launch = Check(
            "Launch at sign-in",
            "Start NJC AI Monitor automatically after signing in to Windows.");
        _completionNotification = Check(
            "Persistent completion card",
            "Keep a desktop alert visible until an AI task is confirmed.");
        _completionSound = Check(
            "Completion sound",
            "Play a notification sound when an AI task finishes.");
        var behaviorBody = new StackPanel();
        behaviorBody.Children.Add(_preview);
        behaviorBody.Children.Add(_launch);
        behaviorBody.Children.Add(_completionNotification);
        behaviorBody.Children.Add(_completionSound);
        content.Children.Add(Card("Behavior", "Startup, preview and completion alerts", behaviorBody));

        _rotate = Check(
            "Rotate output 180°",
            "Use this when the physical LCD is installed upside down.");
        _qualityValue = new TextBlock
        {
            Text = "82",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _accentLabels.Add(_qualityValue);
        _quality = new Slider
        {
            Minimum = 50,
            Maximum = 95,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 8, 0, 8),
        };
        _quality.ValueChanged += (_, _) => _qualityValue.Text = $"{(int)_quality.Value}%";
        var qualityHeader = new Grid();
        qualityHeader.ColumnDefinitions.Add(new ColumnDefinition());
        qualityHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        qualityHeader.Children.Add(FieldLabel("JPEG quality"));
        Grid.SetColumn(_qualityValue, 1);
        qualityHeader.Children.Add(_qualityValue);
        var outputBody = new StackPanel();
        outputBody.Children.Add(qualityHeader);
        outputBody.Children.Add(_quality);
        outputBody.Children.Add(_rotate);
        content.Children.Add(Card("Image Output", "USB frame quality and physical orientation", outputBody));

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroll, 1);
        _shell.Children.Add(scroll);

        _diagnoseButton = ActionButton("USB Diagnostics", false);
        _diagnoseButton.Click += (_, _) => DiagnoseRequested?.Invoke();
        _cancelButton = ActionButton("Cancel", false);
        _cancelButton.Click += (_, _) => Hide();
        _saveButton = ActionButton("Save Settings", true);
        _saveButton.Click += (_, _) => Save();
        _footer = BuildFooter();
        Grid.SetRow(_footer, 2);
        _shell.Children.Add(_footer);

        Content = _shell;
        _colorTheme.SelectionChanged += (_, _) =>
        {
            if (_colorTheme.SelectedItem is ComboBoxItem { Tag: ColorTheme theme })
            {
                ApplyThemePreview(theme);
            }
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

    private Border BuildHeader()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel();
        var eyebrow = new TextBlock
        {
            Text = "NJC // DISPLAY CONTROL",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
        };
        _accentLabels.Add(eyebrow);
        copy.Children.Add(eyebrow);
        copy.Children.Add(new TextBlock
        {
            Text = "Monitor Settings",
            FontSize = 27,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 5, 0, 2),
        });
        copy.Children.Add(MutedText("Thermalright Trofeo Vision 11.3  ·  1920 × 400", 12));
        grid.Children.Add(copy);

        var badge = new Border
        {
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 7, 12, 7),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "LCD READY",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
            },
        };
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);

        return new Border
        {
            Margin = new Thickness(20, 20, 20, 0),
            Padding = new Thickness(22, 18, 22, 18),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = grid,
        };
    }

    private Border BuildFooter()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_diagnoseButton);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_cancelButton);
        actions.Children.Add(_saveButton);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return new Border
        {
            Padding = new Thickness(20, 14, 20, 18),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid,
        };
    }

    private Border Card(string title, string subtitle, UIElement body)
    {
        var stack = new StackPanel();
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
        };
        _accentLabels.Add(heading);
        stack.Children.Add(heading);
        stack.Children.Add(MutedText(subtitle, 11));
        stack.Children.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Child = body,
        });
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 12),
            Child = stack,
        };
        _cards.Add(card);
        return card;
    }

    private Border InputFrame(WpfControl control)
    {
        control.Background = Brushes.Transparent;
        control.BorderThickness = new Thickness(0);
        control.Foreground = Brushes.White;
        var frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            Child = control,
        };
        _inputFrames.Add(frame);
        return frame;
    }

    private StackPanel LabeledField(string label, TextBox input)
    {
        var stack = new StackPanel();
        stack.Children.Add(FieldLabel(label));
        stack.Children.Add(InputFrame(input));
        return stack;
    }

    private static TextBox CreateTextInput() => new()
    {
        Height = 34,
        Padding = new Thickness(9, 5, 9, 5),
        FontSize = 13,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private static CheckBox Check(string title, string description)
    {
        var copy = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        copy.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        copy.Children.Add(MutedText(description, 10));
        return new CheckBox
        {
            Content = copy,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 5, 0, 5),
            Padding = new Thickness(0, 2, 0, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
    }

    private static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(MediaColor.FromRgb(160, 171, 185)),
        Margin = new Thickness(0, 0, 0, 6),
    };

    private static TextBlock MutedText(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = new SolidColorBrush(MediaColor.FromRgb(145, 156, 171)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 0),
    };

    private static Border Swatch() => new()
    {
        Height = 5,
        CornerRadius = new CornerRadius(3),
        Margin = new Thickness(2, 0, 2, 0),
    };

    private static Border InfoStrip(string label, string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var tag = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(MediaColor.FromArgb(28, 255, 255, 255)),
            Padding = new Thickness(8, 5, 8, 5),
            Child = new TextBlock
            {
                Text = label,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
            },
        };
        grid.Children.Add(tag);
        var description = MutedText(text, 10);
        description.Margin = new Thickness(12, 2, 0, 0);
        Grid.SetColumn(description, 1);
        grid.Children.Add(description);
        return new Border
        {
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Child = grid,
        };
    }

    private static Button ActionButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = primary ? 112 : 96,
            Height = 38,
            Margin = new Thickness(primary ? 10 : 0, 0, 0, 0),
            Padding = new Thickness(15, 7, 15, 7),
            FontSize = 12,
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
        };
        button.Template = RoundedButtonTemplate();
        return button;
    }

    private static ControlTemplate RoundedButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(BorderBrush))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(BorderThickness))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, WpfHorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(Content))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        presenter.SetBinding(TextElement.ForegroundProperty, new Binding(nameof(Foreground))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(OpacityProperty, 0.86));
        template.Triggers.Add(hover);
        return template;
    }

    private void ApplyThemePreview(ColorTheme theme)
    {
        var palette = ThemePalette.For(theme);
        _shell.Background = Gradient(palette.Background, palette.BackgroundGradientEnd);
        _header.Background = Gradient(palette.Panel, palette.PanelGradientEnd);
        _header.BorderBrush = Brush(palette.PanelBorder);
        _footer.Background = Brush(palette.PanelGradientEnd);
        _footer.BorderBrush = Brush(palette.PanelBorder);
        foreach (var card in _cards)
        {
            card.Background = Gradient(palette.Panel, palette.PanelGradientEnd);
            card.BorderBrush = Brush(palette.PanelBorder);
        }
        foreach (var frame in _inputFrames)
        {
            frame.Background = Brush(palette.Track);
            frame.BorderBrush = Brush(palette.InnerSeparator);
        }
        foreach (var label in _accentLabels)
        {
            label.Foreground = Brush(palette.AccentPrimary);
        }
        _colorTheme.Template = ComboBoxTemplate(palette);
        var itemStyle = ComboBoxItemStyle(palette);
        foreach (var item in _colorTheme.Items.OfType<ComboBoxItem>())
        {
            item.Style = itemStyle;
        }
        _colorTheme.Foreground = Brush(palette.PrimaryText);
        _primarySwatch.Background = Brush(palette.AccentPrimary);
        _middleSwatch.Background = Brush(palette.AccentStrong);
        _secondarySwatch.Background = Brush(palette.AccentSecondary);
        _themePreviewName.Text = $"{theme.DisplayName()} palette preview — Save to apply";
        _quality.Foreground = Brush(palette.AccentPrimary);
        _saveButton.Background = Brush(palette.AccentPrimary);
        _saveButton.BorderBrush = Brush(palette.AccentHighlight);
        _saveButton.Foreground = ContrastBrush(palette.AccentPrimary);
        StyleSecondaryButton(_diagnoseButton, palette);
        StyleSecondaryButton(_cancelButton, palette);
    }

    private static ControlTemplate ComboBoxTemplate(ThemePalette palette)
    {
        var root = new FrameworkElementFactory(typeof(Grid));

        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(ToggleButton.FocusableProperty, false);
        toggle.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(ComboBox.IsDropDownOpen))
        {
            RelativeSource = RelativeSource.TemplatedParent,
            Mode = BindingMode.TwoWay,
        });
        var toggleBorder = new FrameworkElementFactory(typeof(Border));
        toggleBorder.SetValue(Border.BackgroundProperty, Brush(palette.Track));
        toggleBorder.SetValue(Border.BorderBrushProperty, Brush(palette.InnerSeparator));
        toggleBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        toggleBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        var toggleTemplate = new ControlTemplate(typeof(ToggleButton)) { VisualTree = toggleBorder };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(palette.AccentPrimary), "ToggleBorder"));
        toggleTemplate.Triggers.Add(hover);
        toggleBorder.Name = "ToggleBorder";
        toggle.SetValue(ToggleButton.TemplateProperty, toggleTemplate);
        root.AppendChild(toggle);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.IsHitTestVisibleProperty, false);
        content.SetValue(ContentPresenter.MarginProperty, new Thickness(12, 0, 42, 0));
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, WpfHorizontalAlignment.Left);
        content.SetValue(TextElement.ForegroundProperty, Brush(palette.PrimaryText));
        content.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(ComboBox.SelectionBoxItem))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        content.SetBinding(ContentPresenter.ContentTemplateProperty,
            new Binding(nameof(ComboBox.SelectionBoxItemTemplate))
            {
                RelativeSource = RelativeSource.TemplatedParent,
            });
        root.AppendChild(content);

        var arrow = new FrameworkElementFactory(typeof(TextBlock));
        arrow.SetValue(TextBlock.TextProperty, "▼");
        arrow.SetValue(TextBlock.FontSizeProperty, 9d);
        arrow.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        arrow.SetValue(TextBlock.ForegroundProperty, Brush(palette.AccentPrimary));
        arrow.SetValue(TextBlock.IsHitTestVisibleProperty, false);
        arrow.SetValue(TextBlock.HorizontalAlignmentProperty, WpfHorizontalAlignment.Right);
        arrow.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 14, 0));
        root.AppendChild(arrow);

        var popup = new FrameworkElementFactory(typeof(Popup));
        popup.Name = "PART_Popup";
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.FocusableProperty, false);
        popup.SetBinding(Popup.IsOpenProperty, new Binding(nameof(ComboBox.IsDropDownOpen))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        var popupBorder = new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(Border.BackgroundProperty, Brush(palette.Panel));
        popupBorder.SetValue(Border.BorderBrushProperty, Brush(palette.ModuleSeparator));
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        popupBorder.SetValue(Border.PaddingProperty, new Thickness(5));
        popupBorder.SetValue(Border.MarginProperty, new Thickness(0, 4, 0, 0));
        popupBorder.SetValue(Border.MinWidthProperty, 300d);
        popupBorder.SetBinding(Border.WidthProperty, new Binding(nameof(ActualWidth))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.MaxHeightProperty, 280d);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var items = new FrameworkElementFactory(typeof(ItemsPresenter));
        scroll.AppendChild(items);
        popupBorder.AppendChild(scroll);
        popup.AppendChild(popupBorder);
        root.AppendChild(popup);

        var template = new ControlTemplate(typeof(ComboBox)) { VisualTree = root };
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, 0.45));
        template.Triggers.Add(disabled);
        return template;
    }

    private static Style ComboBoxItemStyle(ThemePalette palette)
    {
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(ForegroundProperty, Brush(palette.PrimaryText)));
        style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(12, 9, 12, 9)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(1, 1, 1, 1)));
        style.Setters.Add(new Setter(CursorProperty, Cursors.Hand));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "ItemBorder";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetBinding(Border.PaddingProperty, new Binding(nameof(Padding))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.Name = "ItemContent";
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(Content))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        presenter.SetBinding(TextElement.ForegroundProperty, new Binding(nameof(Foreground))
        {
            RelativeSource = RelativeSource.TemplatedParent,
        });
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = border };

        var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Border.BackgroundProperty,
            Brush(palette.AccentStrong), "ItemBorder"));
        highlighted.Setters.Add(new Setter(TextElement.ForegroundProperty,
            ContrastBrush(palette.AccentStrong), "ItemContent"));
        template.Triggers.Add(highlighted);
        var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty,
            Brush(palette.AccentPrimary), "ItemBorder"));
        selected.Setters.Add(new Setter(TextElement.ForegroundProperty,
            ContrastBrush(palette.AccentPrimary), "ItemContent"));
        template.Triggers.Add(selected);
        style.Setters.Add(new Setter(TemplateProperty, template));
        return style;
    }

    private static void StyleSecondaryButton(Button button, ThemePalette palette)
    {
        button.Background = Brush(palette.Track);
        button.BorderBrush = Brush(palette.InnerSeparator);
        button.Foreground = Brush(palette.PrimaryText);
    }

    private static SolidColorBrush Brush(System.Drawing.Color color) =>
        new(MediaColor.FromRgb(color.R, color.G, color.B));

    private static LinearGradientBrush Gradient(System.Drawing.Color start, System.Drawing.Color end) =>
        new(MediaColor.FromRgb(start.R, start.G, start.B),
            MediaColor.FromRgb(end.R, end.G, end.B), 90);

    private static SolidColorBrush ContrastBrush(System.Drawing.Color color)
    {
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        return luminance > 0.58 ? Brushes.Black : Brushes.White;
    }

    private static void AddToGrid(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
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
        ApplyThemePreview(_colorTheme.SelectedItem is ComboBoxItem { Tag: ColorTheme theme }
            ? theme
            : ColorTheme.BitcoinOrange);
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
}
