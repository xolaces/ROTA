namespace ROTA.Domain.Enums;

/// <summary>Reason for a per-player guild-currency ledger row (System 21 Slice 3a).</summary>
public enum GuildCurrencyTransactionType
{
    DailyClaim = 0,    // +Sigil daily free claim (once per UTC day)
    TicketGrant = 1,   // +ShopTicket daily allowance (once per UTC day)
    ShopPurchase = 2,  // −ShopTicket and +Sigil in a single buy (shared referenceId)
    Donation = 3,      // −Sigil moved from the player to the guild pool
    AdminGrant = 4,    // operator/admin adjustment
}
