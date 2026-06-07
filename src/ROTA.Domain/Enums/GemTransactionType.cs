namespace ROTA.Domain.Enums;

public enum GemTransactionType
{
    DailyReward  = 1,
    QuestReward  = 2,
    EnergyRefill = 3,
    AdminGrant   = 4,
    RaidReward   = 5,
    LevelUpReward   = 6,
    MagicPurchase   = 7,
    UnitPurchase    = 8,
    LegionPurchase  = 9,
    PinnacleReward  = 10,
    // System 16 Slice 2 — gems spent to buy Gauntlet Strikes (uncapped action currency).
    GauntletStrikePurchase = 11,
    // System 16 Slice 6 — gems GRANTED from a Gauntlet token-shop GemBundle purchase
    // (a +credit on the gem ledger, idempotent via the shop referenceId).
    GauntletShopReward = 12,
}
