# ROTA Session Handoff — 2026-06-07

## TL;DR
**System 16 Gauntlet is COMPLETE + playable** (7 commits). **System 21 Guild fundamentals S1 (core/membership/join) + S2 (chat) are done** (2 commits). Remaining work = **Guild S3a (sigil economy)** then **S3b (guild raids)** — design is LOCKED below, ready to build. All work is on stacked feature branches off `main` (`b9c2dbd`). See **§Git** for merge/push state. Build is green throughout (0 errors; only 4 pre-existing MSB3277 JWT-version warnings in the test projects). Migrations are added but **NOT applied** — owner coordinates `dotnet ef database update`.

---

## DONE THIS SESSION

### System 16 — Gauntlet (COMPLETE, end-to-end playable) — 737 tests green
Stacked branches off main: `feat/system16-gauntlet-s1-content → -s2-ledgers → -s3-leaderboard → -s4-combat → -s5-settlement → -s6-shop → -s7-loop`.
Commits: `c13fcfa, 3e0e113, c6ca0d0, 999a897, a6ee3d3, a3525d9, ef47c54`. Migration **AddGauntletSystem** (one consolidated; not applied).
- **S1** enums + GauntletConfig + content (gauntlet_prizes/trophies/raids JSON + 2 off-cap magics in magics.json) + IGauntletContentProvider (startup-validated).
- **S2** 7 entities (GauntletEvent, GauntletEntry, StrikeTransaction + GauntletCurrencyTransaction append-only ledgers, PlayerGauntletTrophy/PlayerEventMagic/PlayerMagicHonor) + repos + IGauntletService (join w/ league-lock by convergence tier; gem→strikes buy w/ idempotency-key) + IGauntletAdminService (open ≤1-active / close / settle) + GauntletController + GauntletAdminController + CLI `gauntlet-open/close/settle`.
- **S3** IGauntletScoringService (UpdateScoreAsync atomic SQL increment, ambient-tx-aware; RecomputeRanksAsync per-league ROW_NUMBER snapshot; GetLeaderboardAsync top-200+caller) + GauntletRankSnapshotService (hosted ~60s) + `GET /api/gauntlet/leaderboard`.
- **S4 (DEEP combat)** in `RaidService.HitRaidAsync`, gated on `ActiveRaid.GauntletEventId`, NO parallel path: (A) trophy mult on rawLegionPower highest-only before PowerScaling (every raid); (B) off-cap Wrath/Blessing auras (current owner ×1.25 / former-honor ×1.10), outside MaxAggregateProcBonus, before crit; (C) strike-spend fork (Gauntlet hits spend Strikes not Stamina; `StrikeRepository.SpendAsync` reimplemented tx-safe raw-SQL, no ChangeTracker.Clear); (D) score-update hook. A trophy-less non-Gauntlet hit is byte-identical to before.
- **S5** idempotent settlement (tokens/pitchfork/trophies via ledger+unique-index, honor write-back on PlayerEventMagic revoke) + per-defeat strike+token rewards. Settle-twice-pays-once (proven vs real ledgers).
- **S6** token shop: GauntletShopProvider (startup-validated) + BuyFromShopAsync (tri-state spend, per-kind idempotency, Token-vs-Pitchfork isolation). `GET/POST /api/gauntlet/shop`.
- **S7 (loop completion)** gauntlet stages resolve as RaidDefinitions (RaidDefinitionProvider also loads gauntlet_raids.json → HitRaidAsync unchanged); `GET /api/gauntlet/ladder` auto-advance lazy spawn (stage 1 on entry, next stage after defeat, Personal+stamped); OpenEventAsync rank-magic consumable hand-off to prior settled event's rank winners (idempotent).

### System 21 — Guild (S1 + S2) — 602 tests green
Branches off main: `feat/system21-guild-s1-core → -s2-chat`. Commits: `a473426, db83faa`. Migration **AddGuildSystem** (not applied).
- **S1** Guild/GuildMembership/GuildJoinRequest + 4 enums (GuildRank Member/Officer/Leader, GuildJoinPolicy Open/Application/InviteOnly, GuildJoinRequestKind, GuildJoinRequestStatus) + GuildConfig + GuildService (create [gold+L20 gate], disband [leader-only, releases members], per-guild join policy, apply/accept/reject, invite/accept-invite, leave [leader can't], kick, promote/demote, transfer, RunInactivitySuccessionAsync, roster, browse) + GuildController (14 endpoints) + DTOs + validators. One-guild-per-player = partial unique index on `guild_memberships.player_id WHERE is_deleted=false`; CI name/tag uniqueness via normalized shadow columns; permission rule `actor.Rank>target.Rank AND newRank<actor.Rank` (⇒ only Leader changes ranks). Player.GuildId/GuildRank kept in sync.
- **S2** ChatHub guild channel (JoinGuildChannel/LeaveGuildChannel/SendGuildMessage; mute-gate then member-gate; per-guild group) + IGuildChatStore/RedisGuildChatStore (per-guild 100-msg ring buffer `chat:guild:{id}`) + `GET /api/chat/guild/history`. Additive — world/raid chat unchanged. Unity SignalR client deferred.

---

## NEXT: Guild S3 — DESIGN LOCKED (build S3a, then S3b)

**Owner-set (2026-06-07):** guild-shop-ticket source = **daily ticket grant** (small per-member daily allowance, ~enough for 3 sigil buys/day; placeholder until a real guild currency ships); guild-raid bosses = **NEW guild-specific content** (author e.g. `content/guild_raids.json`, distinct from raids.json; reuse the raid engine); **1 sigil per summon**.

**Structural (locked — see memory `guild-foundations-decisions.md` + spec §5):**
- **Sigil flow:** per-player guild-sigil balance (daily claim + shop buy) → **donate to a guild POOL** → officer summons draw from the pool.
- **Daily caps:** claim **1**/day, buy **≤3**/day, donate **≤3**/day per player; reset at **UTC midnight** (reuse gem `daily:{yyyy-MM-dd}` idempotency).
- **Model:** guild sigil pool = guild-scoped counter; per-player sigil balance + per-player guild-shop-ticket balance = lightweight per-player ledgers/counters (NOT a full inventory ItemType).
- **Guild raids:** officer-gated summon consumes **1** pooled sigil; **all guild members** can hit; **GuildStamina/hit = hit size (1/5/20)** (first GuildStamina sink); **contribution-tier rewards via the existing raid engine** + `GuildMembership.ContributionTotal` accrual; scope via new `active_raid.guild_id` (nullable FK + index).

### S3a — Guild sigil economy
Per-player guild-sigil balance + guild sigil pool + per-player guild-shop-ticket balance (counters/ledgers, idempotent daily refs). Endpoints: daily claim (1/day, idempotent `guildclaim:{playerId}:{date}`), daily ticket grant, shop buy sigils (≤3/day, spends tickets), donate (≤3/day, personal→pool), balances. Audit. Tests: daily caps enforced + idempotent; donation moves personal→pool; ticket spend.

### S3b — Guild raids
`content/guild_raids.json` (new guild bosses) + `active_raid.guild_id` (migration) + officer-gated summon (consumes 1 pooled sigil; reuse the raid summon/engine scoped to guild) + guild-member hit gate + GuildStamina spend (mirror the Gauntlet S4 strike-fork: for guild raids spend `IEnergyService.SpendEnergyAsync(playerId, ResourceType.GuildStamina, hitSize)` instead of Stamina, inside the advisory-lock tx) + contribution-tier rewards (existing engine) + `GuildMembership.ContributionTotal` accrual + guild-raid list endpoint. **Reuse RaidService.HitRaidAsync — NO parallel combat path** (exactly like Gauntlet via GauntletEventId; here gate on guild_id). Tests: access gate, GuildStamina spend/insufficient, contribution accrual, reward idempotency, summon consumes a pooled sigil.

---

## GIT / MERGE STATE
Two independent stacks, both off `main` (`b9c2dbd`); they share ZERO tables but BOTH regenerated `RotaDbContextModelSnapshot.cs` and both edited `ServiceCollectionExtensions.cs`, `RotaDbContext.cs`, `Program.cs`, `appsettings.json`, `docs/ROTA_Function_Reference.md`, `docs/PROJECT_STATE.md` — so a Gauntlet→main FF then Guild→main merge **conflicts on those files (all additive — take BOTH sides)**.

**Recommended integration (EF-correct):**
1. `git checkout main && git merge --ff-only feat/system16-gauntlet-s7-loop` (clean FF — main = Gauntlet).
2. Merge Guild **code** (`git merge feat/system21-guild-s2-chat`); resolve additive conflicts by keeping BOTH sides. For the **model snapshot**, the safe path is: keep main's (Gauntlet) snapshot, delete `Migrations/*_AddGuildSystem.*`, reset the snapshot, then `dotnet ef migrations add AddGuildSystem` (regenerates the union against main+Guild-entities). The two `Up()`s are independent + additive, applied in timestamp order.
3. `dotnet build` (0 errors) + `dotnet test` (Docker up).
4. Tag per slice (prior scheme `v0.2.8-lb-s*`; suggest `v0.2.9-gauntlet-s1..s6` + `-loop`, `v0.3.0-guild-s1..s2`).
5. Push `main` + tags + feature branches.

**>>> ACTUAL STATE AT HANDOFF: see the orchestrator's final chat message. If main was not fully merged, follow the steps above. <<<**

---

## KNOWN FOLLOW-UPS / DEBT
- **CLAUDE.md not yet updated** with System 16/21 "Current build status" entries (FR + PROJECT_STATE ARE updated). Add them.
- **Gauntlet ladder double-spawn race:** `GauntletService.GetLadderAsync` spawns without a per-player lock; a rapid double-call after a defeat could spawn duplicate stages (minor score-farm). Harden with an advisory lock or a partial unique index on the active stage per (event, player).
- **Gauntlet finite 6-stage ladder ceiling** — tunable; add stages or a formula-extension for deeper climbs.
- **Guild inactivity-succession auto-driver** — `RunInactivitySuccessionAsync` exists; needs a scheduled hosted-service trigger.
- **Tunable balance values to confirm:** GuildConfig.CreationGoldCost=25000; GuildConfig daily caps; GauntletConfig.StrikeGemPrice=1; gauntlet HP curve; S3a daily ticket allowance.
- **Migrations NOT applied:** AddGauntletSystem, AddGuildSystem — coordinate `dotnet ef database update`.
- **Multi-step guild ops** (create) not in one DB tx; benign cap-check TOCTOU — same Phase-2 patterns as gem-buy / raid-cap races.
- **4 pre-existing MSB3277 JWT-version warnings** in test projects — pre-existing, unrelated.

## WORKING CONVENTIONS (kept this session)
- Per slice: own branch (stacked), `dotnet build` 0 warnings + `dotnet test` green (Docker: `docker compose up -d`), one commit, **NO co-author trailer**.
- Heavy subagent use for builds; the orchestrator reviews EVERY diff (esp. combat + idempotency = money-path) and re-runs build+test before committing.
- Server-authoritative; private setters / no EF attributes; Fluent + snake_case; ledgers append-only (created_at only); FKs indexed; partial unique index where soft-deleted rows recur; idempotent ledgers (referenceId); audit every state change; thin controllers (PlayerId from JWT `sub`); `HasSentinel` only for enum columns with a non-zero store default.
- LF→CRLF git warnings on Windows are benign.
- READ FIRST next session: this file, `docs/specs/active/system-21-guild-foundations.md` (§5 LOCKED), `docs/ROTA_Function_Reference.md`, memory `guild-foundations-decisions.md`. Confirm `git log`/`git branch` before trusting anything.
