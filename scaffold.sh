#!/bin/bash
# =============================================================
# ROTA — Solution Scaffold Script
# Phase 0 | Week 1 | Day 1
# Run once from the repo root. Safe to inspect before running.
# =============================================================

set -e  # Exit immediately on any error

echo "=== ROTA Solution Scaffold ==="
echo ""

# -----------------------------------------
# 1. Solution file
# -----------------------------------------
dotnet new sln --name ROTA
echo "[+] Solution created: ROTA.sln"

# -----------------------------------------
# 2. Source projects
# -----------------------------------------
dotnet new webapi  --name ROTA.Api            --output src/ROTA.Api            --no-openapi false
dotnet new classlib --name ROTA.Application   --output src/ROTA.Application
dotnet new classlib --name ROTA.Domain        --output src/ROTA.Domain
dotnet new classlib --name ROTA.Infrastructure --output src/ROTA.Infrastructure
dotnet new classlib --name ROTA.Shared        --output src/ROTA.Shared
echo "[+] Source projects created"

# -----------------------------------------
# 3. Test projects
# -----------------------------------------
dotnet new xunit --name ROTA.UnitTests        --output tests/ROTA.UnitTests
dotnet new xunit --name ROTA.IntegrationTests --output tests/ROTA.IntegrationTests
echo "[+] Test projects created"

# -----------------------------------------
# 4. Add all projects to solution
# -----------------------------------------
dotnet sln add src/ROTA.Api/ROTA.Api.csproj
dotnet sln add src/ROTA.Application/ROTA.Application.csproj
dotnet sln add src/ROTA.Domain/ROTA.Domain.csproj
dotnet sln add src/ROTA.Infrastructure/ROTA.Infrastructure.csproj
dotnet sln add src/ROTA.Shared/ROTA.Shared.csproj
dotnet sln add tests/ROTA.UnitTests/ROTA.UnitTests.csproj
dotnet sln add tests/ROTA.IntegrationTests/ROTA.IntegrationTests.csproj
echo "[+] All projects registered in solution"

# -----------------------------------------
# 5. Project references (clean architecture)
#
#    ROTA.Domain     → ROTA.Shared
#    ROTA.Application → ROTA.Domain + ROTA.Shared
#    ROTA.Infrastructure → ROTA.Application + ROTA.Domain + ROTA.Shared
#    ROTA.Api        → ROTA.Application + ROTA.Infrastructure + ROTA.Shared
#    ROTA.UnitTests  → ROTA.Application + ROTA.Domain
#    ROTA.IntegrationTests → ROTA.Api + ROTA.Infrastructure
# -----------------------------------------
dotnet add src/ROTA.Domain/ROTA.Domain.csproj reference \
    src/ROTA.Shared/ROTA.Shared.csproj

dotnet add src/ROTA.Application/ROTA.Application.csproj reference \
    src/ROTA.Domain/ROTA.Domain.csproj \
    src/ROTA.Shared/ROTA.Shared.csproj

dotnet add src/ROTA.Infrastructure/ROTA.Infrastructure.csproj reference \
    src/ROTA.Application/ROTA.Application.csproj \
    src/ROTA.Domain/ROTA.Domain.csproj \
    src/ROTA.Shared/ROTA.Shared.csproj

dotnet add src/ROTA.Api/ROTA.Api.csproj reference \
    src/ROTA.Application/ROTA.Application.csproj \
    src/ROTA.Infrastructure/ROTA.Infrastructure.csproj \
    src/ROTA.Shared/ROTA.Shared.csproj

dotnet add tests/ROTA.UnitTests/ROTA.UnitTests.csproj reference \
    src/ROTA.Application/ROTA.Application.csproj \
    src/ROTA.Domain/ROTA.Domain.csproj

dotnet add tests/ROTA.IntegrationTests/ROTA.IntegrationTests.csproj reference \
    src/ROTA.Api/ROTA.Api.csproj \
    src/ROTA.Infrastructure/ROTA.Infrastructure.csproj
echo "[+] Project references wired"

# -----------------------------------------
# 6. NuGet packages — Infrastructure
# -----------------------------------------
dotnet add src/ROTA.Infrastructure/ROTA.Infrastructure.csproj package \
    Microsoft.EntityFrameworkCore --version 8.*
dotnet add src/ROTA.Infrastructure/ROTA.Infrastructure.csproj package \
    Npgsql.EntityFrameworkCore.PostgreSQL --version 8.*
dotnet add src/ROTA.Infrastructure/ROTA.Infrastructure.csproj package \
    Microsoft.EntityFrameworkCore.Design --version 8.*
dotnet add src/ROTA.Infrastructure/ROTA.Infrastructure.csproj package \
    StackExchange.Redis --version 2.*
echo "[+] Infrastructure packages added"

# -----------------------------------------
# 7. NuGet packages — Application
# -----------------------------------------
dotnet add src/ROTA.Application/ROTA.Application.csproj package \
    FluentValidation --version 11.*
dotnet add src/ROTA.Application/ROTA.Application.csproj package \
    FluentValidation.DependencyInjectionExtensions --version 11.*
echo "[+] Application packages added"

# -----------------------------------------
# 8. NuGet packages — Api
# -----------------------------------------
dotnet add src/ROTA.Api/ROTA.Api.csproj package \
    Microsoft.AspNetCore.Authentication.JwtBearer --version 8.*
dotnet add src/ROTA.Api/ROTA.Api.csproj package \
    Swashbuckle.AspNetCore --version 6.*
echo "[+] API packages added"

# -----------------------------------------
# 9. NuGet packages — Test projects
# -----------------------------------------
dotnet add tests/ROTA.UnitTests/ROTA.UnitTests.csproj package \
    Moq --version 4.*
dotnet add tests/ROTA.UnitTests/ROTA.UnitTests.csproj package \
    FluentAssertions --version 6.*

dotnet add tests/ROTA.IntegrationTests/ROTA.IntegrationTests.csproj package \
    Microsoft.AspNetCore.Mvc.Testing --version 8.*
dotnet add tests/ROTA.IntegrationTests/ROTA.IntegrationTests.csproj package \
    Testcontainers.PostgreSql --version 3.*
dotnet add tests/ROTA.IntegrationTests/ROTA.IntegrationTests.csproj package \
    FluentAssertions --version 6.*
echo "[+] Test packages added"

# -----------------------------------------
# 10. Initialize .NET Secret Manager on API project
# -----------------------------------------
dotnet user-secrets init --project src/ROTA.Api/ROTA.Api.csproj
echo "[+] Secret Manager initialized on ROTA.Api"

echo ""
echo "=== Scaffold complete. Next steps ==="
echo "  1. docker-compose up -d"
echo "  2. dotnet build"
echo "  3. dotnet ef migrations add InitialCreate --project src/ROTA.Infrastructure --startup-project src/ROTA.Api"
echo "  4. dotnet test"
