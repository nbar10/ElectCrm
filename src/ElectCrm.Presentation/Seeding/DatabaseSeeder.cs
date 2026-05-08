namespace ElectCrm.Presentation.Seeding;

using System.Security.Claims;
using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Common.ValueObjects;
using ElectCrm.Domain.Users;
using ElectCrm.Infrastructure.Identity;
using ElectCrm.Infrastructure.Persistence;
using ElectCrm.Presentation.Authorization;
using ElectCrm.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class DatabaseSeeder
{
    private const string DemoCompaniesHouseNumber = "00000001";
    private const string AdminEmail = "admin@elect.group";
    private const string AdminPasswordConfigKey = "DevSeed:AdminPassword";

    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseSeeder));
        var dbContext = sp.GetRequiredService<ElectCrmDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var config = sp.GetRequiredService<IConfiguration>();

        logger.LogInformation("Development seed starting");

        var brand = await SeedBrandAsync(dbContext, logger);
        await SeedAdminUserAsync(brand, dbContext, userManager, config, logger);

        logger.LogInformation("Development seed complete");
    }

    private static async Task<AgencyBrand> SeedBrandAsync(ElectCrmDbContext dbContext, ILogger logger)
    {
        var existing = await dbContext.AgencyBrands
            .FirstOrDefaultAsync(b => b.CompaniesHouseNumber == DemoCompaniesHouseNumber);

        if (existing is not null)
        {
            logger.LogDebug("Demo brand already exists (ID {Id}) — skipping", existing.Id);
            return existing;
        }

        var address = new Address(
            line1: "1 Elect House",
            city: "London",
            postcode: "EC1A 1AA");

        var result = AgencyBrand.Create(
            legalName: "Elect Group Ltd",
            tradingName: "Elect Group Demo",
            companiesHouseNumber: DemoCompaniesHouseNumber,
            registeredAddress: address,
            primaryContactEmail: AdminEmail,
            agentPersonaName: "Electra");

        if (result.IsFailure)
            throw new InvalidOperationException($"Seed failed — could not create demo brand: {result.Error.Message}");

        dbContext.AgencyBrands.Add(result.Value);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded brand '{Name}' with ID {Id}", result.Value.TradingName, result.Value.Id);
        return result.Value;
    }

    private static async Task SeedAdminUserAsync(
        AgencyBrand brand,
        ElectCrmDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger logger)
    {
        if (await userManager.FindByEmailAsync(AdminEmail) is not null)
        {
            logger.LogDebug("Admin user {Email} already exists — skipping", AdminEmail);
            return;
        }

        var password = config[AdminPasswordConfigKey];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Admin user not seeded — '{Key}' is missing from user-secrets. " +
                "Set it with: dotnet user-secrets set \"{Key}\" \"<password>\" --project src/ElectCrm.Presentation",
                AdminPasswordConfigKey,
                AdminPasswordConfigKey);
            return;
        }

        // Create domain user first — UserManager.CreateAsync flushes the DbContext so
        // both entities land in the same round-trip to the database.
        var userResult = User.Create(new TenantId(brand.Id), "System Administrator", AdminEmail);
        if (userResult.IsFailure)
            throw new InvalidOperationException($"Seed failed — could not create admin domain user: {userResult.Error.Message}");

        dbContext.Users.Add(userResult.Value);

        var appUser = new ApplicationUser
        {
            DomainUserId = userResult.Value.Id,
            Email = AdminEmail,
            UserName = AdminEmail,
        };
        var createResult = await userManager.CreateAsync(appUser, password);

        if (!createResult.Succeeded)
            throw new InvalidOperationException(
                $"Seed failed — could not create admin ApplicationUser: " +
                $"{string.Join(", ", createResult.Errors.Select(e => e.Description))}");

        // Assign GroupAdmin (cross-tenant) and BrandAdmin (this brand) structured role claims.
        var claims = new Claim[]
        {
            new(ElectClaimTypes.Role, $"{nameof(RoleName.GroupAdmin)}:{nameof(RoleScope.Group)}:{brand.Id}"),
            new(ElectClaimTypes.Role, $"{nameof(RoleName.BrandAdmin)}:{nameof(RoleScope.Brand)}:{brand.Id}"),
        };

        var claimsResult = await userManager.AddClaimsAsync(appUser, claims);
        if (!claimsResult.Succeeded)
            throw new InvalidOperationException(
                $"Seed failed — could not add role claims: " +
                $"{string.Join(", ", claimsResult.Errors.Select(e => e.Description))}");

        logger.LogInformation(
            "Seeded admin user {Email} with GroupAdmin + BrandAdmin claims for brand {BrandId}",
            AdminEmail, brand.Id);
    }
}
