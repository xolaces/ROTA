# ROTA — Current Task

*Updated 2026-06-13. Pointer + snapshot. **Canonical: [SESSION_HANDOFF.md](SESSION_HANDOFF.md).***

## Snapshot
- **v0.3.1 SHIPPED + PUSHED** (backend `main` @ `665f903`, tag `v0.3.1`; client `master` @ `cc87b1f`,
  local-only — bundle at `C:\Dev\ROTA.Client6-v0.3.1.bundle`). 972 unit + 111 integration green.
- v0.3.1 = Obsidian Gilt UI swap · transparent class tier emblems + header/profile badges · gem balance ·
  zone-scoped quest difficulty + Pano chase curve · **Gauntlet battalion (System 24 D8)** backend +
  full-replace combat fork.
- **⚠ Migration `AddGauntletBattalion` NOT applied** — run `dotnet ef database update --project
  src/ROTA.Infrastructure --startup-project src/ROTA.Api`.
- No committed-but-unfinished work.

## What now (priority order)
1. **Client: wire the battalion editor to `GET`/`PUT /api/gauntlet/battalion`** (DTOs
   `GauntletBattalionResponse`/`SetGauntletBattalionRequest`; server-computed power) — IRotaApi/Http/Mock.
2. **Obsidian Gilt slice 2** — Quest/Raid/Gauntlet detail surfaces adopt the rounded template.
3. **Deferred test** — positive battalion-damage integration test through `HitRaidAsync`.
4. **Follow-ups** — live rail notif-dot data; daily-reward claim backend; lore→items content phase.
