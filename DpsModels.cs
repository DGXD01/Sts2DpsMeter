using System.Collections.Generic;

namespace Sts2DpsMeter;

public sealed record SourceStatsSnapshot(
    string Source,
    long Damage,
    string InternalId,
    DamageSourceType SourceType,
    DamageCategory Category,
    AttributionConfidence Confidence,
    string? TriggerCard,
    string? SemanticTag,
    int Hits,
    long BlockedDamage,
    long UnblockedDamage,
    long OverkillDamage);

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
    long EffectiveHpDamage,
    long BlockedDamage,
    long UnblockedDamage,
    long OverkillDamage,
    long IncomingDamage,
    long SelfLossDamage,
    int IncomingHits,
    int SelfLossHits,
    IReadOnlyList<SourceStatsSnapshot> Sources,
    IReadOnlyList<SourceStatsSnapshot> IncomingSources,
    IReadOnlyList<SourceStatsSnapshot> SelfLossSources,
    IReadOnlyList<TurnStatsSnapshot> Turns);

public sealed record CombatStatsSnapshot(
    bool IsActive,
    double ElapsedSeconds,
    int CurrentTurn,
    IReadOnlyList<PlayerStatsSnapshot> Players);
