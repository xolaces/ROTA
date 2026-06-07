namespace ROTA.Domain.Enums;

/// <summary>
/// Per-player guild-economy currency discriminator (System 21 Slice 3a). <see cref="Sigil"/>s summon
/// guild raids (after being donated to the guild pool); <see cref="ShopTicket"/>s are the placeholder
/// currency used to buy sigils until a real guild currency ships. The discriminator is folded into the
/// ledger idempotency key so a single referenceId can touch both currencies in one operation (a buy).
/// </summary>
public enum GuildCurrency
{
    Sigil = 0,
    ShopTicket = 1,
}
