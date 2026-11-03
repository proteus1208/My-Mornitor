using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using NjcAI.Monitor.Core;

namespace NjcAI.Monitor.Services;

internal sealed class AgentUsageCollector
{
    private enum CodexTurnState
    {
        Unknown,
        Running,
        Completed,
    }

    private sealed class FileProgress
    {
        public long Offset;
        public ulong Input;
        public ulong Output;
    }

    private sealed record CodexSessionMetadata(
        string? Id,
        string? ParentThreadId,
        string? ThreadSource);

    private readonly string _home;
    private readonly Dictionary<string, FileProgress> _claudeProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileProgress> _codexProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _codexSessionVisibility = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CodexSessionMetadata> _codexSessionMetadata =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _codexThreadNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _repositoryProjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _claudeMessageIds = new(StringComparer.Ordinal);
    private DateTime _codexThreadIndexWriteTimeUtc;
    private long _codexThreadIndexLength = -1;
    private DateOnly _progressDay = DateOnly.FromDateTime(DateTime.Now);

    public AgentUsageCollector(string? home = null)
    {
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public AgentsSnapshot Collect()
    {
        ResetAtDayBoundary();
        var codexSessions = CollectCodexSessions();
        var primaryCodex = codexSessions.FirstOrDefault() ?? AgentSnapshot.Missing(AgentKind.Codex);
        return new AgentsSnapshot(
            CollectClaude(),
            primaryCodex,
            CollectCursor(),
            codexSessions);
    }

    private AgentSnapshot CollectClaude()
    {
        var root = Path.Combine(_home, ".claude", "projects");
        if (!Directory.Exists(root))
        {
            return AgentSnapshot.Missing(AgentKind.Claude);
        }

        var files = EnumerateJsonl(root).ToArray();
        var latest = Latest(files);
        UpdateTokenProgress(files, _claudeProgress, ParseClaudeUsage);
        var (input, output) = SumProgress(_claudeProgress, files);
        if (latest is null)
        {
            return AgentSnapshot.Missing(AgentKind.Claude) with { Available = true };
        }

        var lines = ReadTailLines(latest.FullName);
        string? project = null;
        string? activity = null;
        string? model = null;
        string? waitingFor = null;
        (int Current, int Total, string Text)? step = null;
        var lastKind = "";

        foreach (var line in lines)
        {
            if (!TryParse(line, out var rootElement))
            {
                continue;
            }

            project = FindString(rootElement, "cwd") is { Length: > 0 } cwd
                ? Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : project;
            var type = GetString(rootElement, "type");
            if (type == "assistant" && TryGet(rootElement, "message", out var message))
            {
                model = GetString(message, "model") ?? model;
                if (TryGet(message, "content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    var usedTool = false;
                    foreach (var block in content.EnumerateArray())
                    {
                        var blockType = GetString(block, "type");
                        if (blockType == "text")
                        {
                            activity = Clean(GetString(block, "text"));
                        }
                        else if (blockType == "tool_use")
                        {
                            usedTool = true;
                            var name = GetString(block, "name");
                            if (name == "TodoWrite" && TryGet(block, "input", out var planInput))
                            {
                                step = ParseTodoPlan(planInput) ?? step;
                            }
                        }
                    }
                    lastKind = usedTool ? "tool" : "assistant";
                }
            }
            else if (type == "user")
            {
                lastKind = "user";
            }
        }

        var sessionId = Path.GetFileNameWithoutExtension(latest.Name);
        var live = ReadClaudeLiveState(sessionId);
        var age = DateTime.Now - latest.LastWriteTime;
        var working = live.Status is "busy" or "shell" ||
                      (live.Status is null && age.TotalSeconds < 90 && lastKind is "tool" or "user");
        var attention = age.TotalMinutes < 10 &&
                        (live.Status == "waiting" || live.Status is null && lastKind == "assistant");
        waitingFor = live.WaitingFor;

        return new AgentSnapshot(
            AgentKind.Claude, true, input, output, latest.LastWriteTime,
            project ?? ProjectFromClaudePath(latest), activity, working, attention,
            waitingFor, model, step?.Current, step?.Total, step?.Text, null, null, true,
            sessionId);
    }

    private IReadOnlyList<AgentSnapshot> CollectCodexSessions()
    {
        var root = Path.Combine(_home, ".codex", "sessions");
        if (!Directory.Exists(root))
        {
            return [];
        }

        RefreshCodexThreadNames();
        var allFiles = EnumerateRecentCodexFiles(root).ToArray();
        var metadataByPath = allFiles.ToDictionary(
            file => file.FullName,
            ReadCodexSessionMetadata,
            StringComparer.OrdinalIgnoreCase);
        var childActivityByParent = allFiles
            .Select(file => (File: file, Metadata: metadataByPath[file.FullName]))
            .Where(item => !IsVisibleCodexThreadSource(item.Metadata.ThreadSource) &&
                           !string.IsNullOrWhiteSpace(item.Metadata.ParentThreadId))
            .GroupBy(item => item.Metadata.ParentThreadId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Max(item => item.File.LastWriteTime),
                StringComparer.OrdinalIgnoreCase);
        var files = allFiles
            .Where(file => IsVisibleCodexSession(file))
            .Select(file =>
            {
                var metadata = metadataByPath[file.FullName];
                var lastActive = file.LastWriteTime;
                if (!string.IsNullOrWhiteSpace(metadata.Id) &&
                    childActivityByParent.TryGetValue(metadata.Id, out var childActivity) &&
                    childActivity > lastActive)
                {
                    lastActive = childActivity;
                }
                return (File: file, Metadata: metadata, LastActive: lastActive);
            })
            .OrderByDescending(item => item.LastActive)
            .ToArray();
        UpdateTokenProgress(files.Select(item => item.File).ToArray(), _codexProgress, ParseCodexUsage);
        if (files.Length == 0)
        {
            return [];
        }

        var sessions = files.Take(12)
            .Select(item => ParseCodexSession(item.File, item.Metadata, item.LastActive))
            .OrderByDescending(session => session.IsWorking)
            .ThenByDescending(session => session.NeedsAttention)
            .ThenByDescending(session => session.LastActive)
            .Take(6)
            .ToList();
        return sessions;
    }

    private AgentSnapshot ParseCodexSession(
        FileInfo file,
        CodexSessionMetadata metadata,
        DateTime lastActive)
    {
        var lines = ReadTailLines(file.FullName);
        var threadTitle = metadata.Id is not null && _codexThreadNames.TryGetValue(metadata.Id, out var threadName)
            ? threadName
            : null;
        var workspace = ReadCodexProject(file);
        var project = threadTitle ?? workspace;
        string? activity = null;
        string? model = null;
        (int Current, int Total, string Text)? step = null;
        double? quota = null;
        DateTime? reset = null;
        int? quotaWindowMinutes = null;
        var turnState = CodexTurnState.Unknown;

        foreach (var line in lines)
        {
            if (!TryParse(line, out var eventRoot) || !TryGet(eventRoot, "payload", out var payload))
            {
                continue;
            }

            var eventType = GetString(eventRoot, "type");
            var payloadType = GetString(payload, "type");
            if (eventType == "session_meta" || payloadType == "session_meta")
            {
                workspace = ProjectFromCodexMetadata(payload) ?? workspace;
                project = threadTitle ?? workspace ?? project;
            }
            if (payloadType == "turn_context")
            {
                model = GetString(payload, "model") ?? model;
            }
            if (payloadType == "agent_message")
            {
                activity = Clean(GetString(payload, "message")) ?? activity;
                var phase = GetString(payload, "phase");
                turnState = string.IsNullOrWhiteSpace(phase) ||
                            string.Equals(phase, "final_answer", StringComparison.OrdinalIgnoreCase)
                    ? CodexTurnState.Completed
                    : CodexTurnState.Running;
            }
            else if (eventType == "response_item" && payloadType == "message" &&
                     GetString(payload, "role") == "assistant")
            {
                var text = ExtractContentText(payload);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    activity = Clean(text);
                }
                if (string.Equals(GetString(payload, "phase"), "final_answer", StringComparison.OrdinalIgnoreCase))
                {
                    turnState = CodexTurnState.Completed;
                }
                else if (!string.IsNullOrWhiteSpace(GetString(payload, "phase")))
                {
                    turnState = CodexTurnState.Running;
                }
            }
            else if (IsCodexTerminalEvent(payloadType))
            {
                turnState = CodexTurnState.Completed;
            }
            else if (IsCodexRunningEvent(eventType, payloadType))
            {
                turnState = CodexTurnState.Running;
                if (payloadType is "function_call" or "custom_tool_call" &&
                    GetString(payload, "name") == "update_plan")
                {
                    var arguments = GetString(payload, "arguments") ?? GetString(payload, "input");
                    step = ParseCodexPlan(arguments) ?? step;
                }
            }

            if (payloadType == "token_count" && TryGet(payload, "rate_limits", out var limits))
            {
                var parsedQuota = ParseRateLimit(limits);
                quota = parsedQuota.Used ?? quota;
                reset = parsedQuota.Reset ?? reset;
                quotaWindowMinutes = parsedQuota.WindowMinutes ?? quotaWindowMinutes;
            }
        }

        var age = DateTime.Now - lastActive;
        var working = turnState == CodexTurnState.Running && age.TotalMinutes < 30 ||
                      turnState == CodexTurnState.Unknown && age.TotalSeconds < 90;
        var attention = turnState == CodexTurnState.Completed && age.TotalMinutes < 10;
        _codexProgress.TryGetValue(file.FullName, out var usage);
        return new AgentSnapshot(
            AgentKind.Codex, true, usage?.Input ?? 0, usage?.Output ?? 0, lastActive,
            project ?? "Codex Session", activity, working, attention, null, model,
            step?.Current, step?.Total, step?.Text, quota, reset, true,
            metadata.Id ?? Path.GetFileNameWithoutExtension(file.Name), quotaWindowMinutes,
            Workspace: workspace);
    }

    private static bool IsCodexRunningEvent(string? eventType, string? payloadType)
    {
        if (eventType == "turn_context" ||
            payloadType is "task_started" or "user_message" or "agent_reasoning" or "reasoning")
        {
            return true;
        }
        return eventType == "response_item" && payloadType is not null &&
               (payloadType.Contains("call", StringComparison.OrdinalIgnoreCase) ||
                payloadType.Contains("tool", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCodexTerminalEvent(string? payloadType) => payloadType is
        "task_complete" or "task_aborted" or "task_cancelled" or "task_canceled" or
        "turn_complete" or "turn_aborted" or "turn_ended";

    private AgentSnapshot CollectCursor()
    {
        var root = Path.Combine(_home, ".cursor", "projects");
        if (!Directory.Exists(root))
        {
            return AgentSnapshot.Missing(AgentKind.Cursor);
        }

        var files = EnumerateJsonl(root)
            .Where(file => file.FullName.Contains($"{Path.DirectorySeparatorChar}agent-transcripts{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var latest = Latest(files);
        if (latest is null)
        {
            return AgentSnapshot.Missing(AgentKind.Cursor) with { Available = true };
        }

        var lines = ReadTailLines(latest.FullName);
        string? project = null;
        string? activity = null;
        string? model = null;
        (int Current, int Total, string Text)? step = null;
        var turnEnded = false;
        foreach (var line in lines)
        {
            if (!TryParse(line, out var item))
            {
                continue;
            }
            var type = GetString(item, "type");
            if (type == "turn_ended")
            {
                turnEnded = true;
                continue;
            }
            var role = GetString(item, "role");
            if (role == "assistant")
            {
                turnEnded = false;
                if (TryGet(item, "message", out var message))
                {
                    model = GetString(message, "model") ?? model;
                    var text = ExtractContentText(message);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        activity = Clean(text);
                    }
                    if (TryGet(message, "content", out var content) && content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var block in content.EnumerateArray())
                        {
                            if (GetString(block, "type") == "tool_use" &&
                                GetString(block, "name") == "TodoWrite" &&
                                TryGet(block, "input", out var input))
                            {
                                step = ParseTodoPlan(input) ?? step;
                            }
                        }
                    }
                }
            }
            project = FindString(item, "cwd") is { Length: > 0 } cwd
                ? Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : project;
        }

        project ??= ProjectFromCursorPath(latest, root);
        var age = DateTime.Now - latest.LastWriteTime;
        return new AgentSnapshot(
            AgentKind.Cursor, true, 0, 0, latest.LastWriteTime,
            project, activity, age.TotalSeconds < 90 && !turnEnded,
            age.TotalMinutes < 10 && turnEnded, null, model,
            step?.Current, step?.Total, step?.Text, null, null, false,
            Path.GetFileNameWithoutExtension(latest.Name));
    }

    private void UpdateTokenProgress(
        IReadOnlyList<FileInfo> files,
        IDictionary<string, FileProgress> progress,
        Func<JsonElement, (ulong Input, ulong Output, string? Id)> parser)
    {
        var today = DateTime.Today;
        foreach (var file in files.Where(file => file.LastWriteTime >= today))
        {
            if (!progress.TryGetValue(file.FullName, out var state))
            {
                state = new FileProgress();
                progress[file.FullName] = state;
            }
            if (file.Length < state.Offset)
            {
                state.Offset = 0;
                state.Input = 0;
                state.Output = 0;
            }
            if (file.Length == state.Offset)
            {
                continue;
            }

            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(state.Offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024, true);
                while (reader.ReadLine() is { } line)
                {
                    if (!TryParse(line, out var element))
                    {
                        continue;
                    }
                    var usage = parser(element);
                    if (usage.Id is not null && !_claudeMessageIds.Add(usage.Id))
                    {
                        continue;
                    }
                    state.Input += usage.Input;
                    state.Output += usage.Output;
                }
                state.Offset = stream.Position;
            }
            catch (Exception exception)
            {
                Log.Warn($"Agent transcript read failed ({file.Name}): {exception.Message}");
            }
        }
    }

    private static (ulong Input, ulong Output, string? Id) ParseClaudeUsage(JsonElement root)
    {
        if (GetString(root, "type") != "assistant" || !TryGet(root, "message", out var message) ||
            !TryGet(message, "usage", out var usage))
        {
            return (0, 0, null);
        }
        var input = GetUInt64(usage, "input_tokens") + GetUInt64(usage, "cache_creation_input_tokens") +
                    GetUInt64(usage, "cache_read_input_tokens");
        return (input, GetUInt64(usage, "output_tokens"), GetString(message, "id"));
    }

    private static (ulong Input, ulong Output, string? Id) ParseCodexUsage(JsonElement root)
    {
        if (!TryGet(root, "payload", out var payload) || GetString(payload, "type") != "token_count" ||
            !TryGet(payload, "info", out var info) || !TryGet(info, "last_token_usage", out var usage))
        {
            return (0, 0, null);
        }
        return (
            GetUInt64(usage, "input_tokens") + GetUInt64(usage, "cache_write_input_tokens"),
            GetUInt64(usage, "output_tokens"),
            null);
    }

    private (string? Status, string? WaitingFor) ReadClaudeLiveState(string sessionId)
    {
        var root = Path.Combine(_home, ".claude", "sessions");
        if (!Directory.Exists(root))
        {
            return (null, null);
        }
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.json"))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var item = document.RootElement;
                if (GetString(item, "sessionId") != sessionId)
                {
                    continue;
                }
                if (TryGet(item, "pid", out var pidValue) && pidValue.TryGetInt32(out var pid))
                {
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        if (process.HasExited)
                        {
                            return (null, null);
                        }
                    }
                    catch
                    {
                        return (null, null);
                    }
                }
                return (GetString(item, "status"), GetString(item, "waitingFor"));
            }
        }
        catch
        {
            // Live state is an optional enhancement.
        }
        return (null, null);
    }

    private static (int Current, int Total, string Text)? ParseTodoPlan(JsonElement input)
    {
        if (!TryGet(input, "todos", out var todos) || todos.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var items = todos.EnumerateArray().Select(todo => (
            Text: GetString(todo, "activeForm") ?? GetString(todo, "content") ?? string.Empty,
            Status: GetString(todo, "status") ?? "pending")).ToArray();
        if (items.Length == 0)
        {
            return null;
        }
        var index = Array.FindIndex(items, item => item.Status == "in_progress");
        if (index < 0)
        {
            index = Array.FindIndex(items, item => item.Status != "completed");
        }
        if (index < 0)
        {
            index = items.Length - 1;
        }
        return (index + 1, items.Length, Clean(items[index].Text) ?? string.Empty);
    }

    private static (int Current, int Total, string Text)? ParseCodexPlan(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(arguments);
            if (!TryGet(document.RootElement, "plan", out var plan) || plan.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var items = plan.EnumerateArray().Select(item => (
                Text: GetString(item, "step") ?? string.Empty,
                Status: GetString(item, "status") ?? "pending")).ToArray();
            if (items.Length == 0)
            {
                return null;
            }
            var index = Array.FindIndex(items, item => item.Status == "in_progress");
            if (index < 0)
            {
                index = Array.FindIndex(items, item => item.Status != "completed");
            }
            if (index < 0)
            {
                index = items.Length - 1;
            }
            return (index + 1, items.Length, Clean(items[index].Text) ?? string.Empty);
        }
        catch
        {
            return null;
        }
    }

    private static (double? Used, DateTime? Reset, int? WindowMinutes) ParseRateLimit(JsonElement limits)
    {
        if (!TryGet(limits, "primary", out var primary) &&
            !TryGet(limits, "primary_window", out primary))
        {
            return (null, null, null);
        }
        double? used = null;
        if (TryGet(primary, "used_percent", out var usedValue) && usedValue.TryGetDouble(out var number))
        {
            used = number;
        }
        DateTime? reset = null;
        if (TryGet(primary, "resets_at", out var resetValue) || TryGet(primary, "reset_at", out resetValue))
        {
            if (resetValue.TryGetDouble(out var epoch))
            {
                reset = DateTimeOffset.FromUnixTimeSeconds((long)epoch).LocalDateTime;
            }
        }
        int? windowMinutes = null;
        if ((TryGet(primary, "window_minutes", out var windowValue) ||
             TryGet(primary, "windowMinutes", out windowValue)) &&
            windowValue.TryGetInt32(out var minutes))
        {
            windowMinutes = minutes;
        }
        return (used, reset, windowMinutes);
    }

    private static string? ExtractContentText(JsonElement value)
    {
        if (!TryGet(value, "content", out var content))
        {
            return GetString(value, "text") ?? GetString(value, "message");
        }
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }
        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var texts = content.EnumerateArray()
            .Where(item => GetString(item, "type") is "text" or "output_text")
            .Select(item => GetString(item, "text"))
            .Where(text => !string.IsNullOrWhiteSpace(text));
        return string.Join(Environment.NewLine, texts!);
    }

    private static IEnumerable<FileInfo> EnumerateJsonl(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<FileInfo> EnumerateRecentCodexFiles(string root)
    {
        var cutoff = DateTime.UtcNow.AddDays(-14);
        return EnumerateJsonl(root)
            .Where(file => file.LastWriteTimeUtc >= cutoff)
            .ToArray();
    }

    private bool IsVisibleCodexSession(FileInfo file)
    {
        if (_codexSessionVisibility.TryGetValue(file.FullName, out var visible))
        {
            return visible;
        }

        var metadata = ReadCodexSessionMetadata(file);
        visible = IsVisibleCodexThreadSource(metadata.ThreadSource);
        if (metadata.ThreadSource is not null)
        {
            _codexSessionVisibility[file.FullName] = visible;
        }
        return visible;
    }

    private CodexSessionMetadata ReadCodexSessionMetadata(FileInfo file)
    {
        if (_codexSessionMetadata.TryGetValue(file.FullName, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024);
            var firstLine = reader.ReadLine();
            if (firstLine is null || !TryParse(firstLine, out var root) ||
                !TryGet(root, "payload", out var payload))
            {
                return new CodexSessionMetadata(null, null, null);
            }

            var metadata = new CodexSessionMetadata(
                GetString(payload, "id") ?? GetString(payload, "session_id"),
                GetString(payload, "parent_thread_id"),
                GetString(payload, "thread_source"));
            _codexSessionMetadata[file.FullName] = metadata;
            return metadata;
        }
        catch
        {
            // A session can be observed while Codex is still creating it; retry next refresh.
            return new CodexSessionMetadata(null, null, null);
        }
    }

    private void RefreshCodexThreadNames()
    {
        var path = Path.Combine(_home, ".codex", "session_index.jsonl");
        var file = new FileInfo(path);
        if (!file.Exists ||
            file.LastWriteTimeUtc == _codexThreadIndexWriteTimeUtc && file.Length == _codexThreadIndexLength)
        {
            return;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024);
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (reader.ReadLine() is { } line)
            {
                if (!TryParse(line, out var item))
                {
                    continue;
                }
                var id = GetString(item, "id");
                var name = Clean(GetString(item, "thread_name"));
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                {
                    names[id] = name;
                }
            }
            _codexThreadNames.Clear();
            foreach (var pair in names)
            {
                _codexThreadNames[pair.Key] = pair.Value;
            }
            file.Refresh();
            _codexThreadIndexWriteTimeUtc = file.LastWriteTimeUtc;
            _codexThreadIndexLength = file.Length;
        }
        catch (Exception exception)
        {
            Log.Warn($"Codex thread index read failed: {exception.Message}");
        }
    }

    internal static bool IsVisibleCodexThreadSource(string? threadSource) =>
        threadSource is "user" or "subagent";

    private static FileInfo? Latest(IEnumerable<FileInfo> files) =>
        files.OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();

    private static (ulong Input, ulong Output) SumProgress(
        IDictionary<string, FileProgress> progress,
        IReadOnlyList<FileInfo> files)
    {
        var today = DateTime.Today;
        var activePaths = files.Where(file => file.LastWriteTime >= today)
            .Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ulong input = 0;
        ulong output = 0;
        foreach (var pair in progress.Where(pair => activePaths.Contains(pair.Key)))
        {
            input += pair.Value.Input;
            output += pair.Value.Output;
        }
        return (input, output);
    }

    private static IReadOnlyList<string> ReadTailLines(string path, int maxBytes = 4 * 1024 * 1024)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - maxBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024);
            if (start > 0)
            {
                _ = reader.ReadLine();
            }
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }
            return lines;
        }
        catch
        {
            return [];
        }
    }

    private static string? ProjectFromClaudePath(FileInfo file)
    {
        var slug = file.Directory?.Name;
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }
        return slug.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
    }

    private static string? ProjectFromCursorPath(FileInfo file, string root)
    {
        var relative = Path.GetRelativePath(root, file.FullName);
        var slug = relative.Split(Path.DirectorySeparatorChar).FirstOrDefault();
        return slug?.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
    }

    private static string? ProjectFromRepositoryUrl(string? repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            return null;
        }

        var value = repositoryUrl.Trim();
        var suffix = value.IndexOfAny(['?', '#']);
        if (suffix >= 0)
        {
            value = value[..suffix];
        }
        value = value.TrimEnd('/', '\\');
        var separator = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
        separator = Math.Max(separator, value.LastIndexOf(':'));
        var name = separator >= 0 ? value[(separator + 1)..] : value;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }
        return Clean(name);
    }

    private string? ReadCodexProject(FileInfo file)
    {
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024);
            for (var index = 0; index < 32 && reader.ReadLine() is { } line; index++)
            {
                if (!TryParse(line, out var root) || !TryGet(root, "payload", out var payload))
                {
                    continue;
                }
                if (GetString(root, "type") == "session_meta" || GetString(payload, "type") == "session_meta")
                {
                    return ProjectFromCodexMetadata(payload);
                }
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"Codex metadata read failed ({file.Name}): {exception.Message}");
        }
        return null;
    }

    private string? ProjectFromCodexMetadata(JsonElement payload)
    {
        var cwd = GetString(payload, "cwd");
        string? repositoryProject = null;
        if (TryGet(payload, "git", out var git))
        {
            repositoryProject = ProjectFromRepositoryUrl(GetString(git, "repository_url"));
        }
        if (repositoryProject is null && !string.IsNullOrWhiteSpace(cwd))
        {
            if (!_repositoryProjects.TryGetValue(cwd, out repositoryProject))
            {
                repositoryProject = ProjectFromGitWorkingDirectory(cwd);
                _repositoryProjects[cwd] = repositoryProject;
            }
        }
        var folderProject = !string.IsNullOrWhiteSpace(cwd)
            ? Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : null;
        return repositoryProject ?? folderProject;
    }

    private static string? ProjectFromGitWorkingDirectory(string cwd)
    {
        try
        {
            var directory = new DirectoryInfo(cwd);
            while (directory is not null)
            {
                var dotGit = Path.Combine(directory.FullName, ".git");
                var config = ResolveGitConfig(dotGit);
                if (config is not null)
                {
                    return ProjectFromRepositoryUrl(ReadGitOrigin(config)) ?? directory.Name;
                }
                directory = directory.Parent;
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"Git project lookup failed ({cwd}): {exception.Message}");
        }
        return null;
    }

    private static string? ResolveGitConfig(string dotGit)
    {
        if (Directory.Exists(dotGit))
        {
            var config = Path.Combine(dotGit, "config");
            return File.Exists(config) ? config : null;
        }
        if (!File.Exists(dotGit))
        {
            return null;
        }

        var pointer = File.ReadLines(dotGit).FirstOrDefault();
        if (pointer is null || !pointer.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var gitDirectory = pointer[7..].Trim();
        if (!Path.IsPathRooted(gitDirectory))
        {
            gitDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dotGit)!, gitDirectory));
        }
        var directConfig = Path.Combine(gitDirectory, "config");
        if (File.Exists(directConfig))
        {
            return directConfig;
        }
        var commonDirFile = Path.Combine(gitDirectory, "commondir");
        if (!File.Exists(commonDirFile))
        {
            return null;
        }
        var commonDirectory = File.ReadLines(commonDirFile).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(commonDirectory))
        {
            return null;
        }
        if (!Path.IsPathRooted(commonDirectory))
        {
            commonDirectory = Path.GetFullPath(Path.Combine(gitDirectory, commonDirectory));
        }
        var commonConfig = Path.Combine(commonDirectory, "config");
        return File.Exists(commonConfig) ? commonConfig : null;
    }

    private static string? ReadGitOrigin(string config)
    {
        var inOrigin = false;
        foreach (var line in File.ReadLines(config))
        {
            var value = line.Trim();
            if (value.StartsWith("[", StringComparison.Ordinal))
            {
                inOrigin = value.Equals("[remote \"origin\"]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inOrigin)
            {
                continue;
            }
            var separator = value.IndexOf('=');
            if (separator > 0 && value[..separator].Trim().Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                return value[(separator + 1)..].Trim();
            }
        }
        return null;
    }

    private void ResetAtDayBoundary()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today == _progressDay)
        {
            return;
        }
        _progressDay = today;
        _claudeProgress.Clear();
        _codexProgress.Clear();
        _claudeMessageIds.Clear();
    }

    private static bool TryParse(string line, out JsonElement root)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            root = document.RootElement.Clone();
            return true;
        }
        catch
        {
            root = default;
            return false;
        }
    }

    private static bool TryGet(JsonElement root, string property, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out value))
        {
            return true;
        }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement root, string property) =>
        TryGet(root, property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static ulong GetUInt64(JsonElement root, string property) =>
        TryGet(root, property, out var value) && value.TryGetUInt64(out var number) ? number : 0;

    private static string? FindString(JsonElement root, string property)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in root.EnumerateObject())
            {
                if (item.NameEquals(property) && item.Value.ValueKind == JsonValueKind.String)
                {
                    return item.Value.GetString();
                }
                var nested = FindString(item.Value, property);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var nested = FindString(item, property);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        return null;
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        text = text.Trim();
        return text.Length > 1200 ? text[..1200] + "…" : text;
    }
}
