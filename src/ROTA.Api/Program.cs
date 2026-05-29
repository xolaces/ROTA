using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Security.Cryptography;
using ROTA.Infrastructure.Persistence;
using ROTA.Api.Middleware;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Application.Configuration;
using ROTA.Domain.Enums;
using ROTA.Infrastructure;
using ROTA.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------
// SERVICES
// ---------------------------------------------------------------

builder.Services.AddControllers();

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

// CORS
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
rsaPublicKey.ImportFromPem(builder.Configuration["Jwt:PublicKey"]
    ?? throw new InvalidOperationException("Jwt:PublicKey is not configured."));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
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

    // "ModeratorOrAdmin": moderators and admins both satisfy this policy.
    options.AddPolicy("ModeratorOrAdmin", policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx =>
                  ctx.User.IsInRole(nameof(PlayerRoles.Admin))
                  || ctx.User.IsInRole(nameof(PlayerRoles.Moderator))));
});

builder.Services.AddSignalR();

// EF Core + PostgreSQL
builder.Services.AddDbContext<RotaDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<LevelingConfig>(
    builder.Configuration.GetSection("LevelingConfig"));

builder.Services.Configure<ClassConfig>(
    builder.Configuration.GetSection("ClassConfig"));

builder.Services.AddRotaServices(builder.Environment.ContentRootPath);

// Redis — factory-based so the connection string is resolved from the fully-built
// IConfiguration (after all sources, including test overrides, have been applied)
// rather than from builder.Configuration at service-registration time.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(
        sp.GetRequiredService<IConfiguration>().GetConnectionString("Redis")!));

// Health checks
builder.Services.AddHealthChecks();

// ---------------------------------------------------------------
// MIDDLEWARE PIPELINE - ORDER IS SECURITY-CRITICAL
// ---------------------------------------------------------------
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
// ---------------------------------------------------------------

var app = builder.Build();

// Startup seed — runs once before accepting requests.
// Idempotent: skipped if the admin account already exists.
await SeedData.EnsureAdminAsync(app.Services);

// [1] Global exception handler
// SECURITY: raw exceptions must never reach the client - stack traces leak architecture
app.UseExceptionHandler("/error");
app.Map("/error", (HttpContext ctx) =>
    Results.Problem(
        title: "An unexpected error occurred.",
        detail: null,
        statusCode: 500
    ));

// [2] HTTPS enforcement
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();

// [3] Request logging
app.UseMiddleware<RequestLoggingMiddleware>();

// [4] CORS
app.UseCors("RotaPolicy");

// [5] Rate limiting
// Runs BEFORE JWT validation - bots are dropped before any expensive DB work
app.UseMiddleware<RateLimitMiddleware>();

// [6] Routing
app.UseRouting();

// [7] Authentication
app.UseAuthentication();

// [8] Authorization
app.UseAuthorization();

// [9] Audit logging
// Runs after auth so we have a verified PlayerId to write to audit_log
app.UseMiddleware<AuditLogMiddleware>();

// [10] Swagger (dev only)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ROTA API v1"));
}

// [10] Endpoints
app.MapControllers();
app.MapHealthChecks("/health");

// SignalR hubs registered here as systems are built
// app.MapHub<RaidHub>("/hubs/raid");
// app.MapHub<GuildHub>("/hubs/guild");

app.Run();

// Expose for integration tests (Testcontainers + WebApplicationFactory)
public partial class Program { }