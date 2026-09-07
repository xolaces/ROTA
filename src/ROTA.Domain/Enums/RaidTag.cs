namespace ROTA.Domain.Enums;

/// <summary>
/// What a raid IS, for the purpose of legion affinity. A legion may be built to fight one of these
/// better than the rest, so the tag is the hook a counter-build hangs on.
///
/// The vocabulary is the lore's own — Master Canon III, "The Four Orders of Being" — rather than an
/// invented list, so a raid's tag is answerable from its fiction instead of assigned by feel:
///
///   Order II  The Great Horrors      -> Horror (Primordials), Demon (the Swollen)
///   Order III The Beasts and Broods  -> Beast, Goblin, Undead
///   Order IV  The Wrought            -> Construct
///   the Awakening-Touched overlay    -> Shadow
///   mortal armies, what is left of Kronarch  -> Legion
///   the Gauntlet's own lineage       -> Dragon
///
/// <see cref="None"/> is the base case and it is load-bearing: a raid with no tag is a raid no legion
/// counters, which is what keeps affinity a bonus for the prepared rather than a tax on everyone else.
/// A raid carries a LIST of these — a Glutbound goblin warband is both Goblin and Shadow — and a
/// legion's affinity resolves HIGHEST-ONLY across the matches, never summed.
/// </summary>
public enum RaidTag
{
    /// <summary>Untagged. The general case; no legion affinity can match it.</summary>
    None      = 0,

    /// <summary>Organised vermin. The Broods' cheapest and most numerous work.</summary>
    Goblin    = 1,

    /// <summary>Order III native predators — the Ashen Stag, the Mire-Brood, the things that were always here.</summary>
    Beast     = 2,

    /// <summary>The risen. See Master Canon XV on the racial origin.</summary>
    Undead    = 3,

    /// <summary>The Awakening-Touched overlay — Glutbound, amber-rot, things bent toward a primal act.</summary>
    Shadow    = 4,

    /// <summary>Order IV, the Wrought. Sigil-driven automata still executing an order nobody can amend.</summary>
    Construct = 5,

    /// <summary>Order II Swollen — mortals bloated by Essence to the edge of becoming a Herald.</summary>
    Demon     = 6,

    /// <summary>Order II Primordials. Apex things that simply grew vast and are indifferent to everyone.</summary>
    Horror    = 7,

    /// <summary>Massed mortal armies — what Kronarch marshalled, and what is left of it.</summary>
    Legion    = 8,

    /// <summary>The Gauntlet's own lineage: Whelpling, Wyrm, Dragon.</summary>
    Dragon    = 9,
}
