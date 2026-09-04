using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;
using ROTA.Api;
using ROTA.Api.BackgroundServices;
using ROTA.Api.SignalR;
using ROTA.Infrastructure.Persistence;
using ROTA.Api.Middleware;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Application.Configuration;
using ROTA.Application.Models;
using ROTA.Domain.Enums;
using ROTA.Infrastructure;
using ROTA.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Response compression (Brotli + Gzip, optimal level). EnableForHttps so payloads compress on
// the TLS path the clients actually use. Caddy already gzips at the edge for the proxied path,
// so this mainly benefits direct/WebGL clients that hit Kestrel without going through Caddy.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Optimal);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Optimal);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "ROTA API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new()
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your JWT access token."
    });
    options.AddSecurityRequirement(new()
    {
        {
            new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("RotaPolicy", policy =>
    {
        if (builder.Environment.IsDevelopment() && allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .WithMethods("GET", "POST", "PUT", "DELETE")
                  .AllowCredentials();
        }
    });
});

// JWT Authentication (RS256)
// SECURITY: RS256 uses asymmetric keys. Private key signs tokens (server only).
// Public key verifies them. HS256 is banned - shared secret is a single point of compromise.
var rsaPublicKey = RSA.Create();
// PEM may arrive single-line with literal "\n" (a Docker .env can't hold real newlines) — normalize first.
rsaPublicKey.ImportFromPem(ROTA.Application.Configuration.PemKey.Normalize(builder.Configuration["Jwt:PublicKey"]));

// The token issuer and the token validator read the SAME two settings, so a missing one does not
// disagree — it disables the claim on both sides at once, in opposite directions. Issuance omits a
// null iss/aud entirely; validation still demands them because ValidateIssuer/ValidateAudience are
// true. The result is an API where every login returns 200 with a real token and every authenticated
// request that follows returns 401, with nothing logged anywhere to say why. Both are plain
// identifiers rather than secrets and now ship with defaults, so this can only be reached by
// explicitly blanking them; it is still worth failing at boot rather than at the first request.
foreach (var (key, value) in new[]
         {
             ("Jwt:Issuer",   builder.Configuration["Jwt:Issuer"]),
             ("Jwt:Audience", builder.Configuration["Jwt:Audience"]),
         })
{
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException(
            $"{key} is not configured. The API signs tokens with it and validates tokens against it, "
            + "so leaving it blank produces tokens this same process will reject — every login "
            + "succeeds and every authenticated request then fails with 401.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // SECURITY/CORRECTNESS: keep JWT claim types verbatim. By default the handler remaps
        // short standard claims (e.g. "sub" -> ClaimTypes.NameIdentifier), which would make
        // every controller's User.FindFirst("sub") return null -> NullReferenceException on
        // GetPlayerId for all authenticated endpoints. Roles are emitted as ClaimTypes.Role
        // already, so role/policy checks are unaffected.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new RsaSecurityKey(rsaPublicKey),
            // SECURITY: explicitly whitelist RS256 - blocks algorithm confusion attacks
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            // SECURITY: zero clock skew - tokens expire exactly when they say they do
            ClockSkew = TimeSpan.Zero
        };

        // SignalR requires JWT via query string - WebSocket handshake cannot set headers
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

// Admin allowlist — kept as a break-glass fallback when the DB role cannot be used.
// Primary check is the "Admin" role claim in the JWT (from Player.Roles flags).
// Populate via user secrets or environment: Admin:PlayerIds:0 = "<guid>"
var adminPlayerIds = builder.Configuration
    .GetSection("Admin:PlayerIds")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddAuthorization(options =>
{
    // "AdminOnly": role claim primary; config allowlist is a break-glass fallback.
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx =>
                  ctx.User.IsInRole(nameof(PlayerRoles.Admin))
                  || adminPlayerIds.Contains(
                      ctx.User.FindFirst("sub")?.Value ?? "",
                      StringComparer.OrdinalIgnoreCase)));

    options.AddPolicy("ModeratorOrAdmin", policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx =>
                  ctx.User.IsInRole(nameof(PlayerRoles.Admin))
                  || ctx.User.IsInRole(nameof(PlayerRoles.Moderator))));
});

builder.Services.AddSignalR();

// Chat/PM delivery (T35/36/37) keys SignalR's per-user identity on the JWT "sub" claim
// (MapInboundClaims is off, so sub is not remapped to NameIdentifier).
builder.Services.AddSingleton<IUserIdProvider, SubUserIdProvider>();

builder.Services.AddDbContext<RotaDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// These three configs carry tables whose C# default is EMPTY, not a fallback — appsettings ships the
// whole thing. An empty table is therefore indistinguishable from a binding failure, and it degrades
// SILENTLY rather than loudly: no milestone floors makes the late game 1.71x faster at L25000, no
// regen map collapses all nineteen class identities to a flat 5.0/5.0, and no refill cost map quietly
// removes gem refills altogether. ValidateOnStart turns each of those into a boot failure.
//
// This is the same shape as the XpExponent default that disagreed with what shipped: the danger is not
// a wrong value, it is a wrong value nobody notices.
builder.Services.AddOptions<LevelingConfig>()
    .Bind(builder.Configuration.GetSection("LevelingConfig"))
    .Validate(c => c.MilestoneFloors.Count > 0,
        "LevelingConfig.MilestoneFloors is empty — appsettings ships the table, so this means it did not bind.")
    .Validate(c => c.PinnacleGemRewards.Count > 0,
        "LevelingConfig.PinnacleGemRewards is empty — appsettings ships the table, so this means it did not bind.")
    .ValidateOnStart();

builder.Services.Configure<LeaderboardConfig>(
    builder.Configuration.GetSection("LeaderboardConfig"));

// The Gauntlet HP curve is exponential AND its late ramp interpolates over the span from
// LateRampStartStage to MaxLadderStage — so raising MaxLadderStage does not extend the curve, it adds
// near-doubling stages and re-scales the whole ramp. The shipped ceiling of 250 tops out at 6.22e16,
// which is 148x under long.MaxValue; 300 tops out at 3.89e25, which is over it by four thousand times.
//
// The failure is silent. StageHp computes in double and casts to long, and a double outside long's
// range has no defined conversion in an unchecked context — the result is a garbage MaxHp rather than
// an exception, and a raid with a non-positive MaxHp is treated as timer-only, so the stage would
// simply become unkillable instead of erroring. Boot instead.
builder.Services.AddOptions<GauntletConfig>()
    .Bind(builder.Configuration.GetSection("GauntletConfig"))
    .Validate(c => c.MaxLadderStage <= 0 || GauntletStageCurve.StageHpIsRepresentable(c),
        "GauntletConfig: the HP curve overflows long at MaxLadderStage. The late ramp interpolates "
        + "across LateRampStartStage..MaxLadderStage, so widening that span multiplies the top stage's "
        + "HP rather than stretching the curve. Lower MaxLadderStage, raise LateRampStartStage, or "
        + "lower LateRampFinalGrowth.")
    .ValidateOnStart();

builder.Services.AddOptions<ClassConfig>()
    .Bind(builder.Configuration.GetSection("ClassConfig"))
    .Validate(c => c.RegenMinutesPerPoint.Count > 0,
        "ClassConfig.RegenMinutesPerPoint is empty — every class would fall back to a flat 5.0/5.0 regen.")
    .Validate(c => c.ConvergenceLevels.Count > 0,
        "ClassConfig.ConvergenceLevels is empty — no class gate would ever fire.")
    .Validate(c => c.ClassUnlockLevels.Count > 0,
        "ClassConfig.ClassUnlockLevels is empty — tier 2/3 unlock levels would be unresolvable.")
    .ValidateOnStart();

builder.Services.Configure<CombatConfig>(
    builder.Configuration.GetSection("CombatConfig"));

builder.Services.Configure<MagicConfig>(
    builder.Configuration.GetSection("MagicConfig"));

builder.Services.Configure<QuestConfig>(
    builder.Configuration.GetSection("QuestConfig"));

builder.Services.Configure<LegionConfig>(
    builder.Configuration.GetSection("LegionConfig"));

builder.Services.Configure<EmailConfig>(
    builder.Configuration.GetSection("Email"));

builder.Services.Configure<GuildConfig>(
    builder.Configuration.GetSection("GuildConfig"));

builder.Services.Configure<DeveloperConfig>(
    builder.Configuration.GetSection("Developer"));

builder.Services.Configure<MasteryConfig>(
    builder.Configuration.GetSection("MasteryConfig"));

builder.Services.Configure<AchievementConfig>(
    builder.Configuration.GetSection("AchievementConfig"));

builder.Services.Configure<RateLimitConfig>(
    builder.Configuration.GetSection("RateLimitConfig"));

builder.Services.AddOptions<ConsumableConfig>()
    .Bind(builder.Configuration.GetSection("ConsumableConfig"))
    .Validate(c => c.InstantRefillGemCost.Count > 0,
        "ConsumableConfig.InstantRefillGemCost is empty — no resource would be refillable for gems.")
    .ValidateOnStart();

builder.Services.Configure<LegalConfig>(
    builder.Configuration.GetSection("Legal"));

builder.Services.Configure<RaidConfig>(
    builder.Configuration.GetSection("RaidConfig"));

builder.Services.AddRotaServices(builder.Environment.ContentRootPath);

// Phase 2 (T39): out-of-band sender that drains the email queue without blocking requests.
builder.Services.AddHostedService<EmailSendBackgroundService>();

// System 16 Slice 3: periodic per-league rank snapshot for the active Gauntlet event.
builder.Services.AddHostedService<GauntletRankSnapshotService>();

// Settles World (timer-only) raids whose clock has run out. Expiry is their only ending — nothing
// else pays the damage ladder they banked.
builder.Services.AddHostedService<RaidExpirySettlementService>();

// Closes and settles a Gauntlet event once it reaches EndsAt. Nothing else did: the window opened
// automatically and never shut, stranding rank prizes and blocking every future event.
builder.Services.AddHostedService<GauntletEventSettlementService>();

// Redis — factory-based so the connection string is resolved from the fully-built
// IConfiguration (after all sources, including test overrides, have been applied)
// rather than from builder.Configuration at service-registration time.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(
        sp.GetRequiredService<IConfiguration>().GetConnectionString("Redis")!));

builder.Services.AddHealthChecks();

// MIDDLEWARE PIPELINE - ORDER IS SECURITY-CRITICAL
// 1. Exception handler   - catches anything that escapes lower layers
// 2. HTTPS               - redirects HTTP to HTTPS in production
// 3. Request logging     - structured log, never logs tokens or passwords
// 4. CORS                - preflight checks before any work
// 5. Rate limiting       - per-player + per-IP, cheap Redis check
// 6. Routing             - matches URL to endpoint
// 7. Authentication      - validates JWT
// 8. Authorization       - checks [Authorize] attributes
// 9. Audit logging       - records state-changing requests with verified PlayerId
// 10. Endpoints          - controllers and hubs

// ADMIN CLI — runs the requested command instead of starting Kestrel.
// MUST be placed after ALL service registration (incl. the Redis factory) so the CLI's
// container is complete and ValidateOnBuild passes. The Redis factory is lazy, so no
// connection is opened unless a command actually resolves a Redis-backed service.
if (args.Length > 0 && AdminCli.IsCommand(args[0]))
    return await AdminCli.RunAsync(args, builder);

var app = builder.Build();

// System 16 Slice 1 — eagerly construct the Gauntlet content provider so its startup
// validation (prize bands, trophy/magic refs, league bounds, off-cap magics, naming
// guard) throws at boot rather than on first use.
app.Services.GetRequiredService<IGauntletContentProvider>();

// System 16 Slice 6 — eagerly construct the token-shop provider so its catalogue validation
// (payloadId referential integrity, price > 0, currency valid, no duplicate ids, bundle/refill
// amount > 0) throws at boot rather than on first purchase.
app.Services.GetRequiredService<IGauntletShopProvider>();

// System 22 Phase A — eagerly construct the mastery definition provider so its content validation
// (4 Ancients, magnitude tables, tier checklists, breadth curve) throws at boot rather than on first use.
app.Services.GetRequiredService<IMasteryDefinitionProvider>();

// System 26 (D-018) — eagerly construct the crafting recipe provider so its content validation
// (ids resolve across four providers, positive quantities, own-once outputs, no recipe consuming its
// own output) throws at boot rather than on a player's first craft.
app.Services.GetRequiredService<ICraftingRecipeProvider>();

// TICKET 46 — eagerly construct the achievement definition provider so its content validation
// (unique ids, valid category/metric, positive points/threshold, NextId chains, Collector keys)
// throws at boot rather than on first use.
app.Services.GetRequiredService<IAchievementDefinitionProvider>();

// T52 — eagerly construct the subject catalog provider so its content validation (non-empty bug/report
// lists, unique keys, non-blank feedback category) throws at boot rather than on first submission.
app.Services.GetRequiredService<ISubjectCatalogProvider>();

// T68 — eagerly construct the legal-text provider so a missing/blank terms.md or privacy.md
// throws at boot rather than on the first registration screen.
app.Services.GetRequiredService<ILegalTextProvider>();

// Dev-only auto-migrate: keeps a fresh local DB in sync without a manual
// `dotnet ef database update`. Idempotent — safe to run even when the schema
// is already current. Production deployments must run migrations explicitly
// before starting the app (operators run `dotnet ef database update` first).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
    await db.Database.MigrateAsync();
}

// Startup seed — runs once before accepting requests.
// Idempotent: skipped if the admin account already exists.
await SeedData.EnsureAdminAsync(app.Services);

// T43: ensure the hidden Dev guild + the developer allowlist. No-op when the allowlist is empty.
await SeedData.EnsureDevGuildAsync(app.Services);

// [1] Global exception handler
// SECURITY: raw exceptions must never reach the client - stack traces leak architecture
app.UseExceptionHandler("/error");
app.Map("/error", (HttpContext ctx) =>
    Results.Problem(
        title: "An unexpected error occurred.",
        detail: null,
        statusCode: 500
    ));

// [1a] Security response headers — early, so they also land on the 500 from the handler above,
// the 429 from the rate limiter and the 403 from the ban gate.
app.UseMiddleware<SecurityHeadersMiddleware>();

// [1b] Reverse-proxy client IPs (T66, host-agnostic deploys)
// OFF by default. When the API sits behind a TLS-terminating proxy/load balancer, every
// RemoteIpAddress is the proxy's — which would collapse per-IP rate limiting and audit IPs.
// SECURITY: honoured ONLY for the explicitly-listed proxy IPs; X-Forwarded-For from anyone
// else stays untrusted (the audit's "spoofable header" rule still holds end-to-end).
//
// The setting now demands an EXPLICIT decision outside Development, because both ways of getting it
// wrong are silent. Left unset behind a proxy, every caller shares one rate-limit bucket and one
// audit IP — the limiter still returns 429s, so it reads as working while protecting nobody and
// throttling everybody. Set to true with an empty TrustedProxies it is worse: KnownProxies and
// KnownIPNetworks are cleared just below, so nothing is trusted, no header is honoured, and the
// config reads as configured. A boot that refuses to start is the only signal that cannot be missed.
if (!app.Environment.IsDevelopment())
{
    var fwdSection = app.Configuration.GetSection("ForwardedHeaders");
    var enabledRaw = fwdSection["Enabled"];
    var proxyCount = fwdSection.GetSection("TrustedProxies").Get<string[]>()?.Length ?? 0;

    if (string.IsNullOrWhiteSpace(enabledRaw))
        throw new InvalidOperationException(
            "ForwardedHeaders:Enabled must be set explicitly outside Development. Set it to true and "
            + "list ForwardedHeaders:TrustedProxies when the API sits behind a reverse proxy, or to "
            + "false to state that it is exposed directly. Leaving it unset behind a proxy collapses "
            + "every per-IP rate-limit bucket and every audit IP onto the proxy.");

    if (app.Configuration.GetValue("ForwardedHeaders:Enabled", false) && proxyCount == 0)
        throw new InvalidOperationException(
            "ForwardedHeaders:Enabled is true but ForwardedHeaders:TrustedProxies is empty. The known-"
            + "proxy lists are cleared for safety, so this combination trusts nothing and honours no "
            + "X-Forwarded-For header — it reads as configured while behaving exactly like disabled. "
            + "List the proxy IP the API actually sees, or set Enabled to false.");
}

if (app.Configuration.GetValue("ForwardedHeaders:Enabled", false))
{
    var fwd = new ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                         | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
    };
    fwd.KnownProxies.Clear();
    // KnownIPNetworks, not the obsolete KnownNetworks — same list, renamed in ASP.NET Core. Cleared so
    // the framework's default loopback trust cannot widen who may set X-Forwarded-For.
    fwd.KnownIPNetworks.Clear();
    foreach (var proxy in app.Configuration.GetSection("ForwardedHeaders:TrustedProxies").Get<string[]>() ?? [])
        fwd.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    app.UseForwardedHeaders(fwd);
}

// [2] HTTPS enforcement
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();

// [3] Request logging
app.UseMiddleware<RequestLoggingMiddleware>();

// [4] CORS
app.UseCors("RotaPolicy");

// [5] Routing
app.UseRouting();

// [5b] Response compression — after routing, before endpoints, so controller/SignalR
// responses are compressed for clients that send an Accept-Encoding header.
app.UseResponseCompression();

// [6] Authentication
app.UseAuthentication();

// [7] Rate limiting (audit fix: AFTER authentication)
// The per-player bucket must key on the signature-VERIFIED identity — keying on an unverified JWT
// 'sub' let attackers exhaust a victim's bucket with forged tokens (targeted DoS). Per-IP limiting
// of /api/auth and of unauthenticated traffic still happens here, before authorization/DB work.
app.UseMiddleware<RateLimitMiddleware>();

// [8] Authorization
app.UseAuthorization();

// [9] Audit logging
// Runs after auth so we have a verified PlayerId to write to audit_log
app.UseMiddleware<AuditLogMiddleware>();

// [9b] Ban gate (audit fix)
// The 15-min access JWT outlives a ban's refresh-token revocation; this gate stops every mutating
// request from a banned player immediately. After AuditLogMiddleware so the 403 is still audited.
app.UseMiddleware<BanGateMiddleware>();

// [10] Swagger (dev only)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ROTA API v1"));
}

// [10] Endpoints
app.MapControllers();
app.MapHealthChecks("/health");

// SignalR hubs (T35 raid chat, T36 world chat, T37 PM delivery)
app.MapHub<ChatHub>("/hubs/chat");

app.Run();

return 0;

// Expose for integration tests (Testcontainers + WebApplicationFactory)
public partial class Program { }