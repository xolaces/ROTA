namespace ROTA.Domain.Enums;

public enum RaidSize
{
    Personal = 0,  // Sigil-summoned solo raid — 1 participant (summoner only)
    Small    = 1,  // cap 10
    Medium   = 2,  // cap 25
    Large    = 3,  // cap 50  (was 2 before ExpandRaidSizeSet migration)
    Titanic  = 4,  // cap 250
}
