using System.Collections.Generic;

namespace Sts2DpsMeter;

public sealed record SourceStatsSnapshot(
    string Source,
    long Damage,
    string InternalId,
    DamageSourceType SourceType,
    DamageCategory Category,
    AttributionConfidence Confidence,
    DamageMechanismKind Mechanism,
    SourceTrackingStatus TrackingStatus,
    string? TriggerCard,
    string? TriggerCardId,
    string? SemanticTag,
    PoisonSourceInfo? PoisonSource,
    int Hits,
    long BlockedDamage,
    long UnblockedDamage,
    long OverkillDamage);

public sealed record CardDamageSnapshot(
    string GroupId,
    string DisplayName,
    string? CardId,
    DamageSourceType SourceType,
    SourceTrackingStatus TrackingStatus,
    string? TriggerCard,
    string? TriggerCardId,
    string? SemanticTag,
    PoisonSourceInfo? PoisonSource,
    int Hits,
    long Damage,
    long BlockedDamage,
    long UnblockedDamage,
    long OverkillDamage);

public sealed record MechanismDamageSnapshot(
    DamageMechanismKind Mechanism,
    int Hits,
    long Damage,
    long BlockedDamage,
    long UnblockedDamage,
    long OverkillDamage);

public sealed record TurnStatsSnapshot(
    int Turn,
    long Damage,
    long BlockGained,
    long EnergySpent,
    long IncomingDamage,
    long SelfLossDamage,
    IReadOnlyList<CardDamageSnapshot> Cards,
    IReadOnlyList<MechanismDamageSnapshot> Mechanisms);

public sealed record PlayerStatsSnapshot(
    ulong PlayerNetId,
    string DisplayName,
    string CharacterId,
    string CharacterName,
    string NameColorHex,
    bool IsLocalPlayer,
    long TotalDamage,
    long BlockGained,
    long EnergySpent,
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
