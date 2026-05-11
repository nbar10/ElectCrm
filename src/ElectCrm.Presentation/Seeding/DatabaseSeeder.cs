namespace ElectCrm.Presentation.Seeding;

using System.Security.Claims;
using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Common.ValueObjects;
using ElectCrm.Domain.Contacts;
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
    private const string Brand1Chn = "00000001";
    private const string Brand2Chn = "00000002";
    private const string AdminEmail = "admin@elect.group";
    private const string Admin2Email = "admin2@elect.group";
    private const string AdminPasswordConfigKey = "DevSeed:AdminPassword";

    // Stable placeholder ClientIds — replaced when the Client slice is built.
    private static readonly Guid AcmeCorpId    = new("a0000001-0000-7000-0000-000000000001");
    private static readonly Guid BuildRightId  = new("a0000001-0000-7000-0000-000000000002");
    private static readonly Guid SkyHighId     = new("a0000001-0000-7000-0000-000000000003");
    private static readonly Guid MetroScaffId  = new("b0000002-0000-7000-0000-000000000001");
    private static readonly Guid PinnacleBldId = new("b0000002-0000-7000-0000-000000000002");

    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var logger      = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseSeeder));
        var dbContext   = sp.GetRequiredService<ElectCrmDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var config      = sp.GetRequiredService<IConfiguration>();

        logger.LogInformation("Development seed starting");

        var brand1 = await SeedBrandAsync(dbContext, Brand1Chn, "Elect Group Ltd",        "Elect Group Demo",     AdminEmail,  "Electra", logger);
        var brand2 = await SeedBrandAsync(dbContext, Brand2Chn, "Test Industries Ltd",    "Test Industries Demo", Admin2Email, "Tessa",   logger);

        await SeedAdminUserAsync(brand1, AdminEmail,  dbContext, userManager, config, logger);
        await SeedAdminUserAsync(brand2, Admin2Email, dbContext, userManager, config, logger);

        await SeedContactsAsync(brand1, brand2, dbContext, logger);

        logger.LogInformation("Development seed complete");
    }

    private static async Task<AgencyBrand> SeedBrandAsync(
        ElectCrmDbContext dbContext,
        string chn,
        string legalName,
        string tradingName,
        string contactEmail,
        string personaName,
        ILogger logger)
    {
        var existing = await dbContext.AgencyBrands
            .FirstOrDefaultAsync(b => b.CompaniesHouseNumber == chn);

        if (existing is not null)
        {
            logger.LogDebug("Brand '{Name}' already exists — skipping", existing.TradingName);
            return existing;
        }

        var address = new Address(line1: "1 Demo Street", city: "London", postcode: "EC1A 1AA");

        var result = AgencyBrand.Create(
            legalName:             legalName,
            tradingName:           tradingName,
            companiesHouseNumber:  chn,
            registeredAddress:     address,
            primaryContactEmail:   contactEmail,
            agentPersonaName:      personaName);

        if (result.IsFailure)
            throw new InvalidOperationException($"Seed failed — could not create brand '{tradingName}': {result.Error.Message}");

        dbContext.AgencyBrands.Add(result.Value);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded brand '{Name}' (ID {Id})", result.Value.TradingName, result.Value.Id);
        return result.Value;
    }

    private static async Task SeedAdminUserAsync(
        AgencyBrand brand,
        string email,
        ElectCrmDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger logger)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            logger.LogDebug("User {Email} already exists — skipping", email);
            return;
        }

        var password = config[AdminPasswordConfigKey];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "User {Email} not seeded — '{Key}' is missing from user-secrets. " +
                "Set it with: dotnet user-secrets set \"{Key}\" \"<password>\" --project src/ElectCrm.Presentation",
                email, AdminPasswordConfigKey, AdminPasswordConfigKey);
            return;
        }

        var userResult = User.Create(new TenantId(brand.Id), "System Administrator", email);
        if (userResult.IsFailure)
            throw new InvalidOperationException($"Seed failed — could not create domain user {email}: {userResult.Error.Message}");

        dbContext.Users.Add(userResult.Value);

        var appUser = new ApplicationUser
        {
            DomainUserId = userResult.Value.Id,
            Email        = email,
            UserName     = email,
        };

        var createResult = await userManager.CreateAsync(appUser, password);
        if (!createResult.Succeeded)
            throw new InvalidOperationException(
                $"Seed failed — could not create ApplicationUser {email}: " +
                $"{string.Join(", ", createResult.Errors.Select(e => e.Description))}");

        var claims = new Claim[]
        {
            new(ElectClaimTypes.Role, $"{nameof(RoleName.GroupAdmin)}:{nameof(RoleScope.Group)}:{brand.Id}"),
            new(ElectClaimTypes.Role, $"{nameof(RoleName.BrandAdmin)}:{nameof(RoleScope.Brand)}:{brand.Id}"),
        };

        var claimsResult = await userManager.AddClaimsAsync(appUser, claims);
        if (!claimsResult.Succeeded)
            throw new InvalidOperationException(
                $"Seed failed — could not add role claims for {email}: " +
                $"{string.Join(", ", claimsResult.Errors.Select(e => e.Description))}");

        logger.LogInformation("Seeded user {Email} (BrandAdmin for {BrandId})", email, brand.Id);
    }

    private static async Task SeedContactsAsync(
        AgencyBrand brand1,
        AgencyBrand brand2,
        ElectCrmDbContext dbContext,
        ILogger logger)
    {
        await SeedBrandContactsAsync(brand1, dbContext, logger);
        await SeedBrandContactsAsync(brand2, dbContext, logger);
    }

    private static async Task SeedBrandContactsAsync(
        AgencyBrand brand,
        ElectCrmDbContext dbContext,
        ILogger logger)
    {
        var hasContacts = await dbContext.Contacts
            .IgnoreQueryFilters()
            .AnyAsync(c => c.AgencyBrandId == brand.Id);

        if (hasContacts)
        {
            logger.LogDebug("Contacts for brand '{Name}' already exist — skipping", brand.TradingName);
            return;
        }

        var seeds = brand.CompaniesHouseNumber == Brand1Chn
            ? Brand1Contacts()
            : Brand2Contacts();

        foreach (var (clientId, fullName, roleTitle, email, phone, categories) in seeds)
        {
            var result = Contact.Create(
                new TenantId(brand.Id),
                clientId,
                fullName,
                roleTitle,
                email,
                phone,
                categories,
                communicationPreferences: null);

            if (result.IsFailure)
                throw new InvalidOperationException(
                    $"Seed failed — could not create contact '{fullName}': {result.Error.Message}");

            dbContext.Contacts.Add(result.Value);
            result.Value.ClearDomainEvents();
        }

        await dbContext.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} contacts for brand '{Name}'", seeds.Count, brand.TradingName);
    }

    private static List<(Guid ClientId, string FullName, string? RoleTitle, string? Email, string? Phone, ContactCategory[] Categories)>
        Brand1Contacts() =>
    [
        (AcmeCorpId,   "James Whitfield",  "Site Manager",          "j.whitfield@acmecorp.fake",   "+447700100001", [ContactCategory.SiteManager]),
        (AcmeCorpId,   "Sarah Chen",       "Health & Safety Lead",  "s.chen@acmecorp.fake",        "+447700100002", [ContactCategory.HealthAndSafety, ContactCategory.Compliance]),
        (AcmeCorpId,   "Michael Torres",   "Accounts Payable",      "ap@acmecorp.fake",            "+447700100003", [ContactCategory.AccountsPayable]),
        (BuildRightId, "Rachel Okafor",    "Compliance Manager",    "r.okafor@buildright.fake",    "+447700200001", [ContactCategory.Compliance]),
        (BuildRightId, "David Harrison",   "Site Supervisor",       "d.harrison@buildright.fake",  "+447700200002", [ContactCategory.SiteManager]),
        (BuildRightId, "Priya Patel",      "Recruitment Lead",      "p.patel@buildright.fake",     "+447700200003", [ContactCategory.Recruitment]),
        (SkyHighId,    "Tom Yates",        "Operations Manager",    "t.yates@skyhigh.fake",        "+447700300001", [ContactCategory.Operations]),
        (SkyHighId,    "Emma Larsson",     "Finance Director",      "e.larsson@skyhigh.fake",      "+447700300002", [ContactCategory.AccountsPayable]),
    ];

    private static List<(Guid ClientId, string FullName, string? RoleTitle, string? Email, string? Phone, ContactCategory[] Categories)>
        Brand2Contacts() =>
    [
        (MetroScaffId,  "Gary Nelson",      "Site Manager",         "g.nelson@metroscaff.fake",    "+447700400001", [ContactCategory.SiteManager]),
        (MetroScaffId,  "Linda Marsh",      "Accounts Payable",     "l.marsh@metroscaff.fake",     "+447700400002", [ContactCategory.AccountsPayable]),
        (PinnacleBldId, "Steve Kwan",       "H&S Officer",          "s.kwan@pinnaclebld.fake",     "+447700500001", [ContactCategory.HealthAndSafety]),
        (PinnacleBldId, "Fiona McAllister", "Recruitment Manager",  "f.mcallister@pinnaclebld.fake","+447700500002",[ContactCategory.Recruitment]),
    ];
}
