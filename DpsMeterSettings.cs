using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;

namespace Sts2DpsMeter;

public sealed class DpsMeterSettingsData
{
    public int SchemaVersion { get; set; } = 1;
    public bool StartVisible { get; set; } = true;
    public bool LockPosition { get; set; }
    public bool CompactMode { get; set; }
    public bool ShowSummary { get; set; } = true;
    public bool ShowRanking { get; set; } = true;
    public bool ShowCoreMetrics { get; set; } = true;
    public bool ShowDamageBreakdown { get; set; } = true;
    public bool ShowSources { get; set; } = true;
    public bool ShowTurns { get; set; } = true;
    public bool ShowCards { get; set; } = true;
    public bool ShowMechanisms { get; set; } = true;
    public bool ShowIncoming { get; set; } = true;
    public bool ShowSelfLoss { get; set; } = true;
    public int Columns { get; set; } = 2;
    public int MaxRows { get; set; } = 5;
    public int MaxTurns { get; set; } = 8;
    public float PanelOpacity { get; set; } = 0.92f;
    public float UiScale { get; set; } = 1.0f;
    public float PositionX { get; set; } = 18f;
    public float PositionY { get; set; } = 18f;
}

public static class DpsMeterSettings
{
    private const int CurrentSchemaVersion = 1;
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
            if (loaded is null || loaded.SchemaVersion != CurrentSchemaVersion)
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
            StartVisible = source.StartVisible,
            LockPosition = source.LockPosition,
            CompactMode = source.CompactMode,
            ShowSummary = source.ShowSummary,
            ShowRanking = source.ShowRanking,
            ShowCoreMetrics = source.ShowCoreMetrics,
            ShowDamageBreakdown = source.ShowDamageBreakdown,
            ShowSources = source.ShowSources,
            ShowTurns = source.ShowTurns,
            ShowCards = source.ShowCards,
            ShowMechanisms = source.ShowMechanisms,
            ShowIncoming = source.ShowIncoming,
            ShowSelfLoss = source.ShowSelfLoss,
            Columns = source.Columns,
            MaxRows = source.MaxRows,
            MaxTurns = source.MaxTurns,
            PanelOpacity = source.PanelOpacity,
            UiScale = source.UiScale,
            PositionX = source.PositionX,
            PositionY = source.PositionY
        };
    }

    private static void Normalize(DpsMeterSettingsData settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        settings.Columns = Math.Clamp(settings.Columns, 1, 3);
        settings.MaxRows = Math.Clamp(settings.MaxRows, 1, MaxRowsLimit);
        settings.MaxTurns = Math.Clamp(settings.MaxTurns, 1, 20);
        settings.PanelOpacity = Math.Clamp(settings.PanelOpacity, 0.35f, 1f);
        settings.UiScale = Math.Clamp(settings.UiScale, 0.75f, 1.35f);
        settings.PositionX = Math.Max(0f, settings.PositionX);
        settings.PositionY = Math.Max(0f, settings.PositionY);
    }
}
