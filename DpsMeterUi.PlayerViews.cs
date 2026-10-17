using Godot;

namespace Sts2DpsMeter;

public static partial class DpsMeterUi
{
    private static readonly HudPlayerSelection PlayerSelection = new();
    private static HBoxContainer? _playerViewRow;
    private static OptionButton? _playerViewOption;
    private static RichTextLabel? _playerIdentity;
    private static bool _selfAwaitingLocal;
    private static bool IsPlayerViewPopupOpen => IsAlive(_playerViewOption) && _playerViewOption!.GetPopup().Visible;

    private static void ApplyPlayerSelection(CombatStatsSnapshot snapshot)
    {
        _selectedPlayerIndex = PlayerSelection.Index;
        _scopeIndex = (int)PlayerSelection.Scope;
        _selfAwaitingLocal = snapshot.Players.Count > 1 && PlayerSelection.View == HudPlayerView.Self && PlayerSelection.Index < 0;
    }

    private static void BuildPlayerViewRow(VBoxContainer layout)
    {
        _playerViewRow = new HBoxContainer
        {
            Name = "Sts2DpsMeterPlayerView",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _playerViewRow.AddThemeConstantOverride("separation", 6);
        layout.AddChild(_playerViewRow);
        _playerViewOption = new OptionButton
        {
            CustomMinimumSize = new Vector2(112, 28),
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        for (var index = 0; index < 3; index++) _playerViewOption.AddItem(string.Empty, index);
        _playerViewOption.AddThemeFontSizeOverride("font_size", DpsHudTypography.CompactBodySize);
        _playerViewOption.AddThemeColorOverride("font_color", PrimaryText);
        _playerViewOption.GetPopup().Theme = _panel!.Theme;
        ApplyDropdownStyle(_playerViewOption);
        _playerViewOption.ItemSelected += index =>
        {
            var snapshot = DpsStats.GetSnapshot();
            PlayerSelection.Sync(snapshot, DpsMeterSettings.Current.MultiplayerDefaultView);
            PlayerSelection.Choose((HudPlayerView)_playerViewOption.GetItemId((int)index), snapshot.Players);
            ApplyPlayerSelection(snapshot);
            ResetContentScroll();
            Refresh();
        };
        _playerViewRow.AddChild(_playerViewOption);
        _playerIdentity = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 24),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _playerViewRow.AddChild(_playerIdentity);
    }

    private static bool UpdatePlayerViewControls(CombatStatsSnapshot snapshot, bool collapsed)
    {
        if (!IsAlive(_playerViewRow) || !IsAlive(_playerViewOption) || !IsAlive(_playerIdentity)) return false;
        var changed = SetVisible(_playerViewRow!, snapshot.Players.Count > 1 && !collapsed);
        for (var index = 0; index < 3; index++)
        {
            var label = HudPlayerSelection.Label((HudPlayerView)index, DpsLocalization.IsChinese);
            if (_playerViewOption!.GetItemText(index) != label)
            {
                _playerViewOption.SetItemText(index, label);
                changed = true;
            }
        }
        _playerViewOption!.Select((int)PlayerSelection.View);
        _playerViewOption.TooltipText = L("快速查看玩家、自己或全队；左右箭头切换到当前玩家视图", "Choose Player, Self, or Team; arrows switch to Player view");
        string identity;
        if (PlayerSelection.View == HudPlayerView.Team) identity = L($"全队 · {snapshot.Players.Count} 人", $"Team · {snapshot.Players.Count} players");
        else if (PlayerSelection.Index < 0) identity = L("等待本地玩家数据", "Waiting for local player data");
        else
        {
            var player = snapshot.Players[PlayerSelection.Index];
            var name = string.IsNullOrWhiteSpace(player.DisplayName) ? L("未知玩家", "Unknown player") : player.DisplayName;
            identity = BuildRichRowText(new DamageRowData(name, 0, NameColorHex: player.NameColorHex, IsLocalPlayer: player.IsLocalPlayer), DpsMeterSettings.Current.HudTheme);
        }
        if (_playerIdentity!.Text != identity)
        {
            _playerIdentity.Text = identity;
            changed = true;
        }
        return changed;
    }
}
