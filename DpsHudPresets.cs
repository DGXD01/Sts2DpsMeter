namespace Sts2DpsMeter;

internal enum HudLayoutPreset
{
    Minimal,
    Default,
    Detailed,
    Multiplayer
}

// 只在用户点击时复制展示参数，不保存“当前预设模式”，不在加载或刷新时重新应用。
internal static class DpsHudPresets
{
    internal static bool Apply(DpsMeterSettingsData settings, HudLayoutPreset preset)
    {
        var values = Values(preset);
        if (values is null) return false;

        settings.HudTheme = values.HudTheme;
        settings.CompactMode = values.CompactMode;
        settings.MaxRows = values.MaxRows;
        settings.UiScale = values.UiScale;
        settings.ShowSourceIcons = values.ShowSourceIcons;
        settings.TooltipDetailMode = values.TooltipDetailMode;
        settings.EnableRowDetails = values.EnableRowDetails;
        settings.ShowDealtBreakdown = values.ShowDealtBreakdown;
        settings.ShowTakenBreakdown = values.ShowTakenBreakdown;
        settings.ShowBlockGained = values.ShowBlockGained;
        settings.ShowEnergySpent = values.ShowEnergySpent;
        settings.ShowEnergyGivenToAllies = values.ShowEnergyGivenToAllies;
        settings.ShowBlockGivenToAllies = values.ShowBlockGivenToAllies;
        settings.ShowHealingGivenToAllies = values.ShowHealingGivenToAllies;
        settings.ShowCardsDrawnForAllies = values.ShowCardsDrawnForAllies;
        settings.ShowFieldName = values.ShowFieldName;
        settings.ShowFieldDamage = values.ShowFieldDamage;
        settings.ShowFieldPercentage = values.ShowFieldPercentage;
        settings.ShowFieldHits = values.ShowFieldHits;
        settings.ShowFieldMaxHit = values.ShowFieldMaxHit;
        settings.ShowFieldAverage = values.ShowFieldAverage;
        settings.SummaryDamage = values.SummaryDamage;
        settings.SummaryDps = values.SummaryDps;
        settings.SummaryDpt = values.SummaryDpt;
        settings.SummaryTaken = values.SummaryTaken;
        settings.SummaryTurn = values.SummaryTurn;
        settings.SummaryHits = values.SummaryHits;
        settings.SortMode = values.SortMode;
        settings.MultiplayerDefaultView = values.MultiplayerDefaultView;
        return true;
    }

    private static DpsMeterSettingsData? Values(HudLayoutPreset preset) => preset switch
    {
        HudLayoutPreset.Minimal => new DpsMeterSettingsData
        {
            CompactMode = true,
            MaxRows = 3,
            UiScale = 0.9f,
            ShowTakenBreakdown = false,
            ShowSourceIcons = false,
            ShowFieldPercentage = false,
            EnableRowDetails = false,
            TooltipDetailMode = HudTooltipDetail.Simple
        },
        HudLayoutPreset.Default => new DpsMeterSettingsData(),
        HudLayoutPreset.Detailed => new DpsMeterSettingsData
        {
            HudTheme = HudTheme.HighContrast,
            MaxRows = 10,
            UiScale = 1.1f,
            ShowBlockGained = true,
            ShowEnergySpent = true,
            ShowEnergyGivenToAllies = true,
            ShowBlockGivenToAllies = true,
            ShowHealingGivenToAllies = true,
            ShowCardsDrawnForAllies = true,
            ShowFieldHits = true,
            ShowFieldMaxHit = true,
            ShowFieldAverage = true,
            SummaryDps = true,
            SummaryDpt = true
        },
        HudLayoutPreset.Multiplayer => new DpsMeterSettingsData
        {
            HudTheme = HudTheme.GameBlend,
            CompactMode = true,
            MaxRows = 8,
            ShowBlockGained = true,
            ShowEnergySpent = true,
            ShowEnergyGivenToAllies = true,
            ShowBlockGivenToAllies = true,
            ShowHealingGivenToAllies = true,
            ShowCardsDrawnForAllies = true,
            ShowFieldHits = true,
            SummaryDps = true,
            SummaryDpt = true,
            MultiplayerDefaultView = HudPlayerView.Team
        },
        _ => null
    };

    internal static string Label(HudLayoutPreset preset, bool chinese) => preset switch
    {
        HudLayoutPreset.Minimal => chinese ? "极简" : "Minimal",
        HudLayoutPreset.Default => chinese ? "默认" : "Default",
        HudLayoutPreset.Detailed => chinese ? "详细" : "Detailed",
        HudLayoutPreset.Multiplayer => chinese ? "多人" : "Multiplayer",
        _ => chinese ? "未知预设" : "Unknown preset"
    };

    internal static string Description(HudLayoutPreset preset, bool chinese) => preset switch
    {
        HudLayoutPreset.Minimal => chinese
            ? "极简：默认深色，3 行，90% 缩放，紧凑；仅输出，名称/数值，无图标/行详情，简洁提示；标题仅伤害。"
            : "Minimal: Default Dark, 3 rows, 90% scale, compact; dealt damage, name/value, no icons/row details, simple tips; damage title.",
        HudLayoutPreset.Default => chinese
            ? "默认：默认深色，5 行，100% 缩放；输出/承伤，名称/数值/占比，图标/行详情及详细提示；标题仅伤害。"
            : "Default: Default Dark, 5 rows, 100% scale; dealt/taken, name/value/share, icons/row details and detailed tips; damage title.",
        HudLayoutPreset.Detailed => chinese
            ? "详细：高对比，10 行，110% 缩放；启用全部统计模块与六字段，图标/行详情及详细提示；标题伤害/DPS/DPT。队友辅助仅多人显示。"
            : "Detailed: High Contrast, 10 rows, 110% scale; all modules and six fields, icons/row details and detailed tips; damage/DPS/DPT title. Ally support appears only in multiplayer.",
        HudLayoutPreset.Multiplayer => chinese
            ? "多人：游戏融合，8 行，100% 缩放，紧凑；全部模块，名称/数值/占比/命中，图标/行详情及详细提示；标题伤害/DPS/DPT，新多人战斗默认全队。"
            : "Multiplayer: Game Blend, 8 rows, 100% scale, compact; all modules, name/value/share/hits, icons/row details and detailed tips; damage/DPS/DPT title, Team default for new multiplayer combats.",
        _ => string.Empty
    };
}
