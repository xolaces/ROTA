using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ROTA.Infrastructure.Persistence;

public class RotaDbContextFactory : IDesignTimeDbContextFactory<RotaDbContext>
{
    public RotaDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<RotaDbContextFactory>()
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<RotaDbContext>();
        optionsBuilder.UseNpgsql(
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. " +
                "Run: dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"<connection-string>\" --project src/ROTA.Api"));

        return new RotaDbContext(optionsBuilder.Options);
    }
}
