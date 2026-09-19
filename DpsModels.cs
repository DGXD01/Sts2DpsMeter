using System.Collections.Generic;

namespace Sts2DpsMeter;

public sealed record SourceStatsSnapshot(string Source, long Damage);

public sealed record TurnStatsSnapshot(int Turn, long Damage);

public sealed record PlayerStatsSnapshot(
    ulong PlayerNetId,
    string DisplayName,
    long TotalDamage,
    int HitCount,
    int MaxHit,
    string MaxHitSource,
    double Dps,
    double Dpt,
    IReadOnlyList<SourceStatsSnapshot> Sources,
    IReadOnlyList<TurnStatsSnapshot> Turns);

public sealed record CombatStatsSnapshot(
    bool IsActive,
    double ElapsedSeconds,
    int CurrentTurn,
    IReadOnlyList<PlayerStatsSnapshot> Players);
