using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;

namespace Sts2DpsMeter;

public enum HudTooltipDetail
{
    Simple,
    Detailed,
    Developer
}

public sealed class DpsMeterSettingsData
{
    public int SchemaVersion { get; set; } = 5;
    public bool ShowSourceIcons { get; set; } = true;
    public HudTooltipDetail TooltipDetailMode { get; set; } = HudTooltipDetail.Detailed;
    public bool EnableRowDetails { get; set; } = true;
    public bool ShowFieldName { get; set; } = true;
    public bool ShowFieldDamage { get; set; } = true;
    public bool ShowFieldPercentage { get; set; } = true;
    public bool ShowFieldHits { get; set; }
    public bool ShowFieldMaxHit { get; set; }
    public bool ShowFieldAverage { get; set; }
    public bool StartVisible { get; set; } = true;
    public bool LockPosition { get; set; }
    public bool CompactMode { get; set; }
    public bool ShowDealtBreakdown { get; set; } = true;
    public bool ShowTakenBreakdown { get; set; } = true;
    public bool ShowBlockGained { get; set; }
    public bool ShowEnergySpent { get; set; }
    public bool ShowEnergyGivenToAllies { get; set; }
    public bool ShowBlockGivenToAllies { get; set; }
    public bool ShowHealingGivenToAllies { get; set; }
    public bool ShowCardsDrawnForAllies { get; set; }
    public int Columns { get; set; } = 2;
    public int MaxRows { get; set; } = 5;
    public float PanelOpacity { get; set; } = 0.92f;
    public float UiScale { get; set; } = 1.0f;
    public float PositionX { get; set; } = 18f;
    public float PositionY { get; set; } = 18f;
}

public static class DpsMeterSettings
{
    public const float MinUiScale = 0.5f;
    public const float MaxUiScale = 2.0f;
    public const float UiScaleStep = 0.05f;

    private const int CurrentSchemaVersion = 5;
    private const int MaxRowsLimit = 12;

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
            if (loaded is null || loaded.SchemaVersion > CurrentSchemaVersion)
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
        return new DpsMeterSettingsData();
    }

    private static DpsMeterSettingsData Clone(DpsMeterSettingsData source)
    {
        return new DpsMeterSettingsData
        {
            SchemaVersion = source.SchemaVersion,
            ShowSourceIcons = source.ShowSourceIcons,
            TooltipDetailMode = source.TooltipDetailMode,
            EnableRowDetails = source.EnableRowDetails,
            ShowFieldName = source.ShowFieldName,
            ShowFieldDamage = source.ShowFieldDamage,
            ShowFieldPercentage = source.ShowFieldPercentage,
            ShowFieldHits = source.ShowFieldHits,
            ShowFieldMaxHit = source.ShowFieldMaxHit,
            ShowFieldAverage = source.ShowFieldAverage,
            StartVisible = source.StartVisible,
            LockPosition = source.LockPosition,
            CompactMode = source.CompactMode,
            ShowDealtBreakdown = source.ShowDealtBreakdown,
            ShowTakenBreakdown = source.ShowTakenBreakdown,
            ShowBlockGained = source.ShowBlockGained,
            ShowEnergySpent = source.ShowEnergySpent,
            ShowEnergyGivenToAllies = source.ShowEnergyGivenToAllies,
            ShowBlockGivenToAllies = source.ShowBlockGivenToAllies,
            ShowHealingGivenToAllies = source.ShowHealingGivenToAllies,
            ShowCardsDrawnForAllies = source.ShowCardsDrawnForAllies,
            Columns = source.Columns,
            MaxRows = source.MaxRows,
            PanelOpacity = source.PanelOpacity,
            UiScale = source.UiScale,
            PositionX = source.PositionX,
            PositionY = source.PositionY
        };
    }

    private static void Normalize(DpsMeterSettingsData settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        if (!Enum.IsDefined(settings.TooltipDetailMode))
        {
            settings.TooltipDetailMode = HudTooltipDetail.Detailed;
        }
        if (!settings.ShowFieldName && !settings.ShowFieldDamage && !settings.ShowFieldPercentage &&
            !settings.ShowFieldHits && !settings.ShowFieldMaxHit && !settings.ShowFieldAverage)
        {
            // 所有字段都关闭时保留名称，避免出现无法识别的空白条。
            settings.ShowFieldName = true;
        }
        settings.Columns = Math.Clamp(settings.Columns, 1, 2);
        settings.MaxRows = Math.Clamp(settings.MaxRows, 1, MaxRowsLimit);
        settings.PanelOpacity = Math.Clamp(settings.PanelOpacity, 0.35f, 1f);
        settings.UiScale = ClampUiScale(settings.UiScale);
        settings.PositionX = Math.Max(0f, settings.PositionX);
        settings.PositionY = Math.Max(0f, settings.PositionY);
    }
}
