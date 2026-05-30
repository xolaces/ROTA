# ROTA — Backend Hardening & Cleanup (pre-Unity) — SPEC

*Drafted 2026-05-29 from the repository audit. Branch: `fix/backend-hardening-pre-unity`.*

Goal: get the backend "in check" before a Unity client is built against it. This is
**stabilization, not features**. Do NOT change any API contract (endpoints, DTOs, status codes)
— a client will soon depend on them. Obey `CLAUDE.md` and `docs/ARCHITECTURE.md`. If anything is
ambiguous or a step is blocked, STOP and report.

## Environment & tool rules (pre-staged for you)
- You are already on branch `fix/backend-hardening-pre-unity`; Docker (Postgres+Redis) is already
  running. Do NOT run `git checkout`/`git branch`/`git switch`/`docker`/`docker-compose`, and never
  use the PowerShell tool.
- Shell commands via the **Bash tool only**, and only these forms (pre-approved): `dotnet build …`,
  `dotnet test …`, `dotnet ef …`, `git add …`, `git commit …`. Read-only `git status`/`git log` are
  fine. Use Write/Edit for files.
- Do NOT touch anything under `.claude/`, transcripts, or settings files. If any tool call is
  blocked, STOP and report `BLOCKED` with the exact step — never work around it or switch tools.
- Per component: edit → `dotnet build` (0 warnings, 0 errors) → `dotnet test` green → ONE
  conventional commit, last line exactly:
  `Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>`
  (use `git commit -F - <<'EOF' … EOF`).
- Do NOT push and do NOT create a git tag — the supervisor does that after review.
- Baseline to preserve: **186 unit + 7 integration = 193 tests, 0 warnings.**

## STEP 0 — Verify & report ONLY (no code changes)
Produce a short written report in your final message; change nothing here:
1. **Content volume** — open `src/ROTA.Api/content/{quests,raids,items,loot_tables}.json` and report
   counts: zones, quest nodes, boss nodes, items, loot tiers. (Answers "is the core loop playable?")
2. **Reward atomicity** — trace the quest attempt path (`QuestService`) and the raid hit/kill path
   (`RaidService`): is the energy/stamina spend in the **same DB transaction** as the gem/XP/loot
   grant, or separate? Quote the relevant lines/method names. Do NOT fix it — this only scopes a
   future change.

## Component A — Remove dead test stub
1. Delete `tests/ROTA.IntegrationTests/UnitTest1.cs` (obsolete 1-line stub:
   `// Deleted — replaced by RaidConcurrencyTests.cs.`).
2. `dotnet build` + `dotnet test` green. Commit: `chore: remove obsolete integration test stub`.

## Component B — Continuous Integration
Add `.github/workflows/ci.yml` (no CI exists today; target GitHub Actions):
- Triggers: `push` and `pull_request` to `main`.
- Runner: `ubuntu-latest` (has Docker, so Testcontainers integration tests run).
- Steps: `actions/checkout` → `actions/setup-dotnet` pinned to the .NET 10 SDK → `dotnet restore`
  → `dotnet build --configuration Release --no-restore` → `dotnet test --configuration Release
  --no-build`.
- No deploy steps, no secrets. Ensure the YAML is valid and indentation is correct.
- Commit: `ci: add build + test GitHub Actions workflow`.

## Component C — Remove Player factory duplication
`Player.Create(username,email,passwordHash)` and `Player.CreateWithId(id,…)` duplicate the entire
initialization block. Refactor `Create` to delegate:
`public static Player Create(string username, string email, string passwordHash) => CreateWithId(Guid.NewGuid(), username, email, passwordHash);`
Behavior must be byte-for-byte identical (Roles=Player, DisplayName=username, Stats + 3 resource
pools seeded, same defaults). `dotnet test` green (existing Player/seed/auth tests must still pass).
Commit: `refactor(domain): Player.Create delegates to CreateWithId (remove duplication)`.

## Component D — Dev-only auto-migrate at startup
Today the web host does not migrate at startup, so a fresh DB crashes the seeder (`42P01 relation
"players" does not exist`). Make dev/test self-bootstrapping WITHOUT changing production semantics:
1. In `src/ROTA.Api/Program.cs`, after `var app = builder.Build();` and BEFORE
   `await SeedData.EnsureAdminAsync(app.Services);`, add a guarded block:
   if `app.Environment.IsDevelopment()`, open a scope, resolve `RotaDbContext`, and
   `await db.Database.MigrateAsync();`. Production stays explicit (operators run migrations first).
2. Update `docs/OPERATIONS.md`: §4 (note Development auto-migrates on startup) and §3/§8 (resolve
   the "open decision" wording — Development auto-migrates; Production requires explicit
   `dotnet ef database update` before run).
3. Run `dotnet test` — integration tests run as Development, so the host auto-migrate + their own
   `MigrateAsync` are both idempotent; all must still pass.
4. Commit: `fix(api): auto-migrate the database on startup in Development only`.

## FINAL — Verify & hand back (no push, no tag)
- `dotnet build` (0 warnings) + `dotnet test` (all green) — paste the summary lines.
- Startup smoke test via the CLI: `dotnet run --project src/ROTA.Api -- gen-beta-key 1` — confirm it
  prints a key and exits with **no EF model-validation warnings** in the output.
- Report: the Step-0 findings, `git log --oneline` for your commits, every file changed, and any
  deviation or uncertainty.

## Explicitly OUT OF SCOPE (do not attempt)
- Energy-regen rewire to `ClassConfig` — gameplay-semantic; defer to a future spec informed by Step 0.
- Reward-step atomicity changes — only *report* in Step 0; the fix is a separate, tested change.
- Pushing to origin or tagging a release — supervisor actions after review.
- ANY endpoint/DTO/status-code change — a Unity client will depend on the current API contract.
- Deleting `scaffold.sh` or `dotdhistoryresearchpaper.pdf` — leave them; mention as archive
  candidates only if relevant.
