# System 13 — Moderation & Punishment Logging (SPEC, queued)

*Drafted 2026-05-29 · Status: queued, builds after System 12 is merged · Owner: Xolaces*

Implements the Moderator role's actual powers and the binding governance rules from
`docs/DESIGN_NORTHSTAR.md` §6. Depends on System 12 (roles, `ModeratorOrAdmin` policy,
admin tooling, punishment-actor plumbing). Server-only. No UI/Unity.

## Operator requirements captured (verbatim intent)
- Moderator exists mostly for **world chat** and **staff reports**.
- Moderator powers: **chat mute** and **temporary bans up to 3 days**.
- **A reason must be stated before any ban** (and any mute). No reasonless punishments.
- **Staff reports** escalate a **priority report to the admin (Xolaces)**.
- **Every punishment, by any role, against any player, must be logged.**

## Binding rules (non-negotiable)
- Reason is REQUIRED (non-empty) on every mute/ban — validated before the action applies.
- Moderator temp-ban duration is **capped at 72 hours**. Admin may ban for any duration / permanently.
- Moderators may NOT punish Admins or other Moderators, and may not punish themselves.
- Every action writes BOTH the append-only `punishment_log` AND the existing `audit_log`.
- `punishment_log` is append-only — no UPDATE/DELETE DB permission (mirror `audit_log` hardening).

---

## Component A — Domain & schema
1. New enum `PunishmentType { Mute, Unmute, TempBan, PermBan, Unban }`.
2. New entity `PunishmentLog` (table `punishment_log`, append-only): `id`, `actor_player_id`
   (Guid; `Guid.Empty` = system/CLI), `actor_roles` (int snapshot of actor's roles at action time),
   `target_player_id`, `punishment_type`, `reason` (required), `expires_at` (nullable),
   `created_at`. Factory `Create(...)`. Index on `target_player_id` and on `created_at`.
3. `Player` rework (private setters + methods only):
   - Add `BannedUntil (DateTimeOffset?)`, `MutedUntil (DateTimeOffset?)`, `MuteReason (string?)`.
   - Semantics: permanent ban = `IsBanned == true && BannedUntil == null`; temp ban =
     `IsBanned == true && BannedUntil == <future>`. Methods: `ApplyTempBan(string reason,
     DateTimeOffset until)`, `ApplyPermBan(string reason)`, `LiftBan()`, `ApplyMute(string
     reason, DateTimeOffset until)`, `LiftMute()`, and `bool IsCurrentlyBanned(DateTimeOffset
     now)` (true if permanent, or temp not yet expired). Refactor existing `Ban(reason)` →
     `ApplyPermBan(reason)` and update all callers.
4. New entity `PlayerReport` (table `player_reports`): `id`, `reporter_player_id`,
   `target_player_id`, `category` (enum `ReportCategory { ChatAbuse, Cheating, Naming, Other }`),
   `message`, `status` (enum `ReportStatus { Open, Reviewing, Resolved, Dismissed }`),
   `is_priority` (bool), `resolved_by_player_id` (Guid?), `resolution` (string?),
   `resolved_at`, `created_at`, `updated_at`, `is_deleted`. FK indexes on reporter + target.
5. Migrations (Fluent API, snake_case): `AddModerationFields` (player columns),
   `AddPunishmentLog`, `AddPlayerReports`.

## Component B — Enforcement in auth
- `AuthService.LoginAsync` and `RefreshAsync`: replace the `player.IsBanned` check with
  `player.IsCurrentlyBanned(DateTimeOffset.UtcNow)`. If a temp ban has expired, lift it
  (`LiftBan()`) and allow login. Audit the auto-lift.

## Component C — Services (one layer; REST + CLI reuse it)
- `IModerationService` + `ModerationService`:
  - `MuteAsync(actorId, targetIdOrUsername, reason, TimeSpan duration)` — Moderator/Admin.
  - `UnmuteAsync(actorId, target, reason)`.
  - `TempBanAsync(actorId, target, reason, TimeSpan duration)` — Moderator ≤72h enforced; Admin any.
  - `PermBanAsync(actorId, target, reason)` — Admin only.
  - `UnbanAsync(actorId, target, reason)` — Admin only.
  - All: reason required; resolve target by GUID-or-username; enforce the actor/target rank
    rules; revoke target's active refresh tokens on any ban; write `punishment_log` + `audit_log`.
  - `actorId == Guid.Empty` is the trusted system/CLI actor (skips the role re-check, still logged).
- `IReportService` + `ReportService`:
  - `SubmitReportAsync(reporterId, targetIdOrUsername, category, message)` — any player;
    creates `Open`; auto-flags `is_priority` via heuristic (e.g., ≥3 open reports on one target).
  - `ListReportsAsync(ReportStatus?, bool priorityOnly, int take)` — Moderator/Admin.
  - `ResolveReportAsync(actorId, reportId, ReportStatus resolution, string note)` — Moderator/Admin.

## Component D — Controllers
- `ModerationController` `[Authorize(Policy="ModeratorOrAdmin")]`:
  - `POST /api/moderation/players/{idOrUsername}/mute`    `{ reason, durationHours }`
  - `POST /api/moderation/players/{idOrUsername}/unmute`  `{ reason }`
  - `POST /api/moderation/players/{idOrUsername}/tempban` `{ reason, durationHours }` (mod ≤72)
  - `POST /api/moderation/players/{idOrUsername}/ban`     `{ reason }`  — add `[Authorize(Policy="AdminOnly")]`
  - `POST /api/moderation/players/{idOrUsername}/unban`   `{ reason }` — `[Authorize(Policy="AdminOnly")]`
- `ReportController`:
  - `POST /api/reports` `{ targetIdOrUsername, category, message }` — `[Authorize]` (any player)
  - `GET  /api/admin/reports?priority=&status=` — ModeratorOrAdmin
  - `POST /api/admin/reports/{id}/resolve` `{ status, note }` — ModeratorOrAdmin
- FluentValidation on every body: reason NotEmpty, durationHours range (mod 1–72), category/status parse.

## Component E — CLI additions (extend System 12's AdminCli)
- `mute <user|uid> <hours> <reason...>`, `tempban <user|uid> <hours> <reason...>`,
  `ban <user|uid> <reason...>`, `unban <user|uid> <reason...>` — actor `Guid.Empty`, reuse the service.

## Component F — Docs + version
- XML docs on all public members; update `CLAUDE.md` (System 13 entry, migrations, endpoints,
  the `punishment_log` no-UPDATE/DELETE grant note), `changelog.md` (dated), function reference.
- Annotated tag `v0.3.0` (held for operator review).

## Deferred to the world-chat milestone (label `// PHASE-2`)
- Live mute enforcement inside chat (chat reads `MutedUntil`).
- Reports originating from a chat message context.
- Live push of priority reports to the admin (email / SignalR / dashboard). Until then the
  admin polls `GET /api/admin/reports?priority=true`.

## Tests (required)
- Reason empty → rejected, nothing applied. Moderator temp-ban >72h → rejected; Admin >72h → ok.
- Banned player cannot login; expired temp ban auto-lifts on next login.
- Moderator cannot ban/mute an Admin or Moderator; cannot self-punish.
- Every action writes one `punishment_log` row + one `audit_log` row; ban revokes sessions.
- Report submit → Open; 3rd report on a target flips `is_priority`; list filters; resolve sets status.
