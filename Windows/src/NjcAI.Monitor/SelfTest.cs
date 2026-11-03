using System.Drawing;
using System.Text.Json;
using NjcAI.Monitor.Core;
using NjcAI.Monitor.Rendering;
using NjcAI.Monitor.Services;
using NjcAI.Monitor.Usb;

namespace NjcAI.Monitor;

internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            TestHandshake();
            TestFramePackets();
            TestSingleInstanceGuard();
            TestAgentLogs();
            TestAgentCompletionTracker();
            TestDynamicPanelResolver();
            TestMarketParsing();
            TestAsxMarketStatus();
            TestNewsParsing();
            TestWeatherParsing();
            TestRenderer();
            Log.Info("Self-test passed");
            return 0;
        }
        catch (Exception exception)
        {
            Log.Error("Self-test failed", exception);
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void TestAgentCompletionTracker()
    {
        var now = DateTime.Now;
        var working = new AgentSnapshot(
            AgentKind.Codex, true, 100, 20, now, "completion-project", "正在执行测试",
            true, false, null, "test", null, null, null, null, null, true,
            "completion-session");
        AgentsSnapshot Snapshot(AgentSnapshot codex) => new(
            AgentSnapshot.Missing(AgentKind.Claude), codex,
            AgentSnapshot.Missing(AgentKind.Cursor), [codex]);

        var tracker = new AgentCompletionTracker(persistenceEnabled: false);
        var initial = tracker.Update(Snapshot(working), now);
        Assert(initial.CompletedAgents.Count == 0 && initial.Snapshot.Codex.CompletedAt is null,
            "completion tracker establishes startup baseline without notifying");

        var finished = working with
        {
            IsWorking = false,
            NeedsAttention = true,
            Activity = "任务已经完成",
            LastActive = now.AddSeconds(1),
        };
        var completed = tracker.Update(Snapshot(finished), now.AddSeconds(1));
        Assert(completed.CompletedAgents.Count == 1 &&
               completed.Snapshot.Codex.CompletedAt == now.AddSeconds(1) &&
               DashboardRenderer.IsCompletionActive(completed.Snapshot.Codex, now.AddSeconds(2)),
            "working-to-finished transition triggers one completion highlight");
        var flashBase = DateTime.Today;
        Assert(DashboardRenderer.IsCompletionFlashOn(flashBase) !=
               DashboardRenderer.IsCompletionFlashOn(flashBase.AddMilliseconds(450)),
            "completion highlight alternates between bright and dim phases");

        var steady = tracker.Update(Snapshot(finished), now.AddSeconds(2));
        Assert(steady.CompletedAgents.Count == 0 && steady.Snapshot.Codex.CompletedAt is not null,
            "completed state does not notify repeatedly");

        var resumedTracker = new AgentCompletionTracker(persistenceEnabled: false);
        _ = resumedTracker.Update(Snapshot(working), now);
        _ = resumedTracker.Update(Snapshot(finished), now.AddSeconds(1));
        var resumedWorking = finished with
        {
            IsWorking = true,
            NeedsAttention = false,
            LastActive = now.AddSeconds(3),
        };
        var resumedUpdate = resumedTracker.Update(Snapshot(resumedWorking), now.AddSeconds(3));
        Assert(resumedUpdate.PendingChanged && resumedTracker.PendingAlerts.Count == 0 &&
               resumedUpdate.Snapshot.Codex.CompletedAt is null &&
               resumedUpdate.Snapshot.Codex.IsWorking,
            "resuming a completed session clears its alert and returns to working");

        var persistent = tracker.Update(Snapshot(finished), now.AddHours(2));
        Assert(persistent.Snapshot.Codex.CompletedAt is not null &&
               DashboardRenderer.IsCompletionActive(persistent.Snapshot.Codex, now.AddHours(2)),
            "completion highlight persists until acknowledged");
        Assert(tracker.PendingAlerts.Count == 1 &&
               tracker.PendingAlerts[0].Project == "completion-project" &&
               tracker.Acknowledge(finished.Identity) && !tracker.Acknowledge(finished.Identity),
            "individual completion acknowledgement clears one pending alert once");
        var acknowledged = tracker.Update(Snapshot(finished), now.AddHours(2).AddSeconds(1));
        Assert(acknowledged.Snapshot.Codex.CompletedAt is null &&
               acknowledged.CompletedAgents.Count == 0,
            "acknowledged completion stays cleared while the session remains idle");

        var resumed = finished with
        {
            IsWorking = true,
            NeedsAttention = false,
            LastActive = now.AddHours(2).AddSeconds(2),
        };
        _ = tracker.Update(Snapshot(resumed), now.AddHours(2).AddSeconds(2));
        var waiting = resumed with
        {
            IsWorking = false,
            NeedsAttention = true,
            WaitingFor = "approval",
            LastActive = now.AddHours(2).AddSeconds(3),
        };
        var waitingUpdate = tracker.Update(Snapshot(waiting), now.AddHours(2).AddSeconds(3));
        Assert(waitingUpdate.CompletedAgents.Count == 0,
            "waiting-for-user transition remains attention instead of completion");

        var persistenceRoot = Path.Combine(AppContext.BaseDirectory,
            $".njc-completion-{Guid.NewGuid():N}");
        var persistencePath = Path.Combine(persistenceRoot, "alerts.json");
        try
        {
            var writer = new AgentCompletionTracker(persistencePath);
            _ = writer.Update(Snapshot(working), now);
            _ = writer.Update(Snapshot(finished), now.AddSeconds(1));
            Assert(writer.PendingAlerts.Count == 1,
                "completion alert cache is retained in memory");
            Assert(File.Exists(persistencePath),
                "completion alert cache is written");
            var reader = new AgentCompletionTracker(persistencePath);
            var restored = reader.Update(Snapshot(finished), now.AddSeconds(2));
            Assert(reader.PendingAlerts.Count == 1,
                "completion alert cache is loaded");
            Assert(restored.Snapshot.Codex.CompletedAt == now.AddSeconds(1),
                "unacknowledged completion survives app restart");
            var restoredWorking = reader.Update(Snapshot(working with
            {
                LastActive = now.AddSeconds(3),
            }), now.AddSeconds(3));
            Assert(restoredWorking.PendingChanged && reader.PendingAlerts.Count == 0 &&
                   restoredWorking.Snapshot.Codex.CompletedAt is null,
                "resuming after app restart clears the persisted completion alert");
            Assert(new AgentCompletionTracker(persistencePath).PendingAlerts.Count == 0,
                "automatic completion acknowledgement persists across app restart");

            _ = reader.Update(Snapshot(finished with
            {
                LastActive = now.AddSeconds(4),
            }), now.AddSeconds(4));
            Assert(reader.AcknowledgeAll() == 1 &&
                   new AgentCompletionTracker(persistencePath).PendingAlerts.Count == 0,
                "acknowledged completion stays cleared across app restart");
        }
        finally
        {
            if (Directory.Exists(persistenceRoot))
            {
                Directory.Delete(persistenceRoot, true);
            }
        }
    }

    private static void TestSingleInstanceGuard()
    {
        var mutexName = $@"Local\NjcAI.Monitor.SelfTest.{Guid.NewGuid():N}";
        using (var first = SingleInstanceGuard.TryAcquire(mutexName))
        using (var duplicate = SingleInstanceGuard.TryAcquire(mutexName))
        {
            Assert(first.HasOwnership, "first app instance owns the mutex");
            Assert(!duplicate.HasOwnership, "duplicate app instance is rejected");
        }

        using var restarted = SingleInstanceGuard.TryAcquire(mutexName);
        Assert(restarted.HasOwnership, "single-instance mutex is released on exit");
    }

    private static void TestHandshake()
    {
        var response = new byte[512];
        response[0] = 0x03;
        response[1] = 0xFF;
        response[8] = 0x01;
        response[20] = LyProtocol.TrofeoVision113Profile;
        response[22] = 0x02;
        var info = LyProtocol.ParseHandshake(response, LyProtocol.ProductLy);
        Assert(info.Fbl == 192, "FBL mapping");
        Assert(info.Width == 1920 && info.Height == 400, "Trofeo Vision 11.3 resolution");
        Assert(LyProtocol.IsTrofeoVision113(info), "Trofeo Vision 11.3 profile detection");

        response[20] = 0x01;
        var olderInfo = LyProtocol.ParseHandshake(response, LyProtocol.ProductLy);
        Assert(olderInfo.Width == 1920 && olderInfo.Height == 480,
            "Trofeo Vision 9.16 remains distinguishable");
        Assert(!LyProtocol.IsTrofeoVision113(olderInfo), "9.16 is rejected by the 11.3 target check");
    }

    private static void TestFramePackets()
    {
        var jpeg = Enumerable.Range(0, 1000).Select(index => (byte)(index & 0xFF)).ToArray();
        var transfer = LyProtocol.CreateFrameTransfer(jpeg, LyProtocol.ProductLy);
        Assert(transfer.Length == 2048, "LY transfer padding");
        Assert(transfer[0] == 0x01 && transfer[1] == 0xFF, "chunk magic");
        Assert(BitConverter.ToUInt32(transfer, 2) == 1000, "frame length header");
        Assert(BitConverter.ToUInt16(transfer, 9) == 3, "chunk count");
        Assert(transfer[16] == jpeg[0] && transfer[512 + 16] == jpeg[496], "chunk payload");
    }

    private static void TestRenderer()
    {
        var renderer = new DashboardRenderer();
        var settings = new AppSettings { LeftAgent = AgentKind.Claude, RightAgent = AgentKind.Codex };
        using var bitmap = renderer.Render(DemoData.Create(), settings, "自检");
        Assert(bitmap.Width == DashboardRenderer.Width && bitmap.Height == DashboardRenderer.Height,
            "render dimensions");
        var jpeg = DashboardRenderer.EncodeJpeg(bitmap, 82, false);
        Assert(jpeg.Length > 10_000 && jpeg.Length <= LyProtocol.MaximumJpegSize, "JPEG size");
        Assert(jpeg[0] == 0xFF && jpeg[1] == 0xD8, "JPEG signature");

        var idle = DemoData.Create() with { Agents = AgentsSnapshot.Empty };
        using var idleBitmap = new DashboardRenderer().Render(idle, settings, "天气面板自检");
        Assert(idleBitmap.Width == 1920 && idleBitmap.Height == 400, "idle weather layout dimensions");

        var completedDemo = DemoData.Create(completed: true);
        var completedPanels = new DynamicPanelResolver().Resolve(completedDemo.Agents, DateTime.Now);
        var completedAgents = completedPanels
            .Where(panel => panel.Kind == DashboardPanelKind.Agent)
            .Select(panel => panel.Agent!)
            .ToArray();
        Assert(completedAgents.Length == 2 &&
               completedAgents.Any(agent => agent.IsWorking) &&
               completedAgents.Any(agent => agent.CompletedAt is not null),
            "completion demo contains one working and one completed agent");
    }

    private static void TestAgentLogs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"NJC-AI-Monitor-selftest-{Guid.NewGuid():N}");
        try
        {
            var claude = Path.Combine(root, ".claude", "projects", "demo");
            var oldSessionDay = DateTime.Today.AddDays(-30);
            var codex = Path.Combine(root, ".codex", "sessions", oldSessionDay.ToString("yyyy"),
                oldSessionDay.ToString("MM"), oldSessionDay.ToString("dd"));
            var cursor = Path.Combine(root, ".cursor", "projects", "demo", "agent-transcripts", "abc");
            Directory.CreateDirectory(claude);
            Directory.CreateDirectory(codex);
            Directory.CreateDirectory(cursor);
            var workingCopy = Path.Combine(root, "workspace", "sample-workspace");
            var dotGit = Path.Combine(workingCopy, ".git");
            Directory.CreateDirectory(dotGit);
            File.WriteAllText(Path.Combine(dotGit, "config"),
                "[remote \"origin\"]\n\turl = https://github.com/example/codex-project.git\n");
            var timestamp = DateTimeOffset.Now.ToString("O");
            var claudeLine = JsonSerializer.Serialize(new
            {
                type = "assistant",
                timestamp,
                cwd = @"C:\work\claude-demo",
                message = new
                {
                    id = "msg-1",
                    model = "claude-test",
                    usage = new { input_tokens = 100, cache_read_input_tokens = 25, output_tokens = 30 },
                    content = new[] { new { type = "text", text = "Claude 完成测试" } },
                },
            });
            File.WriteAllText(Path.Combine(claude, "session.jsonl"), claudeLine + Environment.NewLine);

            var codexLines = new[]
            {
                JsonSerializer.Serialize(new
                {
                    timestamp,
                    type = "session_meta",
                    payload = new
                    {
                        type = "session_meta",
                        id = "primary-session",
                        cwd = workingCopy,
                        thread_source = "user",
                    },
                }),
                new string('x', 600_000),
                JsonSerializer.Serialize(new
                {
                    timestamp,
                    type = "event_msg",
                    payload = new
                    {
                        type = "token_count",
                        info = new { last_token_usage = new { input_tokens = 200, output_tokens = 40 } },
                        rate_limits = new
                        {
                            primary = new
                            {
                                used_percent = 60,
                                resets_at = 1893456000,
                                window_minutes = 10080,
                            },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    timestamp,
                    type = "event_msg",
                    payload = new { type = "agent_message", message = "Codex 完成测试" },
                }),
                JsonSerializer.Serialize(new
                {
                    timestamp,
                    type = "event_msg",
                    payload = new { type = "task_started" },
                }),
                JsonSerializer.Serialize(new
                {
                    timestamp,
                    type = "event_msg",
                    payload = new
                    {
                        type = "agent_message",
                        message = "Codex 正在执行工具",
                        phase = "commentary",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    timestamp,
                    type = "response_item",
                    payload = new { type = "custom_tool_call", name = "exec", input = "{}" },
                }),
            };
            var primaryCodexPath = Path.Combine(codex, "rollout.jsonl");
            File.WriteAllText(primaryCodexPath,
                string.Join(Environment.NewLine, codexLines) + Environment.NewLine);
            var secondCodexLines = new[]
            {
                JsonSerializer.Serialize(new
                {
                    timestamp,
                    type = "session_meta",
                    payload = new
                    {
                        type = "session_meta",
                        id = "second-session",
                        cwd = @"C:\work\codex-second",
                        thread_source = "subagent",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    timestamp,
                    type = "event_msg",
                    payload = new
                    {
                        type = "token_count",
                        info = new { last_token_usage = new { input_tokens = 100, output_tokens = 10 } },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    timestamp,
                    type = "event_msg",
                    payload = new { type = "agent_message", message = "第二个 Codex 会话" },
                }),
            };
            var secondCodexPath = Path.Combine(codex, "rollout-second.jsonl");
            File.WriteAllText(secondCodexPath,
                string.Join(Environment.NewLine, secondCodexLines) + Environment.NewLine);
            var staleCodexPath = Path.Combine(codex, "rollout-stale.jsonl");
            File.WriteAllText(staleCodexPath,
                string.Join(Environment.NewLine, secondCodexLines) + Environment.NewLine);
            var guardianCodexLines = new[]
            {
                JsonSerializer.Serialize(new
                {
                    timestamp,
                    type = "session_meta",
                    payload = new
                    {
                        type = "session_meta",
                        id = "guardian-session",
                        parent_thread_id = "primary-session",
                        cwd = workingCopy,
                        thread_source = "guardian_review",
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    timestamp,
                    type = "event_msg",
                    payload = new
                    {
                        type = "agent_message",
                        message = "{\"risk_level\":\"low\",\"outcome\":\"allow\"}",
                    },
                }),
            };
            var guardianCodexPath = Path.Combine(codex, "rollout-guardian.jsonl");
            File.WriteAllText(guardianCodexPath,
                string.Join(Environment.NewLine, guardianCodexLines) + Environment.NewLine);
            var codexIndex = Path.Combine(root, ".codex", "session_index.jsonl");
            File.WriteAllText(codexIndex, JsonSerializer.Serialize(new
            {
                id = "primary-session",
                thread_name = "Sample Codex Session",
                updated_at = timestamp,
            }) + Environment.NewLine);
            File.SetLastWriteTime(primaryCodexPath, DateTime.Now.AddMinutes(-20));
            File.SetLastWriteTime(secondCodexPath, DateTime.Now);
            File.SetLastWriteTime(staleCodexPath, DateTime.Now.AddDays(-30));
            File.SetLastWriteTime(guardianCodexPath, DateTime.Now);

            var cursorLines = new[]
            {
                JsonSerializer.Serialize(new
                {
                    role = "assistant",
                    message = new
                    {
                        model = "cursor-test",
                        content = new[] { new { type = "text", text = "Cursor 完成测试" } },
                    },
                }),
                JsonSerializer.Serialize(new { type = "turn_ended", status = "completed" }),
            };
            File.WriteAllText(Path.Combine(cursor, "abc.jsonl"),
                string.Join(Environment.NewLine, cursorLines) + Environment.NewLine);

            var agents = new AgentUsageCollector(root).Collect();
            Assert(agents.Claude.TodayInputTokens == 125 && agents.Claude.TodayOutputTokens == 30,
                "Claude token parser");
            Assert(agents.Claude.Activity?.Contains("完成测试", StringComparison.Ordinal) == true,
                "Claude activity parser");
            Assert(agents.Codex.TodayInputTokens == 200 && agents.Codex.TodayOutputTokens == 40,
                "Codex token parser");
            Assert(agents.Codex.QuotaUsedPercent == 60, "Codex quota parser");
            Assert(agents.Codex.QuotaWindowMinutes == 10_080, "Codex quota window parser");
            Assert(agents.Codex.IsWorking && !agents.Codex.NeedsAttention,
                "Codex child activity keeps a running parent conversation visible");
            Assert(agents.CodexSessions?.Count == 2 &&
                   agents.CodexSessions[0].Project == "Sample Codex Session" &&
                   agents.CodexSessions[0].Workspace == "codex-project" &&
                   agents.CodexSessions[0].SessionId == "primary-session" &&
                   agents.CodexSessions[0].LastActive > DateTime.Now.AddMinutes(-1) &&
                   agents.CodexSessions[1].Project == "codex-second" &&
                   agents.CodexSessions[1].NeedsAttention,
                "Codex titles and child activity are merged while stale files stay excluded");
            Assert(AgentUsageCollector.IsVisibleCodexThreadSource("user") &&
                   AgentUsageCollector.IsVisibleCodexThreadSource("subagent") &&
                   !AgentUsageCollector.IsVisibleCodexThreadSource("guardian_review") &&
                   !AgentUsageCollector.IsVisibleCodexThreadSource(null),
                "Codex user/subagent source allowlist");
            Assert(agents.Cursor.Activity?.Contains("完成测试", StringComparison.Ordinal) == true,
                "Cursor activity parser");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static void TestDynamicPanelResolver()
    {
        var now = DateTime.Now;
        AgentSnapshot Agent(AgentKind kind, string id, int minutesAgo = 0) => new(
            kind, true, 10, 2, now.AddMinutes(-minutesAgo), id, "测试活动",
            minutesAgo == 0, false, null, "test", null, null, null,
            null, null, kind != AgentKind.Cursor, id);

        var codex = Agent(AgentKind.Codex, "codex");
        var codexSecond = Agent(AgentKind.Codex, "codex-second");
        var claude = Agent(AgentKind.Claude, "claude");
        var cursor = Agent(AgentKind.Cursor, "cursor");
        var four = new AgentsSnapshot(claude, codex, cursor, [codex, codexSecond]);
        var panels = new DynamicPanelResolver().Resolve(four, now);
        Assert(panels.Count == 4 && panels.All(panel => panel.Kind == DashboardPanelKind.Agent),
            "four active agents fill dynamic slots");

        var three = new AgentsSnapshot(claude, codex,
            AgentSnapshot.Missing(AgentKind.Cursor), [codex, codexSecond]);
        panels = new DynamicPanelResolver().Resolve(three, now);
        Assert(panels.Select(panel => panel.Kind).SequenceEqual(
                [DashboardPanelKind.Agent, DashboardPanelKind.Agent, DashboardPanelKind.Agent,
                 DashboardPanelKind.Markets]),
            "three agents add markets");

        var two = new AgentsSnapshot(claude, codex,
            AgentSnapshot.Missing(AgentKind.Cursor), [codex]);
        panels = new DynamicPanelResolver().Resolve(two, now);
        Assert(panels.Select(panel => panel.Kind).SequenceEqual(
                [DashboardPanelKind.Agent, DashboardPanelKind.Agent, DashboardPanelKind.Markets,
                 DashboardPanelKind.News]),
            "two agents add markets and news");

        var one = new AgentsSnapshot(AgentSnapshot.Missing(AgentKind.Claude), codex,
            AgentSnapshot.Missing(AgentKind.Cursor), [codex]);
        panels = new DynamicPanelResolver().Resolve(one, now);
        Assert(panels.Select(panel => panel.Kind).SequenceEqual(
                [DashboardPanelKind.Agent, DashboardPanelKind.Markets, DashboardPanelKind.News,
                 DashboardPanelKind.AiQuota]),
            "one agent adds markets news and AI quota");

        panels = new DynamicPanelResolver().Resolve(AgentsSnapshot.Empty, now);
        Assert(panels.Select(panel => panel.Kind).SequenceEqual(
                [DashboardPanelKind.Markets, DashboardPanelKind.News, DashboardPanelKind.AiQuota,
                 DashboardPanelKind.Forecast]),
            "no agents adds markets news AI quota and forecast");

        var expired = Agent(AgentKind.Codex, "expired", 11) with { IsWorking = false };
        panels = new DynamicPanelResolver().Resolve(new AgentsSnapshot(
            AgentSnapshot.Missing(AgentKind.Claude), expired,
            AgentSnapshot.Missing(AgentKind.Cursor), [expired]), now);
        Assert(panels.All(panel => panel.Kind != DashboardPanelKind.Agent),
            "inactive agents leave the dashboard after ten minutes");

        var stableResolver = new DynamicPanelResolver(replacementCooldown: TimeSpan.FromSeconds(30));
        var first = Agent(AgentKind.Codex, "first", 1) with { IsWorking = false };
        var second = Agent(AgentKind.Codex, "second", 2) with { IsWorking = false };
        var third = Agent(AgentKind.Codex, "third", 3) with { IsWorking = false };
        var fourth = Agent(AgentKind.Codex, "fourth", 4) with { IsWorking = false };
        var newcomer = Agent(AgentKind.Codex, "newcomer", 5) with { IsWorking = false };
        var five = new AgentsSnapshot(AgentSnapshot.Missing(AgentKind.Claude), first,
            AgentSnapshot.Missing(AgentKind.Cursor), [first, second, third, fourth, newcomer]);
        stableResolver.Resolve(five, now);
        var promoted = newcomer with { IsWorking = true, LastActive = now.AddSeconds(1) };
        five = five with { CodexSessions = [first, second, third, fourth, promoted] };
        panels = stableResolver.Resolve(five, now.AddSeconds(1));
        Assert(panels.All(panel => panel.Agent?.SessionId != "newcomer"),
            "agent replacement waits for cooldown");
        panels = stableResolver.Resolve(five, now.AddSeconds(31));
        Assert(panels.Any(panel => panel.Agent?.SessionId == "newcomer"),
            "agent replacement occurs after cooldown");
    }

    private static void TestMarketParsing()
    {
        const string yahoo = """
            {"chart":{"result":[{"meta":{"regularMarketPrice":159.2,"chartPreviousClose":158.5,
            "regularMarketTime":1893456000,"currentTradingPeriod":{"regular":{"start":1,"end":2}}},
            "indicators":{"quote":[{"close":[158.5,159.0,159.2]}]}}]}}
            """;
        var stock = MarketDataCollector.ParseYahooQuote(yahoo, "BGBL");
        Assert(stock.Symbol == "BGBL" && stock.Price == 159.2m && stock.ChangePercent > 0 &&
               stock.History?.SequenceEqual([158.5m, 159.0m, 159.2m]) == true,
            "Yahoo quote parser");

        const string okxAud = """
            {"code":"0","data":[{"instId":"USDT-AUD","last":"1.4053","open24h":"1.4136",
            "high24h":"1.4172","low24h":"1.4021","ts":"1893456000000"}]}
            """;
        var audPair = MarketDataCollector.ParseOkxQuote(okxAud, "USDT/AUD");
        Assert(audPair.Symbol == "USDT/AUD" && audPair.Source == "OKX" &&
               audPair.Currency == "AUD" && audPair.ChangePercent < 0 &&
               DashboardRenderer.FormatMarketPrice(audPair.Price, true, 0.0001m, audPair.Symbol) == "1.4053",
            "OKX USDT/AUD quote parser");

        const string okx = """
            {"code":"0","data":[{"instId":"BTC-USDT","last":"62400.0","open24h":"60000.0",
            "high24h":"63000.0","low24h":"59000.0","ts":"1893456000000"}]}
            """;
        var crypto = MarketDataCollector.ParseOkxQuote(okx, "BTC");
        Assert(crypto.Price == 62400m && crypto.ChangePercent == 4m && crypto.IsLive &&
               crypto.Source == "OKX" && crypto.Currency == "USDT", "OKX quote parser");

        const string okxCandles = """
            {"code":"0","data":[
              ["1893459600000","62000","62600","61800","62400","100","1","1","0"],
              ["1893456000000","61500","62100","61200","62000","100","1","1","1"],
              ["1893452400000","61000","61600","60800","61500","100","1","1","1"]
            ]}
            """;
        var hourly = MarketDataCollector.ParseOkxCandles(okxCandles);
        Assert(hourly.SequenceEqual([61_500m, 62_000m, 62_400m]),
            "OKX 1H candles are ordered from oldest to newest");

        const string okxInstruments = """
            {"code":"0","data":[
              {"instId":"USDT-AUD","state":"live","tickSz":"0.00010000"},
              {"instId":"BTC-USDT","state":"live","tickSz":"0.10000000"},
              {"instId":"ETH-USDT","state":"live","tickSz":"0.01000000"},
              {"instId":"WLFI-USDT","state":"live","tickSz":"0.00010000"}
            ]}
            """;
        var ticks = MarketDataCollector.ParseOkxPriceTicks(okxInstruments);
        Assert(ticks["USDT/AUD"] == 0.0001m && ticks["BTC"] == 0.1m &&
               ticks["ETH"] == 0.01m && ticks["WLFI"] == 0.0001m,
            "OKX price precision parser");

        const string binance = """
            {"symbol":"BTCUSDT","lastPrice":"62483.00","priceChangePercent":"3.50","closeTime":1893456000000}
            """;
        var binanceFallback = MarketDataCollector.ParseBinanceQuote(binance, "BTC");
        Assert(binanceFallback.Price == 62483m && binanceFallback.ChangePercent == 3.50m &&
               binanceFallback.Source == "Binance" && binanceFallback.Currency == "USDT",
            "Binance fallback quote parser");

        const string exchangeInfo = """
            {"symbols":[
              {"symbol":"BTCUSDT","filters":[{"filterType":"PRICE_FILTER","tickSize":"0.10000000"}]},
              {"symbol":"ETHUSDT","filters":[{"filterType":"PRICE_FILTER","tickSize":"0.01000000"}]},
              {"symbol":"WLFIUSDT","filters":[{"filterType":"PRICE_FILTER","tickSize":"0.00010000"}]}
            ]}
            """;
        var binanceTicks = MarketDataCollector.ParseBinancePriceTicks(exchangeInfo);
        Assert(binanceTicks["BTC"] == 0.1m && binanceTicks["ETH"] == 0.01m &&
               binanceTicks["WLFI"] == 0.0001m, "Binance fallback precision parser");
        Assert(DashboardRenderer.FormatMarketPrice(159.2m, false) == "A$159.20",
            "ASX prices use two decimals");
        Assert(DashboardRenderer.FormatMarketPrice(62483.27m, true, ticks["BTC"]) == "$62,483.3",
            "crypto price follows exchange tick size");
        Assert(DashboardRenderer.FormatMarketPrice(0.054632m, true, ticks["WLFI"]) == "$0.0546",
            "low-priced crypto follows exchange tick size");

        const string coinGecko = """
            {"bitcoin":{"usd":62483,"usd_24h_change":3.5,"last_updated_at":1893456000},
             "ethereum":{"usd":1855,"usd_24h_change":3.3,"last_updated_at":1893456000},
             "world-liberty-financial":{"usd":0.0546,"usd_24h_change":-0.8,"last_updated_at":1893456000}}
            """;
        var fallback = MarketDataCollector.ParseCoinGeckoQuotes(coinGecko);
        Assert(fallback.Count == 3 && fallback.Any(quote => quote.Symbol == "WLFI") &&
               fallback.All(quote => quote.Source == "CoinGecko"),
            "CoinGecko fallback parser");

        var quoteNow = DateTime.Now;
        var current = new MarketQuote("BTC", 100m, 0m, "USDT", quoteNow, true, Source: "OKX");
        var older = new MarketQuote("BTC", 90m, 0m, "USDT", quoteNow.AddSeconds(-1), true,
            Source: "OKX");
        var merged = MarketDataCollector.MergeQuotes(["BTC"], [current], [older]);
        Assert(merged.Count == 1 && merged[0].Price == 100m,
            "market quotes reject timestamp regression");
    }

    private static void TestAsxMarketStatus()
    {
        var now = DateTime.Now;
        var open = new MarketQuote("BGBL", 160m, 0.2m, "AUD", now, false, true,
            Source: "Yahoo");
        var closed = open with { Symbol = "IVV", MarketOpen = false };

        Assert(DashboardRenderer.ResolveAsxMarketStatus([]) == "UNKNOWN",
            "ASX status is unknown before quotes arrive");
        Assert(DashboardRenderer.ResolveAsxMarketStatus([open, open with { Symbol = "NDQ" }]) ==
               "LIVE", "ASX open status");
        Assert(DashboardRenderer.ResolveAsxMarketStatus([closed, closed with { Symbol = "IBIT" }]) ==
               "CLOSED", "ASX closed status");
        Assert(DashboardRenderer.ResolveAsxMarketStatus([closed, open]) == "LIVE",
            "ASX status tolerates one delayed quote during a session transition");
    }

    private static void TestNewsParsing()
    {
        const string json = """
            {"items":[{"title":"AI 新闻测试","category":"前沿科技","importance":5,
            "sources":["a","b"],"published_at":"2026-08-17T08:00:00Z","url":"/cluster/test.html"}]}
            """;
        var fromJson = NewsDataCollector.ParseJson(json, "https://textradar.app/");
        Assert(fromJson.Count == 1 && fromJson[0].Importance == 5 &&
               fromJson[0].SourceCount == 2 && fromJson[0].Url.Contains("cluster/test.html"),
            "TextRadar JSON parser");

        const string html = """
            <li class="cluster cat-frontier_tech has-detail">
              <time datetime="2026-08-17T08:03:36Z">08-17 18:03</time>
              <span class="cl-cat">前沿科技</span>
              <a class="cl-title" href="cluster/demo.html">TextRadar &amp; LCD 联动</a>
              <span class="cl-stars" data-stars="4">★★★★</span>
            </li>
            """;
        var fromHtml = NewsDataCollector.ParseHtml(html, "https://textradar.app/");
        Assert(fromHtml.Count == 1 && fromHtml[0].Title.Contains("TextRadar & LCD") &&
               fromHtml[0].Importance == 4, "TextRadar HTML fallback parser");
    }

    private static void TestWeatherParsing()
    {
        var future = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd'T'HH:00", System.Globalization.CultureInfo.InvariantCulture);
        var later = DateTime.Now.AddHours(4).ToString("yyyy-MM-dd'T'HH:00", System.Globalization.CultureInfo.InvariantCulture);
        var json = $$$"""
            {"current":{"time":"{{{future}}}","temperature_2m":19.2,"relative_humidity_2m":64,
            "apparent_temperature":18.1,"precipitation":0.2,"weather_code":2,
            "wind_speed_10m":13.4,"wind_direction_10m":70},
            "hourly":{"time":["{{{future}}}","{{{later}}}"],"temperature_2m":[19.2,17.1],
            "precipitation_probability":[20,30],"wind_speed_10m":[13.4,16.1],
            "wind_direction_10m":[70,82]},
            "daily":{"temperature_2m_max":[22.0],"temperature_2m_min":[14.0],
            "precipitation_probability_max":[30]}}
            """;
        var weather = WeatherDataCollector.ParseJson(json, "Sydney");
        Assert(weather.Location == "Sydney" && weather.Temperature == 19.2 &&
               weather.TodayMaximum == 22 && weather.Hours.Count > 0 &&
               weather.Hours[0].WindSpeed == 13.4,
            "Open-Meteo parser");
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Self-test assertion failed: {name}");
        }
    }
}
