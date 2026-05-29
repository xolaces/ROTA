ROTA - Personal Changelog & Learning Notes
==========================================
Started: 2026-05-20
Developer: xolaces
Stack: C# / ASP.NET Core / PostgreSQL / Redis / Unity (client, later)

==========================================
PHASE 0 - FOUNDATION
==========================================

WHAT WE ARE BUILDING
---------------------
Rise of the Ancients (ROTA) is a Dawn of the Dragons-style fantasy RPG.
The backend is a secure game server built in C#. Unity will be the game
client later. Right now everything is backend - the server that runs the
game logic, stores player data, and enforces all the rules.

The core philosophy: the Unity client is never trusted. The server decides
everything. The client only tells the server what the player wants to do.
The server decides what actually happens and sends the result back.

------------------------------------------
MILESTONE 1 - Project Scaffold
------------------------------------------
Status: Complete
Commit: [INIT] Phase 0 scaffold

WHAT WAS BUILT:
  - ROTA.sln              The solution file - ties all 7 projects together
                          so Visual Studio loads them as one workspace.

  - scaffold.sh           A shell script that automates creating the entire
                          project structure with one command.

  - docker-compose.yml    Defines the local development environment.
                          Runs PostgreSQL (database) and Redis (cache/rate
                          limiting) inside Docker containers on your machine.
                          You never install PostgreSQL or Redis directly.

  - .env.example          Template for your local secrets file. The real
                          .env holds your actual passwords and is never
                          committed to Git.

  - .gitignore            Tells Git which files to never track. Secrets,
                          build output, and Visual Studio temp files.

THE 5 SOURCE PROJECTS AND WHAT THEY DO:
  - ROTA.Api              The front door. Receives HTTP requests from the
                          Unity client. Handles middleware, routing, and
                          passes work to the Application layer. Never
                          contains game logic.

  - ROTA.Application      The rules layer. All game logic lives here.
                          "Can this player attempt this quest right now?"
                          "Does this player have enough energy?" This layer
                          makes those decisions.

  - ROTA.Domain           The things layer. Plain C# classes representing
                          game objects - Player, Quest, Dragon, Raid. No
                          database code, no web code. Just the objects.

  - ROTA.Infrastructure   The database layer. The only place that talks to
                          PostgreSQL and Redis. Everything else is shielded
                          from knowing how data is stored.

  - ROTA.Shared           Common vocabulary. DTOs (data transfer objects),
                          constants, and helpers used across all layers.

THE 2 TEST PROJECTS:
  - ROTA.UnitTests        Tests individual pieces of logic in isolation.
                          Fast. No database needed.

  - ROTA.IntegrationTests Tests the whole stack together. Spins up a real
                          test database via Docker to verify everything
                          works end to end.

KEY CONCEPT - PROJECT REFERENCES:
  Project references enforce architecture through the compiler.
  ROTA.Domain cannot import anything from ROTA.Infrastructure.
  If someone tries, the build fails. The rules are baked in.

  Allowed flow: Api -> Application -> Domain
                Infrastructure -> Domain (reads entities, never owns them)

------------------------------------------
MILESTONE 2 - Domain Entities
------------------------------------------
Status: Complete

WHAT WAS BUILT:
  These are plain C# classes in ROTA.Domain. No database code inside them.
  EF Core configuration files in ROTA.Infrastructure do the DB mapping.

  Player.cs
    Who the player is. Username, email, password hash, level, experience,
    gold, guild membership, ban status. Gem balance is NOT stored here -
    it is computed from the gem transaction ledger to prevent exploits.

  PlayerStats.cs
    The player's base combat stats. Attack, defense, max health, current
    health. These are BASE values only. Effective stats (with item bonuses,
    dragon bonuses, guild bonuses) are computed fresh every time they are
    needed and never stored. Storing computed stats causes desync exploits.

  PlayerResource.cs
    Generic resource pool. One row per resource type per player.
    Current types: Energy, Stamina, GuildStamina.
    Adding a new resource type = adding one line to the ResourceType enum.
    No schema changes, no new classes needed.
    CurrentValue is a checkpoint, not a live value. The server always
    recomputes actual value from LastRegenAt timestamp.

  ResourceType.cs (enum)
    Defines all resource pool types. Lives in ROTA.Domain/Enums/.
    Energy = 1, Stamina = 2, GuildStamina = 3

  RefreshToken.cs
    Tracks active login sessions. When a player logs in they get a JWT
    access token (15 min) and a refresh token (7 days). The raw token is
    never stored - only a hash of it. Max 3 active sessions per player.
    On every refresh the old token is immediately revoked.

  AuditLog.cs
    Append-only record of everything that happens on the server. Every
    state change writes an entry regardless of success or failure.
    The database role has no UPDATE or DELETE permission on this table.
    Used for security review, support, and anomaly detection.

KEY CONCEPT - PRIVATE SETTERS:
  All entity properties use { get; private set; }
  This means only code inside the class can change the values.
  External code (services, controllers) can read but never directly modify.
  Changes go through methods on the entity that can enforce rules.
  This prevents accidental or malicious data corruption.

------------------------------------------
MILESTONE 3 - EF Core Infrastructure
------------------------------------------
Status: Complete

WHAT WAS BUILT:
  EF Core is the ORM (Object Relational Mapper). It translates between
  C# objects and PostgreSQL tables so you write C# instead of raw SQL.

  RotaDbContext.cs
    The main database session. Every database operation goes through this.
    Holds a DbSet for each entity - think of each DbSet as a handle to
    that entity's table. Phase 1 entities are commented out and will be
    uncommented as each system is built.

  Configuration files (one per entity):
    PlayerConfiguration.cs
    PlayerStatsConfiguration.cs
    PlayerResourceConfiguration.cs
    RefreshTokenConfiguration.cs
    AuditLogConfiguration.cs

    Each configuration file maps a C# class to a PostgreSQL table using
    Fluent API. Defines: table name, column names, max lengths, defaults,
    indexes, foreign keys, and unique constraints. No mapping code touches
    the domain entities - it all lives here in Infrastructure.

KEY CONCEPT - FLUENT API VS DATA ANNOTATIONS:
  There are two ways to configure EF Core mappings.
  Data annotations put attributes directly on the entity class like
  [MaxLength(32)] or [Required]. We do not use these.
  Fluent API puts all configuration in separate files in Infrastructure.
  This keeps domain entities clean and means database concerns never
  bleed into game logic code.

KEY CONCEPT - MIGRATIONS:
  A migration is a snapshot of your data model at a point in time.
  When you run: dotnet ef migrations add InitialCreate
  EF Core reads your entity configurations and generates a C# file
  describing every table, column, index and constraint.
  Running: dotnet ef database update
  Applies that migration to your actual PostgreSQL database, creating
  the real tables. Every future schema change gets its own migration file.
  Your database history is version controlled alongside your code.

------------------------------------------
MILESTONE 4 - API Pipeline
------------------------------------------
Status: Complete

WHAT WAS BUILT:
  Program.cs
    The entry point of the server. Every request passes through this file
    in the order the middleware is registered. Order is security-critical.

  MIDDLEWARE PIPELINE (in order):
    1. Exception Handler    Any unhandled error is caught here. Raw error
                            messages and stack traces never reach the client.
                            Attackers use stack traces to map your system.

    2. HTTPS Redirect       Forces all traffic to HTTPS in production.

    3. Request Logging      Logs method, path, status code, duration.
                            Never logs tokens, passwords, or JWT payloads.
                            (Stub - full Serilog logging added in Phase 1)

    4. CORS                 Controls which origins can call the API.
                            Loose in dev, strict in production.

    5. Rate Limiting        Rejects requests that exceed per-player or
                            per-IP limits. Runs BEFORE JWT validation
                            intentionally - bots get dropped before the
                            server does any expensive work.
                            (Stub - Redis implementation in Week 2)

    6. Routing              Matches the URL to the right endpoint.

    7. Authentication       Reads and validates the JWT token. Sets the
                            player's identity on the request context.

    8. Authorization        Checks [Authorize] attributes on endpoints.
                            Must run after Authentication.

    9. Audit Logging        Writes to audit_log for state-changing requests.
                            Runs after auth so PlayerId is available.
                            (Stub - full implementation in Week 2)

    10. Endpoints           The actual controllers and SignalR hubs.

  JWT SECURITY NOTE:
    We use RS256 (asymmetric) not HS256 (symmetric).
    RS256 uses a private key to sign tokens and a public key to verify.
    Future microservices can verify tokens without ever seeing the private
    key. HS256 uses one shared secret - if it leaks, everything is compromised.
    Algorithm is explicitly whitelisted to block algorithm confusion attacks.

  RequestLoggingMiddleware.cs   (stub)
  RateLimitMiddleware.cs        (stub)
  AuditLogMiddleware.cs         (stub)

------------------------------------------
PACKAGES INSTALLED
------------------------------------------

ROTA.Infrastructure:
  Microsoft.EntityFrameworkCore        ORM - C# to PostgreSQL translation
  Npgsql.EntityFrameworkCore.PostgreSQL  PostgreSQL driver for EF Core
  Microsoft.EntityFrameworkCore.Design  EF Core CLI tooling support
  StackExchange.Redis                  Redis client for .NET

ROTA.Application:
  FluentValidation                     Input validation library
  FluentValidation.DependencyInjectionExtensions  DI integration

ROTA.Api:
  Microsoft.AspNetCore.Authentication.JwtBearer  JWT middleware
  Swashbuckle.AspNetCore               Swagger UI for testing endpoints
  Microsoft.EntityFrameworkCore.Design  Required for EF migrations CLI
  Microsoft.EntityFrameworkCore.Relational  Pinned to resolve version conflict

ROTA.UnitTests:
  Moq                                  Creates fake service implementations
  FluentAssertions                     Makes test assertions readable

ROTA.IntegrationTests:
  Microsoft.AspNetCore.Mvc.Testing     In-memory test server
  Testcontainers.PostgreSql            Spins up real PostgreSQL for tests
  FluentAssertions                     Makes test assertions readable

------------------------------------------
SECRETS CONFIGURED (local only, never in Git)
------------------------------------------
  ConnectionStrings:DefaultConnection  PostgreSQL connection string
  ConnectionStrings:Redis              Redis connection string
  Jwt:PublicKey                        (Week 2 - not set yet)
  Jwt:PrivateKey                       (Week 2 - not set yet)

==========================================
UP NEXT - PHASE 0 WEEK 2
==========================================
  - Run InitialCreate migration to create tables in PostgreSQL
  - Generate RS256 key pair for JWT
  - Build auth endpoints: register, login, refresh, logout
  - Implement real rate limiting middleware with Redis
  - Implement real audit log middleware
  - Write first unit tests for auth flows

==========================================
SYSTEM 12 - BETA ACCESS CONTROL (2026-05-29)
==========================================

WHAT WAS BUILT
--------------
This system is pure server infrastructure. No game mechanics changed.
The goal was two things: add a role system to tell apart regular players
from moderators and admins, and add a beta key system to control who can
register during the closed beta period.

COMPONENT A - ROLE SYSTEM
--------------------------
Players now have a Roles property that works like flags on a bit field.
The value is a single integer column in the database but it can hold
multiple roles at once using bitwise OR.

  PlayerRoles.None       = 0  (never used in practice)
  PlayerRoles.Player     = 1  (all registered accounts, permanent)
  PlayerRoles.Moderator  = 2  (can use mod tools)
  PlayerRoles.Admin      = 4  (full access)

The Player flag can never be removed. Revoking it is a no-op by design
because a registered player is always a player. Admin and Moderator can
be granted and revoked freely.

Every access token (JWT) now includes one role claim per flag the player
has. ASP.NET Core's [Authorize(Roles="Admin")] reads these claims to
enforce access at the controller level.

Migration applied: AddPlayerRolesAndDisplayName
Also added DisplayName (varchar 48) so players have a visible in-game name
that is separate from their login username.

COMPONENT B - SINGLE-USE BETA KEY GATE
----------------------------------------
Registration is now gated by a beta key when BetaGate:Enabled = true.

Key format: ROTA-XXXX-XXXX-XXXX using Crockford base32 (no 0/O/1/I/L
characters that look alike). Keys are generated by admins and each key
can only be used once.

The concurrency problem: what if two players try to register with the
same key at the exact same time? The naive approach - read, check, update
- fails under concurrent load because both reads can happen before either
update. Solution: a single SQL UPDATE with a WHERE clause that acts as
both the check and the claim in one atomic database operation.

  UPDATE beta_keys
     SET is_redeemed = true, redeemed_by_player_id = @p, redeemed_at = now()
   WHERE key = @k AND is_redeemed = false AND is_deleted = false

PostgreSQL guarantees only one concurrent UPDATE can match. The winner
gets rowsAffected = 1. The loser gets 0 and knows they lost the race.

The key is claimed INSIDE a database transaction with player creation.
If player creation fails for any reason, the transaction rolls back and
the key is returned to the pool automatically.

Migration applied: AddBetaKeys

COMPONENT C - ADMIN SEED
--------------------------
SeedData.EnsureAdminAsync runs at startup, before the HTTP server accepts
any requests. It checks if "Xolaces" exists. If not, it reads the admin
password from Seed:AdminPassword in configuration (never hardcoded) and
creates the account with Admin + Player roles.

If Seed:AdminPassword is not configured, it logs a warning and returns
gracefully. No throw, no crash. This is intentional: you set the password
once to bootstrap, then you can remove it from config.

COMPONENT D - ADMIN REST ENDPOINTS
-------------------------------------
Four new admin-only endpoints under /api/admin/:

  POST /api/admin/players/{idOrUsername}/roles/grant   grant a role
  POST /api/admin/players/{idOrUsername}/roles/revoke  revoke a role
  POST /api/admin/beta-keys                            generate N keys
  GET  /api/admin/beta-keys                            list all keys

The grant/revoke endpoints accept either a GUID or a username to identify
the target. The server first tries to parse as a GUID. If that fails, it
looks up by username.

Safety guards:
- Actor must be Admin in the DB (not just in the JWT) unless it is the
  CLI calling with Guid.Empty as the actor.
- Player base role can never be revoked.
- Last admin protection: if only one admin exists, demoting them is
  rejected. There must always be at least one admin.
- When Admin or Moderator is revoked, all the player's active refresh
  tokens are revoked immediately. Their next API call will require a
  fresh login, at which point the JWT will no longer include the revoked
  role claim.

COMPONENT E - BOOTSTRAP CLI
------------------------------
AdminCli.cs lets you run admin operations directly from the command line
without starting the HTTP server.

  seed-admin                  create the Xolaces admin account
  gen-beta-key [count]        generate 1 to 100 beta keys
  promote {user|guid} {Role}  grant a role
  demote  {user|guid} {Role}  revoke a role

These commands reuse the exact same service layer as the REST endpoints.
No logic is duplicated. The CLI calls IAdminService.GrantRoleAsync and
IAdminService.RevokeRoleAsync just like the controller does.

TEST COUNT
----------
Before System 12: 156 unit tests + 1 integration test = 157 total
After System 12:  185 unit tests + 3 integration tests = 188 total

New tests cover: beta gate registration paths, concurrent key redemption
race condition (integration), admin seed idempotency, role grant/revoke
with all guards, session revocation on role removal, JWT role claims.