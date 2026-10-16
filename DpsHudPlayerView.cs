using System;
using System.Collections.Generic;

namespace Sts2DpsMeter;

public enum HudPlayerView { CurrentPlayer, Self, Team }

// 只管理选择，使用稳定 NetId 区分排序变化及同名玩家，不修改统计。
internal sealed class HudPlayerSelection
{
    private long _combatId = -1;
    private bool _wasActive;
    private bool _defaultApplied;
    private ulong? _selectedId;
    private HudScope _individualScope = HudScope.Combat;
    private HudPlayerView _individualView = HudPlayerView.CurrentPlayer;
    internal HudPlayerView View { get; private set; }
    internal HudScope Scope { get; private set; }
    internal int Index { get; private set; } = -1;

    internal static HudPlayerView Normalize(HudPlayerView value) => Enum.IsDefined(value) ? value : HudPlayerView.CurrentPlayer;
    internal static string Label(HudPlayerView view, bool chinese) => view switch
    {
        HudPlayerView.Self => chinese ? "自己" : "Self",
        HudPlayerView.Team => chinese ? "全队" : "Team",
        _ => chinese ? "当前玩家" : "Player"
    };

    internal bool Sync(CombatStatsSnapshot snapshot, HudPlayerView defaultView)
    {
        var prior = (View, Scope, _selectedId);
        var fresh = snapshot.CombatId != _combatId || (snapshot.IsActive && !_wasActive);
        _combatId = snapshot.CombatId;
        _wasActive = snapshot.IsActive;
        if (fresh)
        {
            _defaultApplied = false;
            _selectedId = null;
            View = _individualView = HudPlayerView.CurrentPlayer;
            Scope = _individualScope = HudScope.Combat;
        }
        if (snapshot.Players.Count < 2)
        {
            View = _individualView = HudPlayerView.CurrentPlayer;
            Scope = _individualScope = DpsHudScope.Normalize(Scope, snapshot.Players.Count);
        }
        else if (snapshot.IsActive && !_defaultApplied)
        {
            _defaultApplied = true;
            Choose(defaultView, snapshot.Players);
        }
        Resolve(snapshot.Players);
        return fresh || prior != (View, Scope, _selectedId);
    }

    internal void Choose(HudPlayerView view, IReadOnlyList<PlayerStatsSnapshot> players)
    {
        view = players.Count > 1 ? Normalize(view) : HudPlayerView.CurrentPlayer;
        if (view == HudPlayerView.Team)
        {
            if (View != HudPlayerView.Team)
            {
                _individualScope = Scope;
                _individualView = View;
            }
            Scope = HudScope.Team;
        }
        else
        {
            if (View == HudPlayerView.Team) Scope = _individualScope;
            _individualView = view;
        }
        View = view;
        Resolve(players);
    }

    internal void SetScope(HudScope scope, IReadOnlyList<PlayerStatsSnapshot> players)
    {
        scope = DpsHudScope.Normalize(scope, players.Count);
        if (scope == HudScope.Team) Choose(HudPlayerView.Team, players);
        else
        {
            if (View == HudPlayerView.Team) View = _individualView;
            Scope = _individualScope = scope;
            Resolve(players);
        }
    }

    internal bool Move(int delta, IReadOnlyList<PlayerStatsSnapshot> players)
    {
        if (players.Count < 2 || View == HudPlayerView.Team) return false;
        Resolve(players);
        Index = ((Math.Max(0, Index) + delta % players.Count) % players.Count + players.Count) % players.Count;
        _selectedId = players[Index].PlayerNetId;
        View = _individualView = HudPlayerView.CurrentPlayer;
        return true;
    }

    private void Resolve(IReadOnlyList<PlayerStatsSnapshot> players)
    {
        var local = -1;
        var selected = -1;
        for (var index = 0; index < players.Count; index++)
        {
            if (local < 0 && players[index].IsLocalPlayer) local = index;
            if (_selectedId == players[index].PlayerNetId) selected = index;
        }
        Index = View == HudPlayerView.Self ? local : selected >= 0 ? selected :
            players.Count == 0 ? -1 : _selectedId.HasValue && local >= 0 ? local : 0;
        _selectedId = Index >= 0 ? players[Index].PlayerNetId : null;
    }
}
