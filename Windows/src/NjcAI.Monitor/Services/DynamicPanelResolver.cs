using NjcAI.Monitor.Core;

namespace NjcAI.Monitor.Services;

internal sealed class DynamicPanelResolver
{
    private readonly TimeSpan _activityWindow;
    private readonly TimeSpan _replacementCooldown;
    private readonly List<string> _selectedAgentIds = [];
    private DateTime _lastSelectionChange;
    private bool _initialized;

    public DynamicPanelResolver(
        TimeSpan? activityWindow = null,
        TimeSpan? replacementCooldown = null)
    {
        _activityWindow = activityWindow ?? TimeSpan.FromMinutes(5);
        _replacementCooldown = replacementCooldown ?? TimeSpan.FromSeconds(30);
    }

    public IReadOnlyList<DashboardPanel> Resolve(AgentsSnapshot agents, DateTime now)
    {
        var allCandidates = agents.Sessions
            .Where(agent => IsEligible(agent, now, _activityWindow))
            .OrderByDescending(agent => agent.IsWorking)
            .ThenByDescending(agent => HasUnacknowledgedCompletion(agent, now))
            .ThenByDescending(agent => agent.NeedsAttention)
            .ThenByDescending(agent => agent.LastActive)
            .ToArray();
        var byId = allCandidates.ToDictionary(agent => agent.Identity, StringComparer.OrdinalIgnoreCase);
        var desiredIds = allCandidates.Take(4).Select(agent => agent.Identity).ToArray();
        var selectedStillValid = _selectedAgentIds.All(byId.ContainsKey);
        var sameSet = _selectedAgentIds.Count == desiredIds.Length &&
                      _selectedAgentIds.All(id => desiredIds.Contains(id, StringComparer.OrdinalIgnoreCase));
        var countChanged = _selectedAgentIds.Count != desiredIds.Length;

        if (!_initialized || !selectedStillValid || countChanged ||
            (!sameSet && now - _lastSelectionChange >= _replacementCooldown))
        {
            var preserved = _selectedAgentIds
                .Where(id => desiredIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                .ToList();
            preserved.AddRange(desiredIds.Where(id => !preserved.Contains(id, StringComparer.OrdinalIgnoreCase)));
            _selectedAgentIds.Clear();
            _selectedAgentIds.AddRange(preserved.Take(4));
            _lastSelectionChange = now;
            _initialized = true;
        }

        var selectedAgents = _selectedAgentIds
            .Select(id => byId.GetValueOrDefault(id))
            .Where(agent => agent is not null)
            .Select(agent => agent!)
            .ToArray();
        var panels = selectedAgents
            .Select(agent => new DashboardPanel(DashboardPanelKind.Agent, agent))
            .ToList();

        if (panels.Count < 4)
        {
            panels.Add(new DashboardPanel(DashboardPanelKind.Markets));
        }
        if (panels.Count < 4)
        {
            panels.Add(new DashboardPanel(DashboardPanelKind.News));
        }
        if (panels.Count < 4)
        {
            panels.Add(new DashboardPanel(DashboardPanelKind.AiQuota));
        }
        if (panels.Count < 4)
        {
            panels.Add(new DashboardPanel(DashboardPanelKind.Forecast));
        }
        return panels.Take(4).ToArray();
    }

    internal static bool IsEligible(AgentSnapshot agent, DateTime now, TimeSpan activityWindow) =>
        agent.Available && agent.LastActive is { } lastActive &&
        (agent.IsWorking || agent.NeedsAttention || HasUnacknowledgedCompletion(agent, now) ||
         now - lastActive <= activityWindow);

    private static bool HasUnacknowledgedCompletion(AgentSnapshot agent, DateTime now) =>
        agent.CompletedAt is { } completedAt &&
        now - completedAt >= TimeSpan.Zero;
}
