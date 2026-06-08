namespace ROTA.Domain.Enums;

// BETA — flat enum chosen over a discriminated Stat axis; simpler to key on board+period_key.
// Three per-stat live ladders (StatAttack/StatDefense/StatDiscernment) are distinct boards.
public enum LeaderboardBoard
{
    StatAttack       = 0,
    StatDefense      = 1,
    StatDiscernment  = 2,
    EnergySpent      = 3,
    DamageDealt      = 4,
    LargestHit       = 5,
    // System 22 Phase A — Overall Mastery Rating (Formula B). Active = live from current levels;
    // Lifetime = high-water tenure marker (equal to Active in Phase A — levels are monotonic).
    MasteryRatingActive   = 6,
    MasteryRatingLifetime = 7,
}
