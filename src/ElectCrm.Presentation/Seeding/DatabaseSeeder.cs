namespace ElectCrm.Presentation.Seeding;

using System.Security.Claims;
using ElectCrm.Application.Features.Persons;
using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Candidates;
using ElectCrm.Domain.Common.ValueObjects;
using ElectCrm.Domain.Contacts;
using ElectCrm.Domain.Persons;
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
        var hashing     = sp.GetRequiredService<IPersonHashingService>();

        logger.LogInformation("Development seed starting");

        await dbContext.Database.MigrateAsync();

        var brand1 = await SeedBrandAsync(dbContext, Brand1Chn, "Elect Group Ltd",        "Elect Group Demo",     AdminEmail,  "Electra", logger);
        var brand2 = await SeedBrandAsync(dbContext, Brand2Chn, "Test Industries Ltd",    "Test Industries Demo", Admin2Email, "Tessa",   logger);

        await SeedAdminUserAsync(brand1, AdminEmail,  dbContext, userManager, config, logger);
        await SeedAdminUserAsync(brand2, Admin2Email, dbContext, userManager, config, logger);

        await SeedContactsAsync(brand1, brand2, dbContext, logger);
        await SeedPersonsAsync(dbContext, hashing, logger);
        await SeedCandidatesAsync(brand1, brand2, dbContext, logger);

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

    private static async Task SeedPersonsAsync(
        ElectCrmDbContext dbContext,
        IPersonHashingService hashing,
        ILogger logger)
    {
        // Guard on the sentinel seed record, not on any person existing,
        // so manually-created persons don't block seeding.
        var alreadySeeded = await dbContext.Persons
            .AnyAsync(p => p.DisplayName == "John Smith" && p.DateOfBirth == new DateOnly(1985, 3, 14));

        if (alreadySeeded)
        {
            logger.LogDebug("Person seed records already present — skipping");
            return;
        }

        var seeds = PersonSeeds();

        foreach (var (displayName, dob, phoneRaw, niRaw, passportRaw) in seeds)
        {
            string? phoneHash = null, phoneEncrypted = null;
            if (phoneRaw is not null)
            {
                var normalised = phoneRaw.Replace(" ", string.Empty);
                phoneHash      = hashing.HashValue(normalised);
                phoneEncrypted = hashing.EncryptValue(normalised);
            }

            string? niHash = null, niEncrypted = null;
            if (niRaw is not null)
            {
                var niResult = NationalInsuranceNumber.TryCreate(niRaw);
                if (niResult.IsFailure)
                    throw new InvalidOperationException($"Seed failed — invalid NI number for '{displayName}': {niResult.Error.Message}");

                niHash      = hashing.HashValue(niResult.Value.Value);
                niEncrypted = hashing.EncryptValue(niResult.Value.Value);
            }

            string? passportHash = null, passportEncrypted = null;
            if (passportRaw is not null)
            {
                passportHash      = hashing.HashValue(passportRaw);
                passportEncrypted = hashing.EncryptValue(passportRaw);
            }

            var fullNameNormalised = PersonNameNormaliser.Normalise(displayName);

            var result = Person.Create(
                displayName, fullNameNormalised, dob,
                phoneHash, phoneEncrypted,
                niHash, niEncrypted,
                passportHash, passportEncrypted);

            if (result.IsFailure)
                throw new InvalidOperationException($"Seed failed — could not create person '{displayName}': {result.Error.Message}");

            dbContext.Persons.Add(result.Value);
            result.Value.ClearDomainEvents();
        }

        await dbContext.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} persons", seeds.Count);
    }

    // (DisplayName, DateOfBirth, PrimaryPhoneRaw, NiNumberRaw, PassportNumberRaw)
    // Nulls = identifier not supplied for that person
    private static List<(string DisplayName, DateOnly? Dob, string? Phone, string? NiNumber, string? Passport)>
        PersonSeeds() =>
    [
        // Similar-name pair — key for future PersonIdentity matching tests
        ("John Smith",    new DateOnly(1985, 3, 14),  "+447700900001", "AB123456C", "123456001"),
        ("Jon Smyth",     new DateOnly(1982, 11, 2),  "+447700900002", "CE234567D", null),

        // Distinct individuals — search should return exactly one result each
        ("Eleanor Whitfield",  new DateOnly(1990, 6, 20),  "+447700900003", "GH345678A", "123456003"),
        ("Marcus Adeyemi",     new DateOnly(1978, 9, 5),   "+447700900004", "JK456789B", null),
        ("Priya Kapoor",       new DateOnly(1995, 1, 30),  "+447700900005", "NP567890C", "123456005"),
        ("Charlotte Davies",   new DateOnly(1988, 7, 17),  "+447700900006", null,        "123456006"),
        ("Rory MacPherson",    new DateOnly(1975, 4, 22),  "+447700900007", "RS678901D", null),
        ("Ingrid Karlsson",    new DateOnly(1993, 12, 8),  "+447700900008", "TW789012A", "123456008"),
        ("Daniel Torres",      new DateOnly(1981, 2, 28),  null,            "XY890123B", null),
        ("Amelia Foster",      new DateOnly(2000, 8, 3),   "+447700900010", null,        "123456010"),
    ];

    private static async Task SeedCandidatesAsync(
        AgencyBrand brand1,
        AgencyBrand brand2,
        ElectCrmDbContext dbContext,
        ILogger logger)
    {
        var alreadySeeded = await dbContext.Candidates
            .IgnoreQueryFilters()
            .AnyAsync(c => c.AgencyBrandId == brand1.Id);

        if (alreadySeeded)
        {
            logger.LogDebug("Candidate seed records already present — skipping");
            return;
        }

        var persons = await dbContext.Persons
            .Where(p => !p.IsDeleted)
            .ToListAsync();

        Domain.Persons.Person FindPerson(string name, DateOnly dob) =>
            persons.FirstOrDefault(p => p.DisplayName == name && p.DateOfBirth == dob)
            ?? throw new InvalidOperationException($"Seed failed — Person '{name}' ({dob:yyyy-MM-dd}) not found. Ensure person seed ran first.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // ── Brand 1 (Elect Group Demo) — 5 candidates ──────────────────────────
        // Cross-brand: John Smith and Eleanor Whitfield appear under both brands.
        // Brand-only:  Marcus Adeyemi, Priya Kapoor, Charlotte Davies.
        // No candidates at all: Jon Smyth, Daniel Torres, Amelia Foster.
        var brand1Seeds = new[]
        {
            (Person: FindPerson("John Smith",       new DateOnly(1985, 3,  14)), Trade: "Electrician",  Source: "Referral",   Status: CandidateStatus.Active,   RegDate: today.AddDays(-120)),
            (Person: FindPerson("Eleanor Whitfield", new DateOnly(1990, 6,  20)), Trade: "Plumber",      Source: "Indeed",     Status: CandidateStatus.Active,   RegDate: today.AddDays(-90)),
            (Person: FindPerson("Marcus Adeyemi",    new DateOnly(1978, 9,  5)),  Trade: "Scaffolder",   Source: "Walk-in",    Status: CandidateStatus.Active,   RegDate: today.AddDays(-60)),
            (Person: FindPerson("Priya Kapoor",      new DateOnly(1995, 1,  30)), Trade: "Labourer",     Source: "Find a Job", Status: CandidateStatus.Active,   RegDate: today.AddDays(-45)),
            (Person: FindPerson("Charlotte Davies",  new DateOnly(1988, 7,  17)), Trade: "Electrician",  Source: "Agency",     Status: CandidateStatus.Dormant,  RegDate: today.AddDays(-200)),
        };

        // ── Brand 2 (Test Industries Demo) — 4 candidates ─────────────────────
        // Cross-brand: John Smith and Eleanor Whitfield re-appear here.
        // Brand-only:  Rory MacPherson, Ingrid Karlsson.
        var brand2Seeds = new[]
        {
            (Person: FindPerson("John Smith",       new DateOnly(1985, 3,  14)), Trade: "Electrician",  Source: "Referral",   Status: CandidateStatus.Active,   RegDate: today.AddDays(-115)),
            (Person: FindPerson("Eleanor Whitfield", new DateOnly(1990, 6,  20)), Trade: "Plumber",      Source: "LinkedIn",   Status: CandidateStatus.Active,   RegDate: today.AddDays(-80)),
            (Person: FindPerson("Rory MacPherson",   new DateOnly(1975, 4,  22)), Trade: "Joiner",       Source: "Walk-in",    Status: CandidateStatus.Active,   RegDate: today.AddDays(-30)),
            (Person: FindPerson("Ingrid Karlsson",   new DateOnly(1993, 12, 8)),  Trade: "Labourer",     Source: "Find a Job", Status: CandidateStatus.Dormant,  RegDate: today.AddDays(-180)),
        };

        void AddCandidate(AgencyBrand brand, Domain.Persons.Person person, CandidateStatus status, DateOnly regDate, string trade, string source)
        {
            var result = Candidate.Create(
                new TenantId(brand.Id),
                person.Id,
                regDate,
                status,
                ownerConsultantId: null,
                primaryTrade: trade,
                source: source,
                sourceLegacyId: null,
                notes: null);

            if (result.IsFailure)
                throw new InvalidOperationException($"Seed failed — {result.Error.Message}");

            dbContext.Candidates.Add(result.Value);
            result.Value.ClearDomainEvents();
        }

        foreach (var s in brand1Seeds)
            AddCandidate(brand1, s.Person, s.Status, s.RegDate, s.Trade, s.Source);

        foreach (var s in brand2Seeds)
            AddCandidate(brand2, s.Person, s.Status, s.RegDate, s.Trade, s.Source);

        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "Seeded {Total} candidates — {B1Count} for '{B1}', {B2Count} for '{B2}'. " +
            "Cross-brand persons: John Smith, Eleanor Whitfield. " +
            "No-candidate persons: Jon Smyth, Daniel Torres, Amelia Foster.",
            brand1Seeds.Length + brand2Seeds.Length,
            brand1Seeds.Length, brand1.TradingName,
            brand2Seeds.Length, brand2.TradingName);
    }
}
