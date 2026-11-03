using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using NjcAI.Monitor.Core;
using NjcAI.Monitor.Services;

namespace NjcAI.Monitor.Rendering;

internal sealed class DashboardRenderer
{
    public const int Width = 1920;
    public const int Height = 400;

    private static ThemePalette Palette => ThemePaletteContext.Current;
    private static Color Background => Palette.Background;
    private static Color Panel => Palette.Panel;
    private static Color PanelBorder => Palette.PanelBorder;
    private static Color ModuleSeparator => Palette.ModuleSeparator;
    private static Color InnerSeparator => Palette.InnerSeparator;
    private static Color NewsSeparator => Palette.NewsSeparator;
    private static Color Track => Palette.Track;
    private static Color PrimaryText => Palette.PrimaryText;
    private static Color SecondaryText => Palette.SecondaryText;
    private static Color Web3Lime => Palette.AccentHighlight;
    private static Color Web3Green => Palette.AccentPrimary;
    private static Color Web3Teal => Palette.AccentStrong;
    private static Color Blue => Web3Teal;
    private static Color Cyan => Web3Teal;
    private static Color Green => Web3Green;
    private static Color Yellow => Web3Lime;
    private static Color Red => Palette.Error;
    private static Color MarketUp => Color.FromArgb(247, 147, 26);
    private static Color MarketUpHighlight => Color.FromArgb(247, 147, 26);
    private static Color MarketDown => Color.FromArgb(0, 229, 153);
    private static Color QuotaOrange => Palette.Quota;
    private static Color Purple => Web3Lime;
    private static Color Orange => Web3Teal;
    private readonly DynamicPanelResolver _panelResolver = new();

    public Bitmap Render(DashboardSnapshot snapshot, AppSettings settings, string deviceStatus)
    {
        using var themeScope = ThemePaletteContext.Use(settings.Theme);
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        bitmap.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Background);
        var renderTime = DateTime.Now;
        DrawHolographicBackground(graphics, renderTime);
        DrawUnifiedDeck(graphics);

        const int panelWidth = Width / 5;
        var panelRects = Enumerable.Range(0, 5)
            .Select(index => new Rectangle(index * panelWidth, 58, panelWidth, 334))
            .ToArray();
        var dynamicPanels = _panelResolver.Resolve(snapshot.Agents, renderTime);
        for (var index = 0; index < Math.Min(4, dynamicPanels.Count); index++)
        {
            var completionActive = dynamicPanels[index].Agent is { } agent &&
                                   IsCompletionActive(agent, renderTime);
            var flashOn = completionActive && IsCompletionFlashOn(renderTime);
            var accent = completionActive
                ? flashOn ? Palette.CompletionBright : Palette.CompletionDim
                : PanelAccent(dynamicPanels[index]);
            DrawPanel(graphics, panelRects[index], accent, completionActive, flashOn, index + 1);
            DrawDynamicPanel(graphics, dynamicPanels[index], panelRects[index], snapshot);
        }
        DrawPanel(graphics, panelRects[4], Cyan, panelIndex: 5);
        DrawToday(graphics, snapshot.System, snapshot.Weather ?? WeatherSnapshot.Empty(), panelRects[4]);
        return bitmap;
    }

    private static void DrawDynamicPanel(
        Graphics graphics,
        DashboardPanel panel,
        Rectangle rect,
        DashboardSnapshot snapshot)
    {
        switch (panel.Kind)
        {
            case DashboardPanelKind.Agent when panel.Agent is not null:
                DrawAgentSession(graphics, panel.Agent, rect);
                break;
            case DashboardPanelKind.Markets:
                DrawMarkets(graphics, snapshot.Markets ?? MarketSnapshot.Empty, rect);
                break;
            case DashboardPanelKind.News:
                DrawNews(graphics, snapshot.News ?? NewsSnapshot.Empty, rect);
                break;
            case DashboardPanelKind.Forecast:
                DrawForecast(graphics, snapshot.Weather ?? WeatherSnapshot.Empty(), rect);
                break;
            case DashboardPanelKind.AiQuota:
                DrawAiQuota(graphics, snapshot.Agents, rect);
                break;
        }
    }

    private static Color PanelAccent(DashboardPanel _) => Web3Teal;

    public static byte[] EncodeJpeg(Bitmap bitmap, int initialQuality, bool rotate180)
    {
        using var finalBitmap = rotate180 ? RotateCopy(bitmap) : null;
        var image = finalBitmap ?? bitmap;
        var codec = ImageCodecInfo.GetImageEncoders().First(item => item.MimeType == "image/jpeg");
        for (var quality = Math.Clamp(initialQuality, 35, 95); quality >= 30; quality -= 5)
        {
            using var stream = new MemoryStream();
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
            image.Save(stream, codec, parameters);
            if (stream.Length <= 650_000 || quality == 30)
            {
                return stream.ToArray();
            }
        }
        return [];
    }

    private static Bitmap RotateCopy(Bitmap source)
    {
        var copy = (Bitmap)source.Clone();
        copy.RotateFlip(RotateFlipType.Rotate180FlipNone);
        return copy;
    }

    private static void DrawHolographicBackground(Graphics graphics, DateTime now)
    {
        using (var gradient = new LinearGradientBrush(
                   new Rectangle(0, 0, Width, Height),
                   Background, Palette.BackgroundGradientEnd, 8f))
        {
            graphics.FillRectangle(gradient, 0, 0, Width, Height);
        }

        using var gridPen = new Pen(Color.FromArgb(14, Web3Teal), 1);
        const int grid = 48;
        for (var x = -Height; x < Width; x += grid)
        {
            graphics.DrawLine(gridPen, x, Height, x + Height, 0);
        }
        for (var x = 0; x < Width + Height; x += grid)
        {
            graphics.DrawLine(gridPen, x, 0, x - Height, Height);
        }
        for (var y = 32; y < Height; y += 64)
        {
            graphics.DrawLine(gridPen, 0, y, Width, y);
        }

        using var linkPen = new Pen(Color.FromArgb(34, Web3Green), 1.5f);
        using var nodeBrush = new SolidBrush(Color.FromArgb(105, Web3Teal));
        Point[] nodes =
        [
            new(48, 72), new(236, 328), new(458, 88), new(742, 337),
            new(965, 62), new(1214, 318), new(1452, 83), new(1812, 307),
        ];
        for (var index = 0; index < nodes.Length - 1; index++)
        {
            graphics.DrawLine(linkPen, nodes[index], nodes[index + 1]);
            graphics.FillEllipse(nodeBrush, nodes[index].X - 3, nodes[index].Y - 3, 6, 6);
        }
        graphics.FillEllipse(nodeBrush, nodes[^1].X - 3, nodes[^1].Y - 3, 6, 6);

        var scanX = (int)(now.TimeOfDay.TotalMilliseconds / 24 % Width);
        using var scanGlow = new LinearGradientBrush(
            new Rectangle(Math.Max(0, scanX - 32), 0, 64, Height),
            Color.FromArgb(0, Web3Green), Color.FromArgb(22, Web3Green), 0f);
        graphics.FillRectangle(scanGlow, Math.Max(0, scanX - 32), 0, 64, Height);
    }

    private static void DrawUnifiedDeck(Graphics graphics)
    {
        Point[] shell =
        [
            new(8, 24), new(24, 8), new(Width - 24, 8), new(Width - 8, 24),
            new(Width - 8, Height - 24), new(Width - 24, Height - 8),
            new(24, Height - 8), new(8, Height - 24),
        ];
        using var shellPath = new GraphicsPath();
        shellPath.AddPolygon(shell);
        using (var shellFill = new LinearGradientBrush(
                   new Rectangle(8, 8, Width - 16, Height - 16),
                   Panel, Palette.PanelGradientEnd, 90f))
        {
            graphics.FillPath(shellFill, shellPath);
        }
        using (var warmGlass = new LinearGradientBrush(
                   new Rectangle(8, 8, Width - 16, Height - 16),
                   Color.FromArgb(20, Web3Green), Color.FromArgb(0, Web3Teal), 0f))
        {
            graphics.FillPath(warmGlass, shellPath);
        }
        using (var outline = new Pen(Color.FromArgb(165, PanelBorder), 1.2f))
        {
            graphics.DrawPath(outline, shellPath);
        }

        using (var topGradient = new LinearGradientBrush(
                   new Rectangle(32, 8, Width - 312, 3), Web3Lime, Web3Teal, 0f))
        using (var topPen = new Pen(topGradient, 3))
        {
            graphics.DrawLine(topPen, 32, 9, Width - 280, 9);
        }
        const int lineY = 51;
        using var chainGradient = new LinearGradientBrush(
            new Rectangle(70, lineY, Width - 140, 2), Web3Lime, Web3Teal, 0f);
        using (var chainPen = new Pen(chainGradient, 2))
        {
            graphics.DrawLine(chainPen, 70, lineY, Width - 70, lineY);
        }
        using var nodeBackground = new SolidBrush(Panel);
        for (var index = 0; index < 5; index++)
        {
            var centerX = index * (Width / 5) + Width / 10;
            using var halo = new Pen(Color.FromArgb(210, Web3Green), 2);
            using var center = new SolidBrush(index == 0 ? Web3Lime : Web3Teal);
            graphics.FillEllipse(nodeBackground, centerX - 7, lineY - 7, 14, 14);
            graphics.DrawEllipse(halo, centerX - 7, lineY - 7, 14, 14);
            graphics.FillEllipse(center, centerX - 2, lineY - 2, 4, 4);
            DrawCenteredMonoText(graphics, $"Node {index + 1:00}", centerX, 25, 11,
                index == 0 ? Web3Lime : Web3Green, FontStyle.Bold);
        }
    }

    private static void DrawPanel(
        Graphics graphics,
        Rectangle rect,
        Color accent,
        bool highlighted = false,
        bool flashOn = false,
        int panelIndex = 0)
    {
        using var path = RoundedRect(new Rectangle(rect.Left + 8, rect.Top + 3,
            rect.Width - 16, rect.Height - 7), 8);
        if (highlighted)
        {
            using var glow = new SolidBrush(Color.FromArgb(flashOn ? 58 : 12, Web3Green));
            graphics.FillPath(glow, path);
            using var outline = new Pen(accent, flashOn ? 6 : 2);
            graphics.DrawPath(outline, path);
        }
        if (panelIndex < 5)
        {
            var separatorX = rect.Right;
            using var separator = new Pen(ModuleSeparator, 1.8f);
            graphics.DrawLine(separator, separatorX, 72, separatorX, Height - 25);
            using var marker = new SolidBrush(Color.FromArgb(220, Web3Green));
            graphics.FillRectangle(marker, separatorX - 2, 72, 4, 9);
        }

    }

    private static void DrawAgentSession(
        Graphics graphics,
        AgentSnapshot agent,
        Rectangle rect)
    {
        var now = DateTime.Now;
        var completed = IsCompletionActive(agent, now);
        var completionFlashOn = completed && IsCompletionFlashOn(now);
        var accent = AgentColor(agent.Kind);
        var agentName = agent.Kind switch
        {
            AgentKind.Claude => "Claude Code",
            AgentKind.Cursor => "Cursor",
            _ => "Codex",
        };
        var statusColor = completed || agent.IsWorking
            ? completed && completionFlashOn ? Color.White : Green
            : Yellow;
        DrawColumnHeader(graphics, rect, agentName, string.Empty, statusColor);

        var hasStep = agent.StepCurrent is { } && agent.StepTotal is > 0;
        var project = agent.Project ?? (agent.Available ? "尚无会话" : $"未安装 {agentName}");
        var hasWorkspace = !string.IsNullOrWhiteSpace(agent.Workspace) &&
                           !string.Equals(agent.Workspace, project, StringComparison.OrdinalIgnoreCase);
        var titleTop = hasWorkspace ? rect.Top + 79 : rect.Top + 65;
        var titleSize = hasWorkspace ? 16f : 18f;
        if (hasWorkspace)
        {
            var workspace = agent.Workspace!;
            DrawText(graphics, FitSingleLine(graphics, workspace, 16, rect.Width - 44),
                rect.Left + 22, rect.Top + 56, 16, accent, FontStyle.Bold);
        }
        DrawText(graphics, FitSingleLine(graphics, project, titleSize,
                rect.Width - 44 - (hasStep ? 84 : 0)),
            rect.Left + 22, titleTop, titleSize, PrimaryText, FontStyle.Bold);
        if (agent.StepCurrent is { } current && agent.StepTotal is { } total && total > 0)
        {
            DrawRightText(graphics, $"{current:00}/{total:00}", rect.Right - 22,
                hasWorkspace ? rect.Top + 82 : rect.Top + 68,
                11, accent, FontStyle.Bold);
            DrawSegmentedProgress(graphics,
                new Rectangle(rect.Left + 22, hasWorkspace ? rect.Top + 105 : rect.Top + 93,
                    rect.Width - 44, 8),
                current, total, accent);
        }

        var stateTitle = completed
            ? "Task Completed"
            : agent.IsWorking ? "Agent Working"
            : "Waiting for You";
        var stateDetail = completed
            ? "Completion Alert Active"
            : agent.IsWorking ? "Live Session"
            : "Response Ready";
        var workingAnimation = agent.IsWorking && !completed;
        var workingPulse = workingAnimation
            ? (Math.Sin(now.TimeOfDay.TotalSeconds * Math.PI * 2 / 1.6) + 1) / 2
            : 0;
        var stateColor = completed && completionFlashOn ? Color.White : statusColor;
        var stateRect = new Rectangle(rect.Left + 18, rect.Top + 122, rect.Width - 36, 82);
        using (var statePath = RoundedRect(stateRect, 7))
        using (var stateFill = new SolidBrush(Color.FromArgb(completed && completionFlashOn
                   ? 48
                   : workingAnimation ? 16 + (int)(workingPulse * 18) : 20,
                   completed || agent.IsWorking ? Green : accent)))
        using (var stateBorder = new Pen(Color.FromArgb(completed && completionFlashOn
                   ? 220
                   : workingAnimation ? 90 + (int)(workingPulse * 90) : 105,
                   completed || agent.IsWorking ? Green : accent),
                   workingAnimation ? 1.2f + (float)workingPulse * 0.55f : 1.2f))
        {
            graphics.FillPath(stateFill, statePath);
            graphics.DrawPath(stateBorder, statePath);
        }
        if (workingAnimation)
        {
            var perimeter = 2d * (stateRect.Width + stateRect.Height);
            var animationPosition = now.TimeOfDay.TotalMilliseconds / 3600d * perimeter;
            using var movingGlow = new Pen(Color.FromArgb(80 + (int)(workingPulse * 50), stateColor), 5)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            using var movingCore = new Pen(Color.FromArgb(225, stateColor), 2.1f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            DrawMovingBorderSegment(graphics, stateRect, animationPosition, 74, movingGlow);
            DrawMovingBorderSegment(graphics, stateRect, animationPosition, 74, movingCore);
        }
        var pulseDiameter = workingAnimation ? 14 + (int)(workingPulse * 10) : 14;
        var pulseLeft = rect.Left + 35 - pulseDiameter / 2;
        var pulseTop = rect.Top + 149 - pulseDiameter / 2;
        using (var glow = new SolidBrush(Color.FromArgb(
                   workingAnimation ? 50 + (int)(workingPulse * 80) : 55, stateColor)))
        using (var dot = new SolidBrush(stateColor))
        {
            graphics.FillEllipse(glow, pulseLeft, pulseTop, pulseDiameter, pulseDiameter);
            graphics.FillEllipse(dot, rect.Left + 32, rect.Top + 146, 6, 6);
        }
        DrawText(graphics, stateTitle, rect.Left + 50, rect.Top + 135, 17,
            stateColor, FontStyle.Bold);
        DrawText(graphics, stateDetail, rect.Left + 50, rect.Top + 166, 10,
            SecondaryText, FontStyle.Bold);
        if (agent.LastActive is { } stateUpdated)
        {
            DrawRightText(graphics, $"Updated {stateUpdated:HH:mm:ss}", rect.Right - 28,
                rect.Top + 166, 9, SecondaryText, FontStyle.Bold);
        }

        var separatorY = rect.Top + 229;
        using (var separator = new Pen(InnerSeparator, 1.4f))
        {
            graphics.DrawLine(separator, rect.Left + 22, separatorY, rect.Right - 22, separatorY);
        }
        DrawText(graphics, agent.HasTokenUsage ? "Token flow" : "Local session",
            rect.Left + 22, separatorY + 15, 9, SecondaryText, FontStyle.Bold);
        DrawText(graphics, agent.HasTokenUsage ? FormatTokens(agent.TodayTotalTokens) : "Token N/A",
            rect.Left + 22, separatorY + 34, 27, PrimaryText, FontStyle.Bold);
        if (agent.HasTokenUsage)
        {
            DrawRightText(graphics, $"In  {FormatTokens(agent.TodayInputTokens)}", rect.Right - 22,
                separatorY + 32, 11, SecondaryText, FontStyle.Bold);
            DrawRightText(graphics, $"Out {FormatTokens(agent.TodayOutputTokens)}", rect.Right - 22,
                separatorY + 50, 11, SecondaryText, FontStyle.Bold);
        }
        else if (!string.IsNullOrWhiteSpace(agent.Model))
        {
            DrawRightText(graphics, Ellipsize(agent.Model!, 20), rect.Right - 22,
                separatorY + 36, 11, accent, FontStyle.Bold);
        }

        if (agent.QuotaUsedPercent is { } used)
        {
            var remaining = Math.Clamp(100 - used, 0, 100);
            DrawRightText(graphics, $"Credit {remaining:0}%", rect.Right - 22,
                separatorY + 15, 11, Yellow, FontStyle.Bold);
            DrawBlockMeter(graphics,
                new Rectangle(rect.Left + 22, rect.Top + 309, rect.Width - 44, 8),
                remaining / 100);
        }
        else if (agent.LastActive is { } active)
        {
            DrawRightText(graphics, $"Updated {active:HH:mm:ss}", rect.Right - 22,
                separatorY + 15, 9, SecondaryText, FontStyle.Bold);
            DrawBlockMeter(graphics,
                new Rectangle(rect.Left + 22, rect.Top + 309, rect.Width - 44, 8), 0);
        }
    }

    private static void DrawMovingBorderSegment(
        Graphics graphics,
        Rectangle rect,
        double start,
        double length,
        Pen pen)
    {
        var perimeter = 2d * (rect.Width + rect.Height);
        var position = ((start % perimeter) + perimeter) % perimeter;
        var remaining = Math.Min(length, perimeter);
        while (remaining > 0.01)
        {
            var edgeRemaining = position < rect.Width
                ? rect.Width - position
                : position < rect.Width + rect.Height
                    ? rect.Width + rect.Height - position
                    : position < rect.Width * 2 + rect.Height
                        ? rect.Width * 2 + rect.Height - position
                        : perimeter - position;
            var distance = Math.Min(remaining, edgeRemaining);
            var from = PointOnRectangle(rect, position);
            var to = PointOnRectangle(rect, (position + distance) % perimeter);
            graphics.DrawLine(pen, from, to);
            remaining -= distance;
            position = (position + distance) % perimeter;
        }
    }

    private static PointF PointOnRectangle(Rectangle rect, double position)
    {
        if (position <= rect.Width)
        {
            return new PointF(rect.Left + (float)position, rect.Top);
        }
        position -= rect.Width;
        if (position <= rect.Height)
        {
            return new PointF(rect.Right, rect.Top + (float)position);
        }
        position -= rect.Height;
        if (position <= rect.Width)
        {
            return new PointF(rect.Right - (float)position, rect.Bottom);
        }
        position -= rect.Width;
        return new PointF(rect.Left, rect.Bottom - (float)position);
    }

    private static void DrawColumnHeader(
        Graphics graphics,
        Rectangle rect,
        string title,
        string status,
        Color statusColor)
    {
        DrawText(graphics, title, rect.Left + 22, rect.Top + 12, 21,
            PrimaryText, FontStyle.Bold);
        if (!string.IsNullOrWhiteSpace(status))
        {
            DrawRightText(graphics, status, rect.Right - 22, rect.Top + 15, 11,
                statusColor, FontStyle.Bold);
        }
    }

    internal static bool IsCompletionActive(AgentSnapshot agent, DateTime now) =>
        agent.CompletedAt is { } completedAt &&
        now - completedAt >= TimeSpan.Zero;

    internal static bool IsCompletionFlashOn(DateTime now) =>
        ((long)(now.TimeOfDay.TotalMilliseconds / 450) & 1) == 0;

    private static void DrawMarkets(Graphics graphics, MarketSnapshot markets, Rectangle rect)
    {
        var asxStatus = ResolveAsxMarketStatus(markets.Stocks);
        var asxStatusColor = asxStatus switch
        {
            "LIVE" => Green,
            "CLOSED" => SecondaryText,
            _ => Yellow,
        };
        var asxDisplayStatus = asxStatus switch
        {
            "LIVE" => "Live",
            "CLOSED" => "Closed",
            _ => "Unknown",
        };
        DrawColumnHeader(graphics, rect, "Markets", asxDisplayStatus, asxStatusColor);

        DrawMarketRows(graphics, markets.Stocks, ["BGBL", "NDQ", "IBIT", "IVV"],
            rect.Left + 22, rect.Right - 22, rect.Top + 60, false, 29);
        DrawTechSeparator(graphics, rect.Left + 22, rect.Right - 22,
            rect.Top + 174, Web3Green);
        var cryptoQuotes = markets.Cryptos.ToArray();
        DrawMarketRows(graphics, markets.Cryptos, ["USDT/AUD", "BTC", "ETH", "WLFI"], rect.Left + 22,
            rect.Right - 22, rect.Top + 185, true, 29);

        var latest = cryptoQuotes.Concat(markets.Stocks)
            .OrderByDescending(quote => quote.UpdatedAt)
            .FirstOrDefault();
        var sources = string.Join("/", cryptoQuotes.Select(quote => quote.Source)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        var footer = markets.Error ?? (latest is null
            ? "正在连接免费行情…"
            : $"1h · {(sources.Length > 0 ? sources + " · " : string.Empty)}更新于 {latest.UpdatedAt:HH:mm:ss}");
        DrawCenteredText(graphics, Ellipsize(footer, 50), rect.Left + rect.Width / 2,
            rect.Top + 309, 9, markets.Error is null ? SecondaryText : Yellow);
    }

    internal static string ResolveAsxMarketStatus(IReadOnlyList<MarketQuote> stocks)
    {
        if (stocks.Count == 0)
        {
            return "UNKNOWN";
        }

        var openCount = stocks.Count(quote => quote.MarketOpen);
        return openCount * 2 >= stocks.Count ? "LIVE" : "CLOSED";
    }

    private static void DrawNews(Graphics graphics, NewsSnapshot news, Rectangle rect)
    {
        DrawColumnHeader(graphics, rect, "News",
            news.Articles.Count > 0 ? "Synced" : "Connecting",
            news.Articles.Count > 0 ? Green : Yellow);
        if (news.Articles.Count == 0)
        {
            DrawCenteredText(graphics, "正在连接 TextRadar…", rect.Left + rect.Width / 2,
                rect.Top + 155, 15, SecondaryText);
            DrawCenteredText(graphics, news.Error ?? string.Empty, rect.Left + rect.Width / 2,
                rect.Top + 306, 10, Yellow);
            return;
        }

        const int articlesPerPage = 4;
        var pageCount = Math.Max(1, (int)Math.Ceiling(news.Articles.Count / (double)articlesPerPage));
        var page = (int)(DateTimeOffset.Now.ToUnixTimeSeconds() / 20 % pageCount);
        var articles = news.Articles.Skip(page * articlesPerPage).Take(articlesPerPage).ToArray();
        for (var index = 0; index < articles.Length; index++)
        {
            var article = articles[index];
            var y = rect.Top + 64 + index * 58;
            DrawText(graphics, Ellipsize(article.Category, 13), rect.Left + 20, y,
                10, Web3Green, FontStyle.Bold);
            var sourceText = article.SourceCount > 0 ? $"{article.SourceCount}源 · " : string.Empty;
            DrawRightText(graphics, $"{sourceText}{FormatAge(article.PublishedAt)}",
                rect.Right - 22, y, 10, SecondaryText, FontStyle.Bold);
            const float newsTitleSize = 13;
            DrawText(graphics, FitSingleLine(graphics, article.Title, newsTitleSize, rect.Width - 40),
                rect.Left + 20, y + 20, newsTitleSize, PrimaryText);
            if (index < articles.Length - 1)
            {
                using var separator = new Pen(NewsSeparator, 1.5f);
                graphics.DrawLine(separator, rect.Left + 20, y + 49, rect.Right - 22, y + 49);
            }
        }

        var footer = news.Error ??
                     $"第 {page + 1}/{pageCount} 页 · 更新于 {news.UpdatedAt:HH:mm}";
        DrawCenteredText(graphics, footer, rect.Left + rect.Width / 2,
            rect.Top + 309, 9, news.Error is null ? SecondaryText : Yellow);
    }

    private static void DrawForecast(Graphics graphics, WeatherSnapshot weather, Rectangle rect)
    {
        var hours = weather.Hours.Take(4).ToArray();
        DrawColumnHeader(graphics, rect, "Forecast",
            hours.Length == 0 ? "Connecting" : weather.Location,
            hours.Length == 0 ? Yellow : Green);
        if (hours.Length == 0)
        {
            DrawCenteredText(graphics, "Connecting to Open-Meteo…", rect.Left + rect.Width / 2,
                rect.Top + 155, 15, SecondaryText);
            DrawCenteredText(graphics, weather.Error ?? string.Empty, rect.Left + rect.Width / 2,
                rect.Top + 306, 10, Yellow);
            return;
        }

        DrawText(graphics, "Next 6 Hours", rect.Left + 22, rect.Top + 58,
            10, Web3Green, FontStyle.Bold);
        var forecastLeft = rect.Left + 22;
        var forecastWidth = rect.Width - 44;
        var slotWidth = forecastWidth / 4f;
        for (var index = 0; index < hours.Length; index++)
        {
            var hour = hours[index];
            var center = forecastLeft + slotWidth * index + slotWidth / 2;
            DrawCenteredText(graphics, hour.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
                center, rect.Top + 79, 10, SecondaryText, FontStyle.Bold);
            DrawCenteredText(graphics, $"{hour.Temperature:0}°", center, rect.Top + 101,
                23, PrimaryText, FontStyle.Bold);
            DrawCenteredText(graphics, $"Rain {hour.PrecipitationProbability}%", center,
                rect.Top + 132, 9, Web3Teal, FontStyle.Bold);
            var wind = hour.WindSpeed is { } speed
                ? $"{WindDirection(hour.WindDirection)} {speed:0}km/h"
                : "Wind --";
            DrawCenteredText(graphics, wind, center, rect.Top + 151,
                8, SecondaryText, FontStyle.Bold);
            if (index < hours.Length - 1)
            {
                using var divider = new Pen(Color.FromArgb(105, InnerSeparator), 1);
                var dividerX = forecastLeft + slotWidth * (index + 1);
                graphics.DrawLine(divider, dividerX, rect.Top + 78, dividerX, rect.Top + 170);
            }
        }

        DrawTechSeparator(graphics, rect.Left + 22, rect.Right - 22, rect.Top + 181, Web3Green);
        var statWidth = (rect.Width - 36) / 3;
        var range = weather.TodayMinimum is { } minimum && weather.TodayMaximum is { } maximum
            ? $"{minimum:0}° — {maximum:0}°"
            : "--";
        var peakWind = hours.Where(hour => hour.WindSpeed is not null)
            .Select(hour => hour.WindSpeed!.Value)
            .DefaultIfEmpty(weather.WindSpeed ?? 0)
            .Max();
        DrawForecastStat(graphics, rect.Left + 18 + statWidth / 2, rect.Top + 198,
            "Today Range", range);
        DrawForecastStat(graphics, rect.Left + 18 + statWidth + statWidth / 2, rect.Top + 198,
            "Max Rain", weather.TodayRainProbability is { } rain ? $"{rain}%" : "--");
        DrawForecastStat(graphics, rect.Left + 18 + statWidth * 2 + statWidth / 2, rect.Top + 198,
            "Peak Wind", peakWind > 0 ? $"{peakWind:0} km/h" : "--");

        DrawText(graphics, "Temperature Trend", rect.Left + 22, rect.Top + 246,
            9, SecondaryText, FontStyle.Bold);
        DrawSparkline(graphics, hours.Select(hour => (decimal)hour.Temperature).ToArray(),
            new Rectangle(rect.Left + 22, rect.Top + 266, rect.Width - 44, 30), Web3Teal);

        var footer = weather.Error ?? $"Open-Meteo · Updated {weather.UpdatedAt:HH:mm}";
        DrawCenteredText(graphics, footer, rect.Left + rect.Width / 2,
            rect.Top + 312, 9, weather.Error is null ? SecondaryText : Yellow);
    }

    private static void DrawForecastStat(Graphics graphics, int centerX, int y, string label, string value)
    {
        DrawCenteredText(graphics, label, centerX, y, 9, SecondaryText, FontStyle.Bold);
        DrawCenteredText(graphics, value, centerX, y + 18, 12, PrimaryText, FontStyle.Bold);
    }

    private static void DrawMarketRows(
        Graphics graphics,
        IReadOnlyList<MarketQuote> quotes,
        IReadOnlyList<string> symbols,
        int left,
        int right,
        int top,
        bool crypto,
        int rowSpacing)
    {
        for (var index = 0; index < symbols.Count; index++)
        {
            var symbol = symbols[index];
            var quote = quotes.FirstOrDefault(item => item.Symbol == symbol);
            var y = top + index * rowSpacing;
            var displaySymbol = symbol == "USDT/AUD" ? "USDT" : symbol;
            DrawText(graphics, displaySymbol, left, y, 14, PrimaryText, FontStyle.Bold);
            if (quote is null)
            {
                DrawRightText(graphics, "—", right, y, 14, SecondaryText);
                continue;
            }
            var changeColor = quote.ChangePercent > 0
                ? MarketUp
                : quote.ChangePercent < 0 ? MarketDown : SecondaryText;
            const int sparkWidth = 94;
            var sparkRight = right - 64;
            var sparkLeft = sparkRight - sparkWidth;
            DrawRightText(graphics, FormatMarketPrice(quote.Price, crypto, quote.PriceTickSize, quote.Symbol),
                sparkLeft - 24, y,
                14, PrimaryText, FontStyle.Bold);
            DrawSparkline(graphics, quote.History,
                new Rectangle(sparkLeft, y + 1, sparkWidth, 18), changeColor);
            DrawRightText(graphics, $"{quote.ChangePercent:+0.00;-0.00;0.00}%", right, y + 1,
                13, changeColor, FontStyle.Bold);
        }
    }

    private static void DrawSparkline(
        Graphics graphics,
        IReadOnlyList<decimal>? history,
        Rectangle rect,
        Color color)
    {
        if (history is null || history.Count < 2)
        {
            using var waiting = new Pen(Color.FromArgb(70, SecondaryText), 1);
            graphics.DrawLine(waiting, rect.Left, rect.Top + rect.Height / 2,
                rect.Right, rect.Top + rect.Height / 2);
            return;
        }

        var values = history.TakeLast(24).Select(decimal.ToDouble).ToArray();
        var minimum = values.Min();
        var maximum = values.Max();
        var spread = maximum - minimum;
        var points = new PointF[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            var x = rect.Left + rect.Width * index / (float)Math.Max(1, values.Length - 1);
            var normalized = spread <= double.Epsilon
                ? 0.5
                : (values[index] - minimum) / spread;
            var y = rect.Bottom - 2 - (float)(normalized * (rect.Height - 4));
            points[index] = new PointF(x, y);
        }

        var isMarketUp = color.ToArgb() == MarketUp.ToArgb();
        if (isMarketUp)
        {
            using var glowPen = new Pen(Color.FromArgb(62, MarketUp), 4.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            graphics.DrawLines(glowPen, points);
        }

        using var pen = new Pen(color, isMarketUp ? 2.1f : 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        graphics.DrawLines(pen, points);
        var endpoint = points[^1];
        var endpointColor = isMarketUp ? MarketUpHighlight : color;
        using var endpointGlow = new SolidBrush(Color.FromArgb(72, endpointColor));
        using var endpointRing = new Pen(Color.FromArgb(235, endpointColor), 1.4f);
        using var endpointCore = new SolidBrush(endpointColor);
        graphics.FillEllipse(endpointGlow, endpoint.X - 5.2f, endpoint.Y - 5.2f, 10.4f, 10.4f);
        graphics.DrawEllipse(endpointRing, endpoint.X - 3.4f, endpoint.Y - 3.4f, 6.8f, 6.8f);
        graphics.FillEllipse(endpointCore, endpoint.X - 2.1f, endpoint.Y - 2.1f, 4.2f, 4.2f);
    }

    private static void DrawAiQuota(Graphics graphics, AgentsSnapshot agents, Rectangle rect)
    {
        var codexQuota = (agents.CodexSessions ?? Array.Empty<AgentSnapshot>())
            .Append(agents.Codex)
            .Where(agent => agent.Kind == AgentKind.Codex && agent.QuotaUsedPercent is not null)
            .OrderByDescending(agent => agent.LastActive)
            .FirstOrDefault();
        var claudeQuota = agents.Claude.QuotaUsedPercent is not null ? agents.Claude : null;
        var updatedAt = new[] { codexQuota?.LastActive, claudeQuota?.LastActive }
            .Where(value => value is not null)
            .Max();
        var codexRemaining = codexQuota?.QuotaUsedPercent is { } used
            ? Math.Clamp(100 - used, 0, 100)
            : (double?)null;
        DrawColumnHeader(graphics, rect, "AI Quota",
            codexRemaining is { } value ? $"{value:0}% Left" : "Local",
            codexRemaining is null ? SecondaryText : Green);

        var center = new Point(rect.Left + rect.Width / 2, rect.Top + 136);
        if (codexQuota?.QuotaUsedPercent is { } codexUsed)
        {
            var remaining = Math.Clamp(100 - codexUsed, 0, 100);
            DrawGauge(graphics, center, 67, remaining, QuotaOrange);
            DrawCenteredText(graphics, $"{remaining:0}%", center.X, rect.Top + 116,
                27, PrimaryText, FontStyle.Bold);
            DrawCenteredText(graphics, "Codex credit", center.X, rect.Top + 154,
                9, QuotaOrange, FontStyle.Bold);
        }
        else
        {
            DrawGauge(graphics, center, 67, 0, SecondaryText);
            DrawCenteredText(graphics, "--", center.X, rect.Top + 116,
                27, SecondaryText, FontStyle.Bold);
            DrawCenteredText(graphics, "Codex credit", center.X, rect.Top + 154,
                9, SecondaryText, FontStyle.Bold);
        }

        DrawText(graphics, "Reset", rect.Left + 22, rect.Top + 224,
            9, SecondaryText, FontStyle.Bold);
        DrawRightText(graphics,
            codexQuota is null ? "--" : FormatQuotaReset(codexQuota.QuotaResetsAt),
            rect.Right - 22, rect.Top + 224, 11, PrimaryText, FontStyle.Bold);
        DrawBlockMeter(graphics,
            new Rectangle(rect.Left + 22, rect.Top + 250, rect.Width - 44, 8),
            (codexRemaining ?? 0) / 100);

        DrawText(graphics, "Claude", rect.Left + 22, rect.Top + 279,
            9, SecondaryText, FontStyle.Bold);
        if (claudeQuota?.QuotaUsedPercent is { } claudeUsed)
        {
            var remaining = Math.Clamp(100 - claudeUsed, 0, 100);
            DrawRightText(graphics, $"{remaining:0}% Left · {FormatQuotaWindow(claudeQuota.QuotaWindowMinutes)}",
                rect.Right - 22, rect.Top + 277, 11, Yellow, FontStyle.Bold);
            DrawBlockMeter(graphics,
                new Rectangle(rect.Left + 22, rect.Top + 301, rect.Width - 44, 8),
                remaining / 100);
        }
        else
        {
            DrawRightText(graphics, agents.Claude.Available ? "Await /usage" : "Not connected",
                rect.Right - 22, rect.Top + 277, 11,
                agents.Claude.Available ? Yellow : SecondaryText, FontStyle.Bold);
            DrawBlockMeter(graphics,
                new Rectangle(rect.Left + 22, rect.Top + 301, rect.Width - 44, 8),
                0);
        }

        DrawCenteredText(graphics,
            updatedAt is { } updated ? $"Local quota · Updated {updated:HH:mm}" : "Local quota source",
            rect.Left + rect.Width / 2, rect.Top + 317, 8, SecondaryText, FontStyle.Bold);
    }

    private static void DrawToday(
        Graphics graphics,
        SystemSnapshot system,
        WeatherSnapshot weather,
        Rectangle rect)
    {
        var now = DateTime.Now;
        DrawText(graphics, now.ToString("HH:mm", CultureInfo.InvariantCulture),
            rect.Left + 22, rect.Top + 17, 42, PrimaryText, FontStyle.Bold);
        DrawText(graphics, now.ToString(":ss", CultureInfo.InvariantCulture),
            rect.Left + 151, rect.Top + 36, 18, Web3Green, FontStyle.Bold);

        if (weather.Temperature is { } temperature)
        {
            DrawCompactWeatherIcon(graphics, rect.Left + 238, rect.Top + 52,
                weather.WeatherCode ?? 0);
            DrawText(graphics, $"{temperature:0}°", rect.Left + 292, rect.Top + 22,
                30, PrimaryText, FontStyle.Bold);
            DrawText(graphics, WeatherDescription(weather.WeatherCode ?? 0),
                rect.Left + 292, rect.Top + 61, 10, Web3Green, FontStyle.Bold);
        }
        else
        {
            DrawRightText(graphics, "Weather", rect.Right - 22,
                rect.Top + 27, 12, SecondaryText, FontStyle.Bold);
            DrawRightText(graphics, "Connecting", rect.Right - 22,
                rect.Top + 54, 9, Yellow, FontStyle.Bold);
        }

        DrawTechSeparator(graphics, rect.Left + 22, rect.Right - 22,
            rect.Top + 105, Web3Green);

        DrawSystemMetric(graphics, rect, rect.Top + 123, "CPU",
            system.CpuTemperature is { } cpuTemperature
                ? $"{system.CpuPercent:0}% · {cpuTemperature:0}°C"
                : $"{system.CpuPercent:0}%",
            system.CpuPercent / 100);
        DrawSystemMetric(graphics, rect, rect.Top + 175, "GPU",
            system.GpuTemperature is { } gpuTemperature
                ? $"{system.GpuPercent:0}% · {gpuTemperature:0}°C"
                : $"{system.GpuPercent:0}%",
            system.GpuPercent / 100);
        DrawSystemMetric(graphics, rect, rect.Top + 227, "Memory",
            $"{system.MemoryPercent:0}% · {system.UsedMemory / 1024d / 1024 / 1024:0.0}/{system.TotalMemory / 1024d / 1024 / 1024:0.#}G",
            system.MemoryPercent / 100);
        DrawProcessInfo(graphics, rect, rect.Top + 279, system.ProcessCount);
    }

    private static void DrawSystemMetric(
        Graphics graphics,
        Rectangle rect,
        int y,
        string label,
        string value,
        double fraction)
    {
        DrawText(graphics, label, rect.Left + 22, y - 2, 12, PrimaryText, FontStyle.Bold);
        DrawRightText(graphics, value, rect.Right - 22, y - 2, 13, PrimaryText, FontStyle.Bold);
        DrawBlockMeter(graphics,
            new Rectangle(rect.Left + 22, y + 21, rect.Width - 44, 8), fraction);
    }

    private static void DrawProcessInfo(Graphics graphics, Rectangle rect, int y, int processCount)
    {
        DrawText(graphics, "Processes", rect.Left + 22, y - 2, 12, PrimaryText, FontStyle.Bold);
        DrawRightText(graphics, $"{processCount} Running", rect.Right - 22,
            y - 2, 13, PrimaryText, FontStyle.Bold);
    }

    private static void DrawTechSeparator(Graphics graphics, int left, int right, int y, Color accent)
    {
        using var node = new SolidBrush(accent);
        graphics.FillRectangle(node, left, y - 2, 5, 5);
        graphics.FillRectangle(node, right - 5, y - 2, 5, 5);

        using var dot = new SolidBrush(Color.FromArgb(220, accent));
        for (var x = left + 11; x <= right - 12; x += 7)
        {
            graphics.FillRectangle(dot, x, y - 1, 3, 2);
        }
    }

    private static void DrawCompactWeatherIcon(Graphics graphics, int x, int y, int code)
    {
        var isRain = code is >= 51 and <= 67 or >= 80 and <= 82;
        var isSnow = code is >= 71 and <= 77 or >= 85 and <= 86;
        var isStorm = code >= 95;
        var isCloudy = code is >= 1 and <= 3 || isRain || isSnow || isStorm || code is 45 or 48;

        if (!isCloudy || code is 1 or 2)
        {
            var sunX = isCloudy ? x - 12 : x;
            var sunY = isCloudy ? y - 9 : y;
            using var sun = new SolidBrush(Yellow);
            graphics.FillEllipse(sun, sunX - 18, sunY - 18, 36, 36);
        }
        if (!isCloudy)
        {
            return;
        }

        using var cloud = new SolidBrush(Color.FromArgb(220, 226, 239));
        graphics.FillEllipse(cloud, x - 29, y - 6, 39, 29);
        graphics.FillEllipse(cloud, x - 7, y - 18, 42, 42);
        graphics.FillEllipse(cloud, x + 16, y - 4, 35, 27);
        graphics.FillRectangle(cloud, x - 28, y + 7, 77, 18);
        if (isRain)
        {
            using var rain = new Pen(Web3Teal, 3);
            for (var offset = -17; offset <= 25; offset += 14)
            {
                graphics.DrawLine(rain, x + offset, y + 30, x + offset - 4, y + 40);
            }
        }
        else if (isSnow)
        {
            using var snow = new SolidBrush(Color.FromArgb(225, PrimaryText));
            for (var offset = -17; offset <= 25; offset += 14)
            {
                graphics.FillEllipse(snow, x + offset, y + 30, 5, 5);
            }
        }
        else if (isStorm)
        {
            using var lightning = new Pen(Yellow, 4);
            graphics.DrawLines(lightning,
            new Point[]
            {
                new Point(x + 2, y + 25), new Point(x - 5, y + 37),
                new Point(x + 4, y + 36), new Point(x - 2, y + 48),
            });
        }
    }

    private static void DrawGauge(Graphics graphics, Point center, int radius, double percentage, Color color)
    {
        var bounds = new Rectangle(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        using var trackPen = new Pen(Color.FromArgb(95, Track), 14)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var valuePen = new Pen(color, 14)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        graphics.DrawArc(trackPen, bounds, 135, 270);
        graphics.DrawArc(valuePen, bounds, 135, (float)(270 * Math.Clamp(percentage / 100, 0, 1)));
    }

    private static void DrawSegmentedProgress(Graphics graphics, Rectangle rect, int current, int total, Color color)
    {
        var gap = 5;
        var width = Math.Max(2, (rect.Width - (total - 1) * gap) / Math.Max(1, total));
        for (var index = 0; index < total; index++)
        {
            var segment = new Rectangle(rect.Left + index * (width + gap), rect.Top, width, rect.Height);
            DrawBar(graphics, segment, 1, index < current ? color : Track, rect.Height / 2);
        }
    }

    private static void DrawBlockMeter(
        Graphics graphics,
        Rectangle rect,
        double fraction,
        Color? fillColor = null)
    {
        const int gap = 4;
        var squareSize = Math.Max(4, rect.Height);
        var segmentCount = Math.Max(1, (rect.Width + gap) / (squareSize + gap));
        var clamped = Math.Clamp(fraction, 0, 1);
        using var trackBrush = new SolidBrush(Track);
        using Brush meterBrush = fillColor is { } solidColor
            ? new SolidBrush(solidColor)
            : new LinearGradientBrush(rect, Web3Lime, Web3Teal, 0f);
        for (var index = 0; index < segmentCount; index++)
        {
            var left = rect.Left + index * (squareSize + gap);
            var segment = new Rectangle(left, rect.Top, squareSize, squareSize);
            graphics.FillRectangle(trackBrush, segment);

            var segmentFraction = Math.Clamp(clamped * segmentCount - index, 0, 1);
            var filledWidth = (int)Math.Round(segment.Width * segmentFraction);
            if (filledWidth > 0)
            {
                graphics.FillRectangle(meterBrush,
                    new Rectangle(segment.Left, segment.Top, filledWidth, segment.Height));
            }
        }
    }

    private static void DrawBar(Graphics graphics, Rectangle rect, double fraction, Color color, int radius)
    {
        using var trackPath = RoundedRect(rect, radius);
        using var trackBrush = new SolidBrush(Track);
        graphics.FillPath(trackBrush, trackPath);
        var valueWidth = (int)Math.Round(rect.Width * Math.Clamp(fraction, 0, 1));
        if (valueWidth <= 0)
        {
            return;
        }
        var valueRect = new Rectangle(rect.Left, rect.Top, Math.Max(rect.Height, valueWidth), rect.Height);
        using var valuePath = RoundedRect(valueRect, radius);
        using Brush brush = IsWeb3Accent(color)
            ? new LinearGradientBrush(valueRect, Web3Lime, Web3Teal, 0f)
            : new SolidBrush(color);
        graphics.FillPath(brush, valuePath);
    }

    private static bool IsWeb3Accent(Color color) =>
        color.ToArgb() == Web3Lime.ToArgb() ||
        color.ToArgb() == Web3Green.ToArgb() ||
        color.ToArgb() == Web3Teal.ToArgb();

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (diameter <= 1)
        {
            path.AddRectangle(rect);
            return path;
        }
        var arc = new Rectangle(rect.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawText(Graphics graphics, string text, float x, float y, float size,
        Color color, FontStyle style = FontStyle.Regular)
    {
        using var font = CreateFont(size, style);
        using var brush = new SolidBrush(color);
        graphics.DrawString(text, font, brush, x, y, StringFormat.GenericTypographic);
    }

    private static void DrawRightText(Graphics graphics, string text, float right, float y, float size,
        Color color, FontStyle style = FontStyle.Regular)
    {
        using var font = CreateFont(size, style);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Far };
        graphics.DrawString(text, font, brush, new PointF(right, y), format);
    }

    private static void DrawCenteredText(Graphics graphics, string text, float centerX, float y, float size,
        Color color, FontStyle style = FontStyle.Regular)
    {
        using var font = CreateFont(size, style);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center };
        graphics.DrawString(text, font, brush, new PointF(centerX, y), format);
    }

    private static void DrawCenteredMonoText(Graphics graphics, string text, float centerX, float y, float size,
        Color color, FontStyle style = FontStyle.Regular)
    {
        using var font = CreateMonoFont(size, style);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center };
        graphics.DrawString(text, font, brush, new PointF(centerX, y), format);
    }

    private static Font CreateFont(float size, FontStyle style)
    {
        try
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel);
        }
    }

    private static Font CreateMonoFont(float size, FontStyle style)
    {
        try
        {
            return new Font("Cascadia Mono", size, style, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font("Consolas", size, style, GraphicsUnit.Pixel);
        }
    }

    private static Color AgentColor(AgentKind _) => Web3Teal;

    private static string FormatTokens(ulong value) => value switch
    {
        >= 100_000_000 => $"{value / 100_000_000d:0.##}亿",
        >= 10_000 => $"{value / 10_000d:0.##}万",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    private static string FormatAge(DateTime value)
    {
        var age = DateTime.Now - value;
        if (age.TotalMinutes < 1)
        {
            return "刚刚";
        }
        if (age.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)age.TotalMinutes)}分钟前";
        }
        return age.TotalHours < 24
            ? $"{Math.Max(1, (int)age.TotalHours)}小时前"
            : value.ToString("MM-dd", CultureInfo.InvariantCulture);
    }

    private static string WeatherDescription(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Showers",
        >= 85 and <= 86 => "Snow showers",
        >= 95 => "Thunderstorm",
        _ => "Variable",
    };

    private static string WindDirection(int? degrees)
    {
        if (degrees is null)
        {
            return string.Empty;
        }
        string[] directions = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        return directions[(int)Math.Round(degrees.Value / 45d) % 8];
    }

    internal static string FormatMarketPrice(
        decimal value,
        bool crypto,
        decimal? priceTickSize = null,
        string? symbol = null)
    {
        var prefix = symbol == "USDT/AUD" ? string.Empty : crypto ? "$" : "A$";
        int decimalPlaces;
        if (!crypto)
        {
            decimalPlaces = 2;
        }
        else if (priceTickSize is > 0)
        {
            var tickText = priceTickSize.Value.ToString("0.############################",
                CultureInfo.InvariantCulture);
            var separator = tickText.IndexOf('.');
            decimalPlaces = separator < 0 ? 0 : tickText.Length - separator - 1;
            decimalPlaces = Math.Min(decimalPlaces, 6);
        }
        else
        {
            decimalPlaces = value switch
            {
                >= 10_000 => 0,
                >= 1 => 2,
                >= 0.01m => 4,
                _ => 6,
            };
        }
        var format = decimalPlaces == 0
            ? "#,##0"
            : "#,##0." + new string('0', decimalPlaces);
        return prefix + value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatQuotaWindow(int? minutes) => minutes switch
    {
        null => "Window",
        >= 1440 when minutes.Value % 1440 == 0 => $"{minutes.Value / 1440}d window",
        >= 60 when minutes.Value % 60 == 0 => $"{minutes.Value / 60}h window",
        _ => $"{minutes}m window",
    };

    private static string FormatQuotaReset(DateTime? reset)
    {
        if (reset is null)
        {
            return "Reset --";
        }
        var remaining = reset.Value - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return "Reset due";
        }
        if (remaining.TotalDays >= 1)
        {
            return $"Reset {(int)remaining.TotalDays}d {remaining.Hours}h";
        }
        return $"Reset {remaining.Hours}h {remaining.Minutes}m";
    }

    private static string Ellipsize(string text, int length) =>
        text.Length <= length ? text : text[..(length - 1)] + "…";

    private static string FitSingleLine(Graphics graphics, string text, float size, float maxWidth)
    {
        var value = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }
        using var font = CreateFont(size, FontStyle.Regular);
        if (graphics.MeasureString(value, font, int.MaxValue, StringFormat.GenericTypographic).Width <= maxWidth)
        {
            return value;
        }

        var low = 1;
        var high = value.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var candidate = value[..middle] + "…";
            if (graphics.MeasureString(candidate, font, int.MaxValue, StringFormat.GenericTypographic).Width <= maxWidth)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        return value[..low] + "…";
    }
}
