using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;

namespace Sts2DpsMeter;

internal static class DpsLocalization
{
    private const string EnglishLanguageCode = "eng";
    private const string ChineseLanguageCode = "zhs";

    private static readonly object SyncRoot = new();
    private static readonly LocManager.LocaleChangeCallback LocaleChangedCallback = OnLocaleChanged;
    private static readonly Dictionary<string, string> EnglishKnownValues = new(StringComparer.Ordinal)
    {
        ["未知来源"] = "Unknown Source",
        ["其他"] = "Other",
        ["中毒/持续伤害"] = "Poison/DoT",
        ["火焰/持续伤害"] = "Fire/DoT",
        ["闪电"] = "Lightning",
        ["火焰/反伤"] = "Fire/Reflect",
        ["星能/能力伤害"] = "Star/Power Damage",
        ["反伤"] = "Reflect",
        ["束缚/持续伤害"] = "Constrict/DoT",
        ["激发"] = "Evoke",
        ["被动/触发"] = "Passive/Trigger",
        ["灾厄击杀"] = "Doom Kill",
        ["能力伤害"] = "Power Damage",
        ["充能球伤害"] = "Orb Damage",
        ["遗物伤害"] = "Relic Damage",
        ["药水伤害"] = "Potion Damage",
        ["过量伤害"] = "Overkill",
        ["不可格挡"] = "Unblockable",
        ["未知职业"] = "Unknown Class",
        ["敌方伤害"] = "Enemy Damage",
        ["自损"] = "Self",
        ["助攻：易伤"] = "Assist: Vulnerable",
        ["助攻：侧击"] = "Assist: Flanking",
        ["助攻：击倒"] = "Assist: Knockdown",
        ["助攻：减速"] = "Assist: Slow",
        ["助攻：易伤强化"] = "Assist: Vulnerable Amplification"
    };

    private static LocManager? _subscribedManager;
    private static bool _subscribed;

    public static string LanguageCode
    {
        get
        {
            try
            {
                var language = LocManager.Instance?.Language;
                return string.IsNullOrWhiteSpace(language) ? EnglishLanguageCode : language;
            }
            catch
            {
                return EnglishLanguageCode;
            }
        }
    }

    public static bool IsChinese => string.Equals(LanguageCode, ChineseLanguageCode, StringComparison.OrdinalIgnoreCase);

    public static string Get(string chinese, string english)
    {
        return IsChinese ? chinese : english;
    }

    public static void EnsureSubscribed()
    {
        try
        {
            var manager = LocManager.Instance;
            if (manager is null)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (ReferenceEquals(_subscribedManager, manager) && _subscribed)
                {
                    return;
                }

                if (_subscribedManager is not null && _subscribed)
                {
                    try
                    {
                        _subscribedManager.UnsubscribeToLocaleChange(LocaleChangedCallback);
                    }
                    catch
                    {
                        // 旧实例可能已在游戏退出阶段释放，忽略以避免影响 Mod 初始化。
                    }
                }

                manager.SubscribeToLocaleChange(LocaleChangedCallback);
                _subscribedManager = manager;
                _subscribed = true;
            }
        }
        catch
        {
            // LocManager 初始化早于/晚于 Mod 的版本差异不应阻断统计功能。
        }
    }

    public static string LocalizeKnownValue(string? value)
    {
        if (string.IsNullOrEmpty(value) || IsChinese)
        {
            return value ?? string.Empty;
        }

        if (EnglishKnownValues.TryGetValue(value, out var translated))
        {
            return translated;
        }

        if (value.StartsWith("玩家 ", StringComparison.Ordinal))
        {
            return "Player " + value[3..];
        }

        if (value.StartsWith("宠物 + ", StringComparison.Ordinal))
        {
            return "Pet + " + value[5..];
        }

        return value;
    }

    private static void OnLocaleChanged()
    {
        try
        {
            DpsMeterUi.RequestImmediateRefresh();
            DpsMeterSettingsUi.Refresh();
        }
        catch
        {
            // 语言切换刷新失败不改变统计状态；下一次战斗刷新仍会使用新语言。
        }
    }
}
