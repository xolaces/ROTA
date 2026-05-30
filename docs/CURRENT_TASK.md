# ROTA — Current Task

*Updated 2026-05-30. Short by design — answers "what now?"*

## Just completed
- **v0.2.0** Beta Access Control · **v0.2.1** hardening (CI, dev auto-migrate, cleanup).
- **v0.2.2 (pre-UI):** class-based regen (energy/stamina/guild from ClassConfig; GuildStamina now
  regens); RaidSize set Personal/Small/Medium/Large/Titanic with caps 1/10/25/50/250; raid on-hit
  XP (1–4/stam roll) + gold + per-hit raid-log fields. 207 tests green. Merged + tagged + pushed.

## Needs your eyes (balance, not blocking)
Regen pacing is now class-driven: **Conscript = 5 min/point** energy & stamina (~10× slower than the
old placeholder). All values live in `appsettings.json → ClassConfig.RegenMinutesPerPoint` and are a
one-line tweak. Confirm or adjust.

## Next (in order)
- **v0.2.3 — Discernment crit (raids):** +crit chance (hard cap +10%; base ~5%) and Dawn-style crit
  damage, applied in the raid damage formula. Confirm the exact curves before building. Quest
  drop-quality from discernment stays "later."
- **v0.3.0 — C# API client SDK:** testable HTTP client + shared DTOs covering every endpoint — the
  layer the Unity client consumes. You wire scenes/UI in the Editor (Unity not runnable here).

## Deferred / back-burnered
Reward-atomicity rework of the stamina spend · moderation (no chat/mods/players) · per-raid
`xpPerStamina` · guild/gauntlet/gacha/equipment.

## Hard rule (learned)
Build-green ≠ correct. After any agent build: run the app, trace behavior-touching changes, smoke-test
the CLI. This session that caught the EF data-corruption class of bug.
