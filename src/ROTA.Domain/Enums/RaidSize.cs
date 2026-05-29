namespace ROTA.Domain.Enums;

public enum RaidSize
{
    Personal = 0,  // Sigil-summoned solo raid — only the summoner can hit; HP uses PersonalBaseHp
    Small    = 1,  // PHASE-2: party-sized, capped participant count
    Large    = 2,  // World raid — any player can participate
}
