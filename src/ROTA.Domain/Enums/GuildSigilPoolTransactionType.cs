namespace ROTA.Domain.Enums;

/// <summary>Reason for a guild sigil-pool ledger row (System 21 Slice 3a credits / Slice 3b debits).</summary>
public enum GuildSigilPoolTransactionType
{
    Donation = 0,    // +Sigil donated by a member
    RaidSummon = 1,  // −1 Sigil consumed to summon a guild raid (Slice 3b)
    AdminAdjust = 2, // operator/admin adjustment
}
