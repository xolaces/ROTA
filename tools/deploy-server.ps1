<#
  deploy-server.ps1
  Puts the pushed `main` on the live API, the same way every time.

  It prints each step before running it and stops at the first failure. Without -Go it only prints
  (a dry run). It never switches maintenance OFF: check the new build first (admin accounts pass the
  gate), then run the printed command yourself.

  Steps:
    1. maintenance on (players see "Down for an update"; the switch lives in Redis, so it survives the restart)
    2. git pull on the droplet
    3. -Migrate only: snapshot the database to /opt/rota/backups/rota-<date>-pre-<commit>.dump and check it
    4. build the API image
    5. -Migrate only: run a CLI command from the NEW image, which applies pending migrations
       (production does not migrate on startup)
    6. start the new API, wait, check /health and /api/status

  Usage:
    .\tools\deploy-server.ps1                     # dry run: print what would happen
    .\tools\deploy-server.ps1 -Go                 # deploy, no migration
    .\tools\deploy-server.ps1 -Go -Migrate        # deploy with a migration
    .\tools\deploy-server.ps1 -Go -Message "Back at nine."
#>
param(
    [switch]$Go,
    [switch]$Migrate,
    [string]$Message = "Updating the game. Back in a few minutes.",
    # user@host of the droplet. Not written here: this repository is public.
    [string]$Server = $env:ROTA_DEPLOY_HOST
)
$ErrorActionPreference = "Stop"
if (-not $Server) { Write-Host "Set ROTA_DEPLOY_HOST (user@host of the droplet) or pass -Server." -ForegroundColor Red; exit 1 }

$compose = "docker compose -f docker-compose.prod.yml -f docker-compose.caddy.yml"
$safeMessage = $Message.Replace("'", "")

function Step([string]$title, [string]$remote) {
    Write-Host ""
    Write-Host "== $title" -ForegroundColor Cyan
    Write-Host "   ssh $Server `"$remote`""
    if (-not $Go) { return }
    ssh $Server $remote
    if ($LASTEXITCODE -ne 0) {
        Write-Host "STOPPED: '$title' failed (exit $LASTEXITCODE). Maintenance is still on; nothing after this step ran." -ForegroundColor Red
        exit 1
    }
}

if (-not $Go) { Write-Host "DRY RUN. Nothing is sent. Add -Go to deploy." -ForegroundColor Yellow }

Step "Maintenance on" "docker exec rota-api dotnet ROTA.Api.dll maintenance on '$safeMessage'"
Step "Pull main" "cd /opt/rota && git pull -q origin main && git log --oneline -1"

if ($Migrate) {
    Step "Snapshot the database" ("cd /opt/rota && F=backups/rota-`$(date +%Y%m%d)-pre-`$(git rev-parse --short HEAD).dump && " +
        "docker exec rota-postgres-prod sh -c 'pg_dump -U `$POSTGRES_USER -d `$POSTGRES_DB -Fc' > `$F && " +
        "test -s `$F && docker exec -i rota-postgres-prod pg_restore -l < `$F > /dev/null && ls -la `$F")
}

Step "Build the API image" "cd /opt/rota && $compose build api"

if ($Migrate) {
    Step "Apply migrations from the new image" "cd /opt/rota && $compose run --rm --no-deps api maintenance status"
}

Step "Start the new API and check it" ("cd /opt/rota && $compose up -d api && sleep 15 && " +
    "docker ps --filter name=rota-api --format '{{.Names}} {{.Status}}' && " +
    "curl -fsS -m 15 https://api.riseoftheancients.com/api/status && echo && " +
    "docker exec rota-api dotnet ROTA.Api.dll maintenance status")

Write-Host ""
Write-Host "Check the game with an admin account, then reopen it:" -ForegroundColor Green
Write-Host "   ssh $Server `"docker exec rota-api dotnet ROTA.Api.dll maintenance off`""
Write-Host "Client: .\tools\deploy-webgl.ps1 in ROTA.Client6 (before reopening, so players get the new build)."
