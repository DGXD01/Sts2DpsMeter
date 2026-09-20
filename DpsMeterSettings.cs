using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;

namespace Sts2DpsMeter;

public sealed class HudModuleLayoutData
{
    public float X { get; set; }
    public float Y { get; set; }
    public string? AttachTo { get; set; }
    public string DockSide { get; set; } = DpsMeterSettings.DockSideNone;
    public bool Collapsed { get; set; }
}

public sealed class DpsMeterSettingsData
{
    public int SchemaVersion { get; set; } = DpsMeterSettings.CurrentSchemaVersion;
    public bool StartVisible { get; set; } = true;
    public bool LockPosition { get; set; }
    public bool CompactMode { get; set; }
    public bool EnableSnapping { get; set; } = true;
    public bool ShowDealtBreakdown { get; set; } = true;
    public bool ShowTakenBreakdown { get; set; } = true;
    public bool ShowCurrentOutput { get; set; } = true;
    public bool ShowTurnDamage { get; set; }
    public bool ShowCombatDamage { get; set; }
    public bool ShowTakenDamage { get; set; }
    public bool ShowMultiplayerRanking { get; set; }
    public int Columns { get; set; } = 2;
    public int MaxRows { get; set; } = 5;
    public float PanelOpacity { get; set; } = 0.92f;
    public float UiScale { get; set; } = 1.0f;
    public float PositionX { get; set; } = 18f;
    public float PositionY { get; set; } = 18f;
    public Dictionary<string, HudModuleLayoutData> ModuleLayouts { get; set; } = new(StringComparer.Ordinal);
}

public static class DpsMeterSettings
{
    public const float MinUiScale = 0.5f;
    public const float MaxUiScale = 2.0f;
    public const float UiScaleStep = 0.05f;

    public const string ModuleCurrentOutput = "currentOutput";
    public const string ModuleTurnDamage = "turnDamage";
    public const string ModuleCombatDamage = "combatDamage";
    public const string ModuleTakenDamage = "takenDamage";
    public const string ModuleMultiplayerRanking = "multiplayerRanking";

    public const string DockSideNone = "none";
    public const string DockSideTop = "top";
    public const string DockSideBottom = "bottom";
    public const string DockSideLeft = "left";
    public const string DockSideRight = "right";

    public static readonly string[] ModuleOrder =
    [
        ModuleCurrentOutput,
        ModuleTurnDamage,
        ModuleCombatDamage,
        ModuleTakenDamage,
        ModuleMultiplayerRanking
    ];

    internal const int CurrentSchemaVersion = 3;

    private const int MaxRowsLimit = 12;
    private const float DefaultModuleGapX = 380f;
    private const float DefaultModuleGapY = 220f;

    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static DpsMeterSettingsData? _current;

    public static event Action? Changed;

    public static DpsMeterSettingsData Current
    {
        get
        {
            lock (SyncRoot)
            {
                _current ??= Load();
                return _current;
            }
        }
    }

    public static string FilePath => Path.Combine(OS.GetUserDataDir(), ModEntry.ModId, "settings.json");

    public static void Save()
    {
        DpsMeterSettingsData snapshot;
        lock (SyncRoot)
        {
            _current ??= CreateRecommended();
            Normalize(_current);
            snapshot = Clone(_current);
        }

        try
        {
            var filePath = FilePath;
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(filePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[{ModEntry.ModId}] 保存设置失败：{exception.Message}");
        }
    }

    public static void ResetToRecommended()
    {
        lock (SyncRoot)
        {
            _current = CreateRecommended();
        }

        Save();
        Changed?.Invoke();
    }

    public static void NotifyChanged()
    {
        Save();
        Changed?.Invoke();
    }

    public static float ClampUiScale(float value)
    {
        return Math.Clamp(value, MinUiScale, MaxUiScale);
    }

    internal static bool IsModuleEnabled(DpsMeterSettingsData settings, string moduleKey)
    {
        return moduleKey switch
        {
            ModuleCurrentOutput => settings.ShowCurrentOutput,
            ModuleTurnDamage => settings.ShowTurnDamage,
            ModuleCombatDamage => settings.ShowCombatDamage,
            ModuleTakenDamage => settings.ShowTakenDamage,
            ModuleMultiplayerRanking => settings.ShowMultiplayerRanking,
            _ => false
        };
    }

    internal static void SetModuleEnabled(DpsMeterSettingsData settings, string moduleKey, bool enabled)
    {
        switch (moduleKey)
        {
            case ModuleCurrentOutput:
                settings.ShowCurrentOutput = enabled;
                break;
            case ModuleTurnDamage:
                settings.ShowTurnDamage = enabled;
                break;
            case ModuleCombatDamage:
                settings.ShowCombatDamage = enabled;
                break;
            case ModuleTakenDamage:
                settings.ShowTakenDamage = enabled;
                break;
            case ModuleMultiplayerRanking:
                settings.ShowMultiplayerRanking = enabled;
                break;
        }
    }

    internal static int EnabledModuleCount(DpsMeterSettingsData settings)
    {
        var count = 0;
        foreach (var moduleKey in ModuleOrder)
        {
            if (IsModuleEnabled(settings, moduleKey))
            {
                count++;
            }
        }

        return count;
    }

    internal static HudModuleLayoutData GetModuleLayout(DpsMeterSettingsData settings, string moduleKey)
    {
        settings.ModuleLayouts ??= new Dictionary<string, HudModuleLayoutData>(StringComparer.Ordinal);
        if (!settings.ModuleLayouts.TryGetValue(moduleKey, out var layout) || layout is null)
        {
            layout = CreateDefaultModuleLayouts(settings.PositionX, settings.PositionY)[moduleKey];
            settings.ModuleLayouts[moduleKey] = layout;
        }

        return layout;
    }

    internal static void ResetModuleLayouts(DpsMeterSettingsData settings)
    {
        settings.ModuleLayouts = CreateDefaultModuleLayouts(settings.PositionX, settings.PositionY);
    }

    private static DpsMeterSettingsData Load()
    {
        try
        {
            var filePath = FilePath;
            if (!File.Exists(filePath))
            {
                return CreateRecommended();
            }

            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize<DpsMeterSettingsData>(json, JsonOptions);
            if (loaded is null)
            {
                return CreateRecommended();
            }

            if (loaded.SchemaVersion == 2)
            {
                loaded = MigrateFromSchema2(loaded);
            }
            else if (loaded.SchemaVersion != CurrentSchemaVersion)
            {
                return CreateRecommended();
            }

            Normalize(loaded);
            return loaded;
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[{ModEntry.ModId}] 读取设置失败，将使用推荐值：{exception.Message}");
            return CreateRecommended();
        }
    }

    private static DpsMeterSettingsData CreateRecommended()
    {
        var settings = new DpsMeterSettingsData();
        settings.ModuleLayouts = CreateDefaultModuleLayouts(settings.PositionX, settings.PositionY);
        return settings;
    }

    private static DpsMeterSettingsData MigrateFromSchema2(DpsMeterSettingsData source)
    {
        var settings = new DpsMeterSettingsData
        {
            SchemaVersion = CurrentSchemaVersion,
            StartVisible = source.StartVisible,
            LockPosition = source.LockPosition,
            CompactMode = source.CompactMode,
            EnableSnapping = true,
            ShowDealtBreakdown = source.ShowDealtBreakdown,
            ShowTakenBreakdown = source.ShowTakenBreakdown,
            ShowCurrentOutput = source.ShowDealtBreakdown,
            ShowTurnDamage = false,
            ShowCombatDamage = false,
            ShowTakenDamage = !source.ShowDealtBreakdown && source.ShowTakenBreakdown,
            ShowMultiplayerRanking = false,
            Columns = source.Columns,
            MaxRows = source.MaxRows,
            PanelOpacity = source.PanelOpacity,
            UiScale = source.UiScale,
            PositionX = source.PositionX,
            PositionY = source.PositionY
        };

        settings.ModuleLayouts = CreateDefaultModuleLayouts(settings.PositionX, settings.PositionY);
        return settings;
    }

    private static Dictionary<string, HudModuleLayoutData> CreateDefaultModuleLayouts(float baseX, float baseY)
    {
        return new Dictionary<string, HudModuleLayoutData>(StringComparer.Ordinal)
        {
            [ModuleCurrentOutput] = CreateLayout(baseX, baseY),
            [ModuleTurnDamage] = CreateLayout(baseX, baseY + DefaultModuleGapY),
            [ModuleCombatDamage] = CreateLayout(baseX, baseY + DefaultModuleGapY * 2f),
            [ModuleTakenDamage] = CreateLayout(baseX + DefaultModuleGapX, baseY),
            [ModuleMultiplayerRanking] = CreateLayout(baseX + DefaultModuleGapX, baseY + DefaultModuleGapY)
        };
    }

    private static HudModuleLayoutData CreateLayout(float x, float y)
    {
        return new HudModuleLayoutData
        {
            X = x,
            Y = y,
            DockSide = DockSideNone
        };
    }

    private static DpsMeterSettingsData Clone(DpsMeterSettingsData source)
    {
        var clone = new DpsMeterSettingsData
        {
            SchemaVersion = source.SchemaVersion,
            StartVisible = source.StartVisible,
            LockPosition = source.LockPosition,
            CompactMode = source.CompactMode,
            EnableSnapping = source.EnableSnapping,
            ShowDealtBreakdown = source.ShowDealtBreakdown,
            ShowTakenBreakdown = source.ShowTakenBreakdown,
            ShowCurrentOutput = source.ShowCurrentOutput,
            ShowTurnDamage = source.ShowTurnDamage,
            ShowCombatDamage = source.ShowCombatDamage,
            ShowTakenDamage = source.ShowTakenDamage,
            ShowMultiplayerRanking = source.ShowMultiplayerRanking,
            Columns = source.Columns,
            MaxRows = source.MaxRows,
            PanelOpacity = source.PanelOpacity,
            UiScale = source.UiScale,
            PositionX = source.PositionX,
            PositionY = source.PositionY,
            ModuleLayouts = new Dictionary<string, HudModuleLayoutData>(StringComparer.Ordinal)
        };

        foreach (var pair in source.ModuleLayouts)
        {
            clone.ModuleLayouts[pair.Key] = new HudModuleLayoutData
            {
                X = pair.Value.X,
                Y = pair.Value.Y,
                AttachTo = pair.Value.AttachTo,
                DockSide = pair.Value.DockSide,
                Collapsed = pair.Value.Collapsed
            };
        }

        return clone;
    }

    private static void Normalize(DpsMeterSettingsData settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        settings.Columns = Math.Clamp(settings.Columns, 1, 2);
        settings.MaxRows = Math.Clamp(settings.MaxRows, 1, MaxRowsLimit);
        settings.PanelOpacity = Math.Clamp(settings.PanelOpacity, 0.35f, 1f);
        settings.UiScale = ClampUiScale(settings.UiScale);
        settings.PositionX = Math.Max(0f, settings.PositionX);
        settings.PositionY = Math.Max(0f, settings.PositionY);
        settings.ModuleLayouts ??= new Dictionary<string, HudModuleLayoutData>(StringComparer.Ordinal);

        var defaults = CreateDefaultModuleLayouts(settings.PositionX, settings.PositionY);
        foreach (var moduleKey in ModuleOrder)
        {
            if (!settings.ModuleLayouts.TryGetValue(moduleKey, out var layout) || layout is null)
            {
                layout = defaults[moduleKey];
                settings.ModuleLayouts[moduleKey] = layout;
            }

            layout.X = Math.Max(0f, layout.X);
            layout.Y = Math.Max(0f, layout.Y);
            layout.DockSide = NormalizeDockSide(layout.DockSide);
            if (string.IsNullOrWhiteSpace(layout.AttachTo))
            {
                layout.AttachTo = null;
                layout.DockSide = DockSideNone;
            }
        }

        if (EnabledModuleCount(settings) == 0)
        {
            settings.ShowCurrentOutput = true;
        }

        BreakInvalidDocking(settings);
    }

    private static string NormalizeDockSide(string? value)
    {
        return value switch
        {
            DockSideTop => DockSideTop,
            DockSideBottom => DockSideBottom,
            DockSideLeft => DockSideLeft,
            DockSideRight => DockSideRight,
            _ => DockSideNone
        };
    }

    private static void BreakInvalidDocking(DpsMeterSettingsData settings)
    {
        foreach (var moduleKey in ModuleOrder)
        {
            var layout = settings.ModuleLayouts[moduleKey];
            if (layout.AttachTo is not null &&
                (!settings.ModuleLayouts.ContainsKey(layout.AttachTo) || layout.AttachTo == moduleKey))
            {
                layout.AttachTo = null;
                layout.DockSide = DockSideNone;
            }
        }

        foreach (var startKey in ModuleOrder)
        {
            var path = new HashSet<string>(StringComparer.Ordinal);
            var currentKey = startKey;
            while (settings.ModuleLayouts.TryGetValue(currentKey, out var currentLayout) &&
                   !string.IsNullOrWhiteSpace(currentLayout.AttachTo))
            {
                if (!path.Add(currentKey))
                {
                    currentLayout.AttachTo = null;
                    currentLayout.DockSide = DockSideNone;
                    break;
                }

                currentKey = currentLayout.AttachTo!;
            }
        }
    }
}
