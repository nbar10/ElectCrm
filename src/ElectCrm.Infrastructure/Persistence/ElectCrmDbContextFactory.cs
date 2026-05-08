namespace ElectCrm.Infrastructure.Persistence;

using ElectCrm.Domain.Common;
using ElectCrm.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Used by EF Core CLI tools (dotnet ef migrations add / database update).
/// Provides a DbContext with an empty tenant so global query filters are bypassed.
/// </summary>
public sealed class ElectCrmDbContextFactory : IDesignTimeDbContextFactory<ElectCrmDbContext>
{
    public ElectCrmDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? "Server=(localdb)\\mssqllocaldb;Database=ElectCrm_Dev;Trusted_Connection=True;MultipleActiveResultSets=true";

        var options = new DbContextOptionsBuilder<ElectCrmDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ElectCrmDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        // Empty → global query filters pass through, so migrations see all rows.
        public TenantId CurrentTenantId => TenantId.Empty;
    }
}
