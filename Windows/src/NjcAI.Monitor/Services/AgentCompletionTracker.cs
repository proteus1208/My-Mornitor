using System.Text.Json;
using NjcAI.Monitor.Core;

namespace NjcAI.Monitor.Services;

internal sealed record AgentCompletionUpdate(
    AgentsSnapshot Snapshot,
    IReadOnlyList<AgentSnapshot> CompletedAgents,
    IReadOnlyList<AgentCompletionAlert> PendingAlerts,
    bool PendingChanged);

internal sealed class AgentCompletionTracker
{
    private readonly object _gate = new();
    private readonly string? _persistencePath;
    private readonly Dictionary<string, bool> _workingStates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastSeen =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AgentCompletionAlert> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    public AgentCompletionTracker(string? persistencePath = null, bool persistenceEnabled = true)
    {
        _persistencePath = persistenceEnabled
            ? persistencePath ?? Path.Combine(AppSettings.DataDirectory, "completion-alerts.json")
            : null;
        LoadPendingAlerts();
    }

    public IReadOnlyList<AgentCompletionAlert> PendingAlerts
    {
        get
        {
            lock (_gate)
            {
                return OrderedPendingAlerts();
            }
        }
    }

    public AgentCompletionUpdate Update(AgentsSnapshot snapshot, DateTime now)
    {
        lock (_gate)
        {
            var completed = new List<AgentSnapshot>();
            var pendingChanged = false;
            foreach (var agent in snapshot.Sessions)
            {
                var identity = agent.Identity;
                if (agent.IsWorking &&
                    agent.LastActive is { } resumedAt &&
                    _pending.TryGetValue(identity, out var pendingAlert) &&
                    resumedAt > pendingAlert.CompletedAt)
                {
                    _pending.Remove(identity);
                    pendingChanged = true;
                    Log.Info($"Cleared completed AI alert after session resumed: {identity}");
                }

                if (_workingStates.TryGetValue(identity, out var wasWorking) &&
                    wasWorking && !agent.IsWorking && string.IsNullOrWhiteSpace(agent.WaitingFor))
                {
                    _pending[identity] = CreateAlert(agent, now);
                    completed.Add(agent with { CompletedAt = now });
                    pendingChanged = true;
                }

                _workingStates[identity] = agent.IsWorking;
                _lastSeen[identity] = now;
            }

            foreach (var identity in _lastSeen
                         .Where(item => now - item.Value > TimeSpan.FromDays(1) &&
                                        !_pending.ContainsKey(item.Key))
                         .Select(item => item.Key)
                         .ToArray())
            {
                _lastSeen.Remove(identity);
                _workingStates.Remove(identity);
            }

            if (pendingChanged)
            {
                SavePendingAlerts();
            }

            AgentSnapshot Decorate(AgentSnapshot agent) =>
                _pending.TryGetValue(agent.Identity, out var alert)
                    ? agent with { CompletedAt = alert.CompletedAt }
                    : agent with { CompletedAt = null };

            var decorated = snapshot with
            {
                Claude = Decorate(snapshot.Claude),
                Codex = Decorate(snapshot.Codex),
                Cursor = Decorate(snapshot.Cursor),
                CodexSessions = snapshot.CodexSessions?.Select(Decorate).ToArray(),
            };
            return new AgentCompletionUpdate(
                decorated, completed, OrderedPendingAlerts(), pendingChanged);
        }
    }

    public bool Acknowledge(string identity)
    {
        lock (_gate)
        {
            if (!_pending.Remove(identity))
            {
                return false;
            }
            SavePendingAlerts();
            return true;
        }
    }

    public int AcknowledgeAll()
    {
        lock (_gate)
        {
            var count = _pending.Count;
            if (count == 0)
            {
                return 0;
            }
            _pending.Clear();
            SavePendingAlerts();
            return count;
        }
    }

    private static AgentCompletionAlert CreateAlert(AgentSnapshot agent, DateTime completedAt)
    {
        var activity = string.IsNullOrWhiteSpace(agent.Activity)
            ? "This task has been completed."
            : agent.Activity.ReplaceLineEndings(" ").Trim();
        if (activity.Length > 500)
        {
            activity = activity[..497] + "…";
        }
        return new AgentCompletionAlert(
            agent.Identity,
            agent.Kind,
            agent.Project ?? "AI Session",
            activity,
            completedAt);
    }

    private AgentCompletionAlert[] OrderedPendingAlerts() =>
        _pending.Values.OrderByDescending(alert => alert.CompletedAt).ToArray();

    private void LoadPendingAlerts()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath))
        {
            return;
        }
        try
        {
            var alerts = JsonSerializer.Deserialize<AgentCompletionAlert[]>(
                File.ReadAllText(_persistencePath)) ?? [];
            foreach (var alert in alerts.Where(alert => !string.IsNullOrWhiteSpace(alert.Identity)))
            {
                _pending[alert.Identity] = alert;
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"Completion alert cache load failed: {exception.Message}");
        }
    }

    private void SavePendingAlerts()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath))
        {
            return;
        }
        try
        {
            var directory = Path.GetDirectoryName(_persistencePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var temporaryPath = _persistencePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(OrderedPendingAlerts(),
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _persistencePath, true);
        }
        catch (Exception exception)
        {
            Log.Warn($"Completion alert cache save failed: {exception.Message}");
            Console.Error.WriteLine($"Completion alert cache save failed: {exception}");
        }
    }
}
