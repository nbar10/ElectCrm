namespace ElectCrm.Presentation.Seeding;

using System.Security.Claims;
using ElectCrm.Application.Features.Clients;
using ElectCrm.Application.Features.Persons;
using ElectCrm.Application.Features.Vacancies;
using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Branches;
using ElectCrm.Domain.Candidates;
using ElectCrm.Domain.Clients;
using ElectCrm.Domain.Common;
using ElectCrm.Domain.Common.ValueObjects;
using ElectCrm.Domain.Contacts;
using ElectCrm.Domain.Persons;
using ElectCrm.Domain.Users;
using ElectCrm.Domain.Vacancies;
using ElectCrm.Application.Features.Placements;
using ElectCrm.Domain.Placements;
using ElectCrm.Infrastructure.Features.Clients;
using ElectCrm.Infrastructure.Features.Placements;
using ElectCrm.Infrastructure.Features.Vacancies;
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
    private const string Brand3Chn = "00000003";
    private const string Brand4Chn = "00000004";
    private const string Brand5Chn = "00000005";
    private const string AdminEmail = "admin@elect.group";
    private const string Admin2Email = "admin2@elect.group";
    private const string AdminPasswordConfigKey = "DevSeed:AdminPassword";
    private const string MidlandsTestUserEmail = "user@midlands-industrial.test";
    private const string MidlandsTestUserPasswordConfigKey = "SeedData:MidlandsTestUserPassword";

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
        await SeedAdminSliceTestDataAsync(brand1, brand2, dbContext, logger);
        await SeedMidlandsTestUserAsync(dbContext, userManager, config, logger);
        await SeedUserManagementSliceDataAsync(dbContext, userManager, config, logger);
        await SeedVacancySliceTestDataAsync(sp, logger);
        await SeedPlacementSliceTestDataAsync(sp, hashing, logger);

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

    // FIRST_GROUPADMIN_BOOTSTRAP — for production, create the first GroupAdmin user via the seed
    // migration or a one-time EF data migration; do not expose a public registration path for GroupAdmin.
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

    private static async Task SeedAdminSliceTestDataAsync(
        AgencyBrand brand1,
        AgencyBrand brand2,
        ElectCrmDbContext dbContext,
        ILogger logger)
    {
        // Sentinel: brand 3 CHN is the indicator that this entire block has already run.
        var alreadySeeded = await dbContext.AgencyBrands
            .AnyAsync(b => b.CompaniesHouseNumber == Brand3Chn);

        if (alreadySeeded)
        {
            logger.LogDebug("Admin slice test data already seeded — skipping");
            return;
        }

        // ── Brands 3–5 ────────────────────────────────────────────────────────────

        var brand3Result = AgencyBrand.Create(
            legalName:            "Northern Construction Recruitment Ltd",
            tradingName:          "Northern Construction Recruitment",
            companiesHouseNumber: Brand3Chn,
            registeredAddress:    new Address("14 Aire Street", "Leeds", "LS1 4PR", country: "GB"),
            primaryContactEmail:  "admin@northernconstruction.fake",
            agentPersonaName:     "Nova",
            vatNumber:            "GB300000003",
            glaaLicenceNumber:    "GLAA-NCR-2021");

        if (brand3Result.IsFailure)
            throw new InvalidOperationException($"Seed failed — brand 3: {brand3Result.Error.Message}");

        var brand4Result = AgencyBrand.Create(
            legalName:            "Midlands Industrial Staffing Ltd",
            tradingName:          "Midlands Industrial Staffing",
            companiesHouseNumber: Brand4Chn,
            registeredAddress:    new Address("12 Corporation Street", "Coventry", "CV1 1GF", country: "GB"),
            primaryContactEmail:  "admin@midlandsstaffing.fake",
            agentPersonaName:     "Maxwell",
            vatNumber:            "GB400000004");

        if (brand4Result.IsFailure)
            throw new InvalidOperationException($"Seed failed — brand 4: {brand4Result.Error.Message}");

        var brand5Result = AgencyBrand.Create(
            legalName:            "Legacy Recruitment Ltd",
            tradingName:          "Legacy Brand (Acquired 2019)",
            companiesHouseNumber: Brand5Chn,
            registeredAddress:    new Address("3 Friar Gate", "Derby", "DE1 1BU", country: "GB"),
            primaryContactEmail:  "admin@legacybrand.fake",
            agentPersonaName:     "Lexi");

        if (brand5Result.IsFailure)
            throw new InvalidOperationException($"Seed failed — brand 5: {brand5Result.Error.Message}");

        var brand3 = brand3Result.Value;
        var brand4 = brand4Result.Value;
        var brand5 = brand5Result.Value;

        brand4.Pause();
        brand5.Retire();

        dbContext.AgencyBrands.Add(brand3);
        dbContext.AgencyBrands.Add(brand4);
        dbContext.AgencyBrands.Add(brand5);

        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "Seeded brands: '{B3}' (active), '{B4}' (paused), '{B5}' (retired)",
            brand3.TradingName, brand4.TradingName, brand5.TradingName);

        // ── Branches ──────────────────────────────────────────────────────────────

        void AddBranch(AgencyBrand brand, string name, string[] postcodePrefixes, bool retired = false)
        {
            var address = new Address("1 " + name + " Street", name, "XX1 1XX", country: "GB");
            var geography = GeoArea.FromPrefixes(postcodePrefixes);

            var result = Branch.Create(new TenantId(brand.Id), name, address, geography);
            if (result.IsFailure)
                throw new InvalidOperationException(
                    $"Seed failed — branch '{name}' for '{brand.TradingName}': {result.Error.Message}");

            var branch = result.Value;

            if (retired)
                branch.Retire();

            branch.ClearDomainEvents();
            dbContext.Branches.Add(branch);
        }

        // Brand 1 (Elect Group Demo)
        AddBranch(brand1, "London HQ",   ["EC", "E", "N", "NW", "SE", "SW", "W", "WC"]);
        AddBranch(brand1, "Manchester",  ["M"]);

        // Brand 2 (Test Industries Demo)
        AddBranch(brand2, "Birmingham",  ["B"]);

        // Brand 3 (Northern Construction Recruitment)
        AddBranch(brand3, "Leeds",       ["LS"]);
        AddBranch(brand3, "Sheffield",   ["S"]);
        AddBranch(brand3, "Hull",        ["HU"], retired: true);

        // Brand 4 (Midlands Industrial Staffing — paused brand, active branch)
        AddBranch(brand4, "Coventry",    ["CV"]);

        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "Seeded 7 branches: 2 for '{B1}', 1 for '{B2}', 3 for '{B3}' (1 retired), 1 for '{B4}'",
            brand1.TradingName, brand2.TradingName, brand3.TradingName, brand4.TradingName);
    }

    // Exists solely to exercise the login-block fix: a regular user whose brand is paused
    // should see the friendly suspension message, not a generic error page.
    private static async Task SeedMidlandsTestUserAsync(
        ElectCrmDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger logger)
    {
        if (await userManager.FindByEmailAsync(MidlandsTestUserEmail) is not null)
        {
            logger.LogDebug("User {Email} already exists — skipping", MidlandsTestUserEmail);
            return;
        }

        var password = config[MidlandsTestUserPasswordConfigKey];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "User {Email} not seeded — '{Key}' is missing from user-secrets. " +
                "Set it with: dotnet user-secrets set \"{Key}\" \"<password>\" --project src/ElectCrm.Presentation",
                MidlandsTestUserEmail, MidlandsTestUserPasswordConfigKey, MidlandsTestUserPasswordConfigKey);
            return;
        }

        var brand4 = await dbContext.AgencyBrands
            .FirstOrDefaultAsync(b => b.CompaniesHouseNumber == Brand4Chn);

        if (brand4 is null)
        {
            logger.LogWarning(
                "User {Email} not seeded — Brand 4 (Midlands Industrial Staffing) not found. " +
                "Ensure SeedAdminSliceTestDataAsync has run first.",
                MidlandsTestUserEmail);
            return;
        }

        var userResult = User.Create(new TenantId(brand4.Id), "Midlands Test User", MidlandsTestUserEmail);
        if (userResult.IsFailure)
            throw new InvalidOperationException(
                $"Seed failed — could not create domain user {MidlandsTestUserEmail}: {userResult.Error.Message}");

        dbContext.Users.Add(userResult.Value);

        var appUser = new ApplicationUser
        {
            DomainUserId = userResult.Value.Id,
            Email        = MidlandsTestUserEmail,
            UserName     = MidlandsTestUserEmail,
        };

        var createResult = await userManager.CreateAsync(appUser, password);
        if (!createResult.Succeeded)
            throw new InvalidOperationException(
                $"Seed failed — could not create ApplicationUser {MidlandsTestUserEmail}: " +
                $"{string.Join(", ", createResult.Errors.Select(e => e.Description))}");

        logger.LogInformation(
            "Seeded test user {Email} linked to '{Brand}' (paused) — no admin claims",
            MidlandsTestUserEmail, brand4.TradingName);
    }

    private static async Task SeedVacancySliceTestDataAsync(
        IServiceProvider sp,
        ILogger logger)
    {
        var db          = sp.GetRequiredService<ElectCrmDbContext>();
        var dispatcher  = sp.GetRequiredService<IDomainEventDispatcher>();
        var lf          = sp.GetRequiredService<ILoggerFactory>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        // Load brands — IgnoreQueryFilters so no HTTP tenant context is needed.
        var brand1 = await db.AgencyBrands.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.CompaniesHouseNumber == Brand1Chn);
        var brand2 = await db.AgencyBrands.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.CompaniesHouseNumber == Brand2Chn);
        var brand3 = await db.AgencyBrands.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.CompaniesHouseNumber == Brand3Chn);

        if (brand1 is null || brand2 is null || brand3 is null)
        {
            logger.LogWarning("Vacancy slice seed skipped — required brands not found. Ensure SeedAdminSliceTestDataAsync ran first.");
            return;
        }

        // Idempotency sentinel: the last vacancy seeded is "Demolition Crew — Yorkshire Sites" for brand 3.
        // Checking the final item means partial runs (e.g. app crash mid-seed) will re-enter and
        // create only the missing items — each GetOrCreate helper does its own per-item check.
        var alreadySeeded = await db.Vacancies
            .IgnoreQueryFilters()
            .AnyAsync(v => v.AgencyBrandId == brand3.Id && v.RoleTitle == "Demolition Crew — Yorkshire Sites");

        if (alreadySeeded)
        {
            logger.LogDebug("Vacancy slice test data already seeded — skipping");
            return;
        }

        // Load all branches across all tenants (IgnoreQueryFilters to include retired branches).
        var allBranches = await db.Branches.IgnoreQueryFilters().ToListAsync();

        Branch GetBranch(Guid brandId, string name) =>
            allBranches.First(b => b.AgencyBrandId == brandId && b.Name == name);

        // Look up domain user IDs via ApplicationUser.DomainUserId.
        var adminAppUser  = await userManager.FindByEmailAsync(AdminEmail);
        var admin2AppUser = await userManager.FindByEmailAsync(Admin2Email);
        Guid? admin1Id = adminAppUser?.DomainUserId;
        Guid? admin2Id = admin2AppUser?.DomainUserId;

        // Wire services with a mutable seed tenant context instead of the HTTP-based accessor.
        // This lets us set the current tenant before each service call without changing DI registration.
        var seedCtx = new SeedTenantContext();
        var clientService  = new ClientService(db, seedCtx, dispatcher, lf.CreateLogger<ClientService>());
        var vacancyService = new VacancyService(db, seedCtx, dispatcher, lf.CreateLogger<VacancyService>());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // ── BRAND 1 CLIENTS ──────────────────────────────────────────────────────

        seedCtx.CurrentTenantId = new TenantId(brand1.Id);
        var b1London     = GetBranch(brand1.Id, "London HQ").Id;
        var b1Manchester = GetBranch(brand1.Id, "Manchester").Id;

        var acmeId        = await GetOrCreateClientAsync(clientService, db, brand1.Id, "Acme Construction Ltd",        null, b1London,     ClientStatus.Active, logger);
        var thamesId      = await GetOrCreateClientAsync(clientService, db, brand1.Id, "Thames Civil Engineering Ltd", null, b1London,     ClientStatus.Active, logger);
        var manchesterId  = await GetOrCreateClientAsync(clientService, db, brand1.Id, "Manchester Build Co",          null, b1Manchester, ClientStatus.Active, logger);
        var defunctId     = await GetOrCreateClientAsync(clientService, db, brand1.Id, "Defunct Holdings Ltd",         null, b1London,     ClientStatus.Paused, logger);

        // ── BRAND 2 CLIENTS ──────────────────────────────────────────────────────

        seedCtx.CurrentTenantId = new TenantId(brand2.Id);
        var b2Birmingham = GetBranch(brand2.Id, "Birmingham").Id;

        var birminghamId  = await GetOrCreateClientAsync(clientService, db, brand2.Id, "Birmingham Industrial Services", null, b2Birmingham, ClientStatus.Active, logger);
        var midlandsId    = await GetOrCreateClientAsync(clientService, db, brand2.Id, "Midlands Facilities Ltd",         null, b2Birmingham, ClientStatus.Active, logger);

        // ── BRAND 3 CLIENTS ──────────────────────────────────────────────────────

        seedCtx.CurrentTenantId = new TenantId(brand3.Id);
        var b3Leeds    = GetBranch(brand3.Id, "Leeds").Id;
        var b3Sheffield = GetBranch(brand3.Id, "Sheffield").Id;

        var yorkshireId  = await GetOrCreateClientAsync(clientService, db, brand3.Id, "Yorkshire Build Partners",      null, b3Leeds,     ClientStatus.Active, logger);
        var sheffieldId  = await GetOrCreateClientAsync(clientService, db, brand3.Id, "Sheffield Steel Construction",  null, b3Sheffield, ClientStatus.Active, logger);
        var hullId       = await GetOrCreateClientAsync(clientService, db, brand3.Id, "Hull Marine Engineering Ltd",   null, b3Sheffield, ClientStatus.Active, logger);

        logger.LogInformation("Seeded 9 clients: 4 for Brand 1, 2 for Brand 2, 3 for Brand 3");

        // ── BRAND 1 VACANCIES ─────────────────────────────────────────────────────
        // All creates are sequential — Serializable transactions for reference number generation
        // require this to avoid contention within the same brand.

        seedCtx.CurrentTenantId = new TenantId(brand1.Id);

        // 1. Scaffolders — Canary Wharf (Open)
        await GetOrCreateVacancyAsync(vacancyService, db, brand1.Id,
            new CreateVacancyCommand(b1London, acmeId,
                "Scaffolders — Canary Wharf",
                "Scaffolding crew required for Canary Wharf development project.",
                "EC1A 1AA", null,
                today.AddDays(14), null, null,
                18.50m, "GBP", EngagementType.CIS, false, null,
                28.00m, 6, null, admin1Id, VacancyCreatedFrom.Manual),
            VacancyStatus.Open, null, logger);

        // 2. General Labourers — Stratford Stadium (Open)
        await GetOrCreateVacancyAsync(vacancyService, db, brand1.Id,
            new CreateVacancyCommand(b1London, acmeId,
                "General Labourers — Stratford Stadium",
                "General labour required for stadium construction works.",
                "E20 2ST", null,
                today.AddDays(14), today.AddMonths(3), null,
                14.50m, "GBP", EngagementType.PAYE, true, 1.75m,
                22.00m, 12, null, admin1Id, VacancyCreatedFrom.Manual),
            VacancyStatus.Open, null, logger);

        // 3. Site Manager — Thames Crossing (Open, unassigned consultant, null bill rate)
        await GetOrCreateVacancyAsync(vacancyService, db, brand1.Id,
            new CreateVacancyCommand(b1London, thamesId,
                "Site Manager — Thames Crossing",
                "Experienced site manager required for river crossing civil engineering project.",
                "SE1 7PB", null,
                today.AddDays(14), null, null,
                350.00m, "GBP", EngagementType.Umbrella, false, null,
                null, 1, null, null, VacancyCreatedFrom.Manual),
            VacancyStatus.Open, null, logger);

        // 4. Plant Operators — Manchester Ring Road (Filled, started 1 month ago)
        await GetOrCreateVacancyAsync(vacancyService, db, brand1.Id,
            new CreateVacancyCommand(b1Manchester, manchesterId,
                "Plant Operators — Manchester Ring Road",
                "Plant operators needed for ring road improvement scheme.",
                "M1 1AA", null,
                today.AddDays(-30), today.AddDays(30), null,
                20.00m, "GBP", EngagementType.CIS, false, null,
                32.00m, 4, null, admin1Id, VacancyCreatedFrom.Manual),
            VacancyStatus.Filled, null, logger);

        // 5. Electricians — Office Refurb (ClosedUnfilled, terminal — no StartDate needed)
        await GetOrCreateVacancyAsync(vacancyService, db, brand1.Id,
            new CreateVacancyCommand(b1London, acmeId,
                "Electricians — Office Refurb",
                "Electricians required for commercial office refurbishment.",
                "EC2V 8RF", null,
                null, null, null,
                28.00m, "GBP", EngagementType.Ltd, false, null,
                45.00m, 3, null, admin1Id, VacancyCreatedFrom.Manual),
            VacancyStatus.ClosedUnfilled, "Client cancelled the contract before placement", logger);

        // 6. Carpenters — Hospital Build (Cancelled under paused client — exercises nullable client status display)
        await GetOrCreateVacancyAsync(vacancyService, db, brand1.Id,
            new CreateVacancyCommand(b1London, defunctId,
                "Carpenters — Hospital Build",
                "Skilled carpenters required for hospital construction project.",
                "EC1A 1AA", null,
                null, null, null,
                22.00m, "GBP", EngagementType.PAYE, false, null,
                35.00m, 2, null, admin1Id, VacancyCreatedFrom.Manual),
            VacancyStatus.Cancelled, "Client placed into administration", logger);

        // 7. Bricklayers — Housing Estate (Open)
        await GetOrCreateVacancyAsync(vacancyService, db, brand1.Id,
            new CreateVacancyCommand(b1Manchester, manchesterId,
                "Bricklayers — Housing Estate",
                "Bricklayers required for large housing estate development.",
                "M4 1AA", null,
                today.AddDays(14), null, null,
                19.00m, "GBP", EngagementType.CIS, false, null,
                30.00m, 8, null, admin1Id, VacancyCreatedFrom.Manual),
            VacancyStatus.Open, null, logger);

        // 8. Site Cleaners — Multiple Sites (Draft, null bill rate)
        await GetOrCreateVacancyAsync(vacancyService, db, brand1.Id,
            new CreateVacancyCommand(b1Manchester, manchesterId,
                "Site Cleaners — Multiple Sites",
                "Site cleaners required across multiple construction sites.",
                "M1 1AA", null,
                null, null, null,
                12.50m, "GBP", EngagementType.PAYE, true, 1.51m,
                null, 6, null, admin1Id, VacancyCreatedFrom.Manual),
            VacancyStatus.Draft, null, logger);

        // ── BRAND 2 VACANCIES ─────────────────────────────────────────────────────

        seedCtx.CurrentTenantId = new TenantId(brand2.Id);

        // 9. Warehouse Operatives (Open)
        await GetOrCreateVacancyAsync(vacancyService, db, brand2.Id,
            new CreateVacancyCommand(b2Birmingham, birminghamId,
                "Warehouse Operatives",
                "Warehouse operatives required for logistics distribution centre.",
                "B1 1AA", null,
                today.AddDays(14), null, null,
                13.00m, "GBP", EngagementType.PAYE, true, 1.57m,
                19.50m, 10, null, admin2Id, VacancyCreatedFrom.Manual),
            VacancyStatus.Open, null, logger);

        // 10. Forklift Drivers (Filled, started 1 month ago)
        await GetOrCreateVacancyAsync(vacancyService, db, brand2.Id,
            new CreateVacancyCommand(b2Birmingham, midlandsId,
                "Forklift Drivers",
                "Counterbalance and reach truck operators required.",
                "B2 1AA", null,
                today.AddDays(-30), today.AddDays(60), null,
                15.50m, "GBP", EngagementType.PAYE, false, null,
                24.00m, 3, null, admin2Id, VacancyCreatedFrom.Manual),
            VacancyStatus.Filled, null, logger);

        // 11. Cleaners — Weekend Shift (Cancelled, null bill rate)
        await GetOrCreateVacancyAsync(vacancyService, db, brand2.Id,
            new CreateVacancyCommand(b2Birmingham, birminghamId,
                "Cleaners — Weekend Shift",
                "Weekend cleaning operatives for commercial premises.",
                "B3 1AA", null,
                null, null, null,
                12.00m, "GBP", EngagementType.PAYE, false, null,
                null, 4, null, admin2Id, VacancyCreatedFrom.Manual),
            VacancyStatus.Cancelled, "Client withdrew the requirement", logger);

        // ── BRAND 3 VACANCIES ─────────────────────────────────────────────────────

        seedCtx.CurrentTenantId = new TenantId(brand3.Id);

        // 12. Steel Erectors — Sheffield Mill (Open, null consultant)
        await GetOrCreateVacancyAsync(vacancyService, db, brand3.Id,
            new CreateVacancyCommand(b3Sheffield, sheffieldId,
                "Steel Erectors — Sheffield Mill",
                "Structural steel erectors required for mill conversion project.",
                "S1 1AA", null,
                today.AddDays(14), null, null,
                24.00m, "GBP", EngagementType.CIS, false, null,
                38.00m, 5, null, null, VacancyCreatedFrom.Manual),
            VacancyStatus.Open, null, logger);

        // 13. Marine Engineers (Draft, Sheffield branch — Hull branch is retired, tests correct branch assignment)
        await GetOrCreateVacancyAsync(vacancyService, db, brand3.Id,
            new CreateVacancyCommand(b3Sheffield, hullId,
                "Marine Engineers",
                "Specialist marine engineers for hull inspection and repair works.",
                "S9 1AA", null,
                today.AddDays(14), null, null,
                32.00m, "GBP", EngagementType.Umbrella, false, null,
                52.00m, 2, null, null, VacancyCreatedFrom.Manual),
            VacancyStatus.Draft, null, logger);

        // 14. Demolition Crew — Yorkshire Sites (Open, Leeds branch)
        await GetOrCreateVacancyAsync(vacancyService, db, brand3.Id,
            new CreateVacancyCommand(b3Leeds, yorkshireId,
                "Demolition Crew — Yorkshire Sites",
                "Experienced demolition operatives for multiple Yorkshire sites.",
                "LS1 1AA", null,
                today.AddDays(14), null, null,
                21.00m, "GBP", EngagementType.CIS, false, null,
                33.00m, 6, null, null, VacancyCreatedFrom.Manual),
            VacancyStatus.Open, null, logger);

        logger.LogInformation(
            "Seeded 14 vacancies: 8 for Brand 1, 3 for Brand 2, 3 for Brand 3. " +
            "Status coverage: Draft ×3, Open ×6, Filled ×2, ClosedUnfilled ×1, Cancelled ×2. " +
            "Engagement types: PAYE ×6, CIS ×5, Umbrella ×2, Ltd ×1.");
    }

    private static async Task<Guid> GetOrCreateClientAsync(
        ClientService service,
        ElectCrmDbContext db,
        Guid brandId,
        string legalName,
        string? tradingName,
        Guid branchId,
        ClientStatus targetStatus,
        ILogger logger,
        CancellationToken ct = default)
    {
        var existingId = await db.Clients
            .IgnoreQueryFilters()
            .Where(c => c.AgencyBrandId == brandId && c.LegalName == legalName)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (existingId.HasValue)
        {
            logger.LogDebug("Client '{Name}' already exists — skipping", legalName);
            return existingId.Value;
        }

        var createResult = await service.CreateAsync(new CreateClientCommand(branchId, legalName, tradingName), ct);
        if (createResult.IsFailure)
            throw new InvalidOperationException($"Seed failed — could not create client '{legalName}': {createResult.Error.Message}");

        var clientId = createResult.Value;

        if (targetStatus != ClientStatus.Active)
        {
            var statusResult = await service.ChangeStatusAsync(clientId, targetStatus, ct);
            if (statusResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — could not set status for client '{legalName}': {statusResult.Error.Message}");
        }

        logger.LogInformation("Seeded client '{Name}' (ID {Id}, Status {Status})", legalName, clientId, targetStatus);
        return clientId;
    }

    private static async Task<Guid> GetOrCreateVacancyAsync(
        VacancyService service,
        ElectCrmDbContext db,
        Guid brandId,
        CreateVacancyCommand cmd,
        VacancyStatus targetStatus,
        string? statusReason,
        ILogger logger,
        CancellationToken ct = default)
    {
        var existingId = await db.Vacancies
            .IgnoreQueryFilters()
            .Where(v => v.AgencyBrandId == brandId
                     && v.RoleTitle == cmd.RoleTitle
                     && v.ClientId == cmd.ClientId
                     && v.Location.Postcode == cmd.LocationPostcode)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(ct);

        if (existingId.HasValue)
        {
            logger.LogDebug("Vacancy '{Title}' already exists — skipping", cmd.RoleTitle);
            return existingId.Value;
        }

        var createResult = await service.CreateAsync(cmd, ct);
        if (createResult.IsFailure)
            throw new InvalidOperationException($"Seed failed — could not create vacancy '{cmd.RoleTitle}': {createResult.Error.Message}");

        var vacancyId = createResult.Value;

        switch (targetStatus)
        {
            case VacancyStatus.Draft:
                break;

            case VacancyStatus.Open:
            {
                var result = await service.ChangeStatusAsync(vacancyId,
                    new ChangeVacancyStatusCommand(VacancyStatus.Open, null, null), ct);
                if (result.IsFailure)
                    throw new InvalidOperationException($"Seed failed — could not open vacancy '{cmd.RoleTitle}': {result.Error.Message}");
                break;
            }

            case VacancyStatus.Filled:
            {
                var openResult = await service.ChangeStatusAsync(vacancyId,
                    new ChangeVacancyStatusCommand(VacancyStatus.Open, null, null), ct);
                if (openResult.IsFailure)
                    throw new InvalidOperationException($"Seed failed — could not open vacancy '{cmd.RoleTitle}' for fill: {openResult.Error.Message}");

                var fillResult = await service.ChangeStatusAsync(vacancyId,
                    new ChangeVacancyStatusCommand(VacancyStatus.Filled, null, null), ct);
                if (fillResult.IsFailure)
                    throw new InvalidOperationException($"Seed failed — could not fill vacancy '{cmd.RoleTitle}': {fillResult.Error.Message}");
                break;
            }

            case VacancyStatus.ClosedUnfilled:
            {
                // Close from Draft — valid per domain, no StartDate required.
                var result = await service.ChangeStatusAsync(vacancyId,
                    new ChangeVacancyStatusCommand(VacancyStatus.ClosedUnfilled, statusReason, null), ct);
                if (result.IsFailure)
                    throw new InvalidOperationException($"Seed failed — could not close vacancy '{cmd.RoleTitle}': {result.Error.Message}");
                break;
            }

            case VacancyStatus.Cancelled:
            {
                // Cancel from Draft — valid per domain, no StartDate required.
                var result = await service.ChangeStatusAsync(vacancyId,
                    new ChangeVacancyStatusCommand(VacancyStatus.Cancelled, statusReason, null), ct);
                if (result.IsFailure)
                    throw new InvalidOperationException($"Seed failed — could not cancel vacancy '{cmd.RoleTitle}': {result.Error.Message}");
                break;
            }
        }

        logger.LogInformation("Seeded vacancy '{Title}' (Status {Status})", cmd.RoleTitle, targetStatus);
        return vacancyId;
    }

    private static async Task SeedPlacementSliceTestDataAsync(
        IServiceProvider sp,
        IPersonHashingService hashing,
        ILogger logger,
        CancellationToken ct = default)
    {
        var db          = sp.GetRequiredService<ElectCrmDbContext>();
        var dispatcher  = sp.GetRequiredService<IDomainEventDispatcher>();
        var lf          = sp.GetRequiredService<ILoggerFactory>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var brand1 = await db.AgencyBrands.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.CompaniesHouseNumber == Brand1Chn, ct);
        var brand2 = await db.AgencyBrands.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.CompaniesHouseNumber == Brand2Chn, ct);
        var brand3 = await db.AgencyBrands.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.CompaniesHouseNumber == Brand3Chn, ct);

        if (brand1 is null || brand2 is null || brand3 is null)
        {
            logger.LogWarning("Placement slice seed skipped — required brands not found. Ensure SeedAdminSliceTestDataAsync ran first.");
            return;
        }

        var adminAppUser  = await userManager.FindByEmailAsync(AdminEmail);
        var admin2AppUser = await userManager.FindByEmailAsync(Admin2Email);
        Guid? admin1Id = adminAppUser?.DomainUserId;
        Guid? admin2Id = admin2AppUser?.DomainUserId;

        // Idempotency sentinel: check if John Smith's Brand 1 candidate has any placement
        // for "Scaffolders — Canary Wharf". If so, the entire placement seed has already run.
        var johnSmithBrand1CandidateId = await db.Candidates
            .IgnoreQueryFilters()
            .Where(c => c.AgencyBrandId == brand1.Id)
            .Join(db.Persons,
                c => c.PersonId,
                p => p.Id,
                (c, p) => new { c.Id, p.DisplayName, p.DateOfBirth })
            .Where(x => x.DisplayName == "John Smith" && x.DateOfBirth == new DateOnly(1985, 3, 14))
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);

        if (johnSmithBrand1CandidateId.HasValue)
        {
            var scaffoldersVacancyIdCheck = await db.Vacancies
                .IgnoreQueryFilters()
                .Where(v => v.AgencyBrandId == brand1.Id && v.RoleTitle == "Scaffolders — Canary Wharf")
                .Select(v => (Guid?)v.Id)
                .FirstOrDefaultAsync(ct);

            if (scaffoldersVacancyIdCheck.HasValue)
            {
                var sentinelExists = await db.Placements
                    .IgnoreQueryFilters()
                    .AnyAsync(p => p.CandidateId == johnSmithBrand1CandidateId.Value
                                && p.VacancyId == scaffoldersVacancyIdCheck.Value, ct);

                if (sentinelExists)
                {
                    logger.LogDebug("Placement slice test data already seeded — skipping");
                    return;
                }
            }
        }

        // ── SEED NEW PERSONS ───────────────────────────────────────────────────────

        var personAlreadySeeded = await db.Persons
            .AnyAsync(p => p.DisplayName == "Jane Brown" && p.DateOfBirth == new DateOnly(1991, 5, 12), ct);

        if (!personAlreadySeeded)
        {
            var placementPersonSeeds = PlacementPersonSeeds();

            foreach (var (displayName, dob, phoneRaw, niRaw, passportRaw) in placementPersonSeeds)
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

                db.Persons.Add(result.Value);
                result.Value.ClearDomainEvents();
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} new persons for placement slice", placementPersonSeeds.Count);
        }
        else
        {
            logger.LogDebug("Placement slice persons already seeded — skipping person creation");
        }

        // ── RESOLVE PERSONS ────────────────────────────────────────────────────────

        var allPersons = await db.Persons.Where(p => !p.IsDeleted).ToListAsync(ct);

        Domain.Persons.Person FindPerson(string name, DateOnly dob) =>
            allPersons.FirstOrDefault(p => p.DisplayName == name && p.DateOfBirth == dob)
            ?? throw new InvalidOperationException($"Seed failed — Person '{name}' ({dob:yyyy-MM-dd}) not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // ── SEED NEW CANDIDATES ────────────────────────────────────────────────────

        void AddCandidateIfAbsent(AgencyBrand brand, Domain.Persons.Person person, CandidateStatus status, DateOnly regDate, string trade, string source)
        {
            // Check is done after SaveChanges per batch — tracked via EF local cache check.
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
                throw new InvalidOperationException($"Seed failed — could not create candidate '{person.DisplayName}' for brand '{brand.TradingName}': {result.Error.Message}");

            db.Candidates.Add(result.Value);
            result.Value.ClearDomainEvents();
        }

        // Brand 1 new candidates
        var brand1NewCandidateSeeds = new[]
        {
            (Person: FindPerson("Jane Brown",       new DateOnly(1991,  5, 12)), Trade: "Labourer",       Source: "Indeed",     Status: CandidateStatus.Active, RegDate: today.AddDays(-100)),
            (Person: FindPerson("Robert Davis",     new DateOnly(1983,  8, 22)), Trade: "Plant Operator", Source: "Walk-in",    Status: CandidateStatus.Active, RegDate: today.AddDays(-80)),
            (Person: FindPerson("Sarah Wilson",     new DateOnly(1979,  3,  7)), Trade: "Plant Operator", Source: "Referral",   Status: CandidateStatus.Active, RegDate: today.AddDays(-200)),
            (Person: FindPerson("Michael Taylor",   new DateOnly(1986, 11, 15)), Trade: "Plant Operator", Source: "Find a Job", Status: CandidateStatus.Active, RegDate: today.AddDays(-150)),
            (Person: FindPerson("Emma Johnson",     new DateOnly(1994,  2, 28)), Trade: "Site Manager",   Source: "LinkedIn",   Status: CandidateStatus.Active, RegDate: today.AddDays(-90)),
            (Person: FindPerson("Daniel Martinez",  new DateOnly(1988,  7,  4)), Trade: "Bricklayer",     Source: "Walk-in",    Status: CandidateStatus.Active, RegDate: today.AddDays(-70)),
            (Person: FindPerson("Lisa Anderson",    new DateOnly(1992,  9, 19)), Trade: "Bricklayer",     Source: "Indeed",     Status: CandidateStatus.Active, RegDate: today.AddDays(-60)),
            (Person: FindPerson("Alexander Reid",   new DateOnly(1987,  7, 30)), Trade: "Marine Engineer", Source: "Referral",  Status: CandidateStatus.Active, RegDate: today.AddDays(-110)),
        };

        // Brand 2 new candidates
        var brand2NewCandidateSeeds = new[]
        {
            (Person: FindPerson("Christopher Lee",  new DateOnly(1980, 12, 30)), Trade: "Warehouse Op",   Source: "Indeed",     Status: CandidateStatus.Active, RegDate: today.AddDays(-120)),
            (Person: FindPerson("Patricia Garcia",  new DateOnly(1985,  6, 17)), Trade: "Warehouse Op",   Source: "Referral",   Status: CandidateStatus.Active, RegDate: today.AddDays(-95)),
            (Person: FindPerson("James Rodriguez",  new DateOnly(1977,  4,  2)), Trade: "Forklift Driver", Source: "Walk-in",   Status: CandidateStatus.Active, RegDate: today.AddDays(-250)),
            (Person: FindPerson("Mary Hernandez",   new DateOnly(1990, 10, 25)), Trade: "Warehouse Op",   Source: "Find a Job", Status: CandidateStatus.Active, RegDate: today.AddDays(-50)),
        };

        // Brand 3 new candidates
        var brand3NewCandidateSeeds = new[]
        {
            (Person: FindPerson("William Thompson", new DateOnly(1982,  1, 14)), Trade: "Steel Erector",  Source: "Referral",  Status: CandidateStatus.Active, RegDate: today.AddDays(-140)),
            (Person: FindPerson("Karen White",      new DateOnly(1975,  8,  9)), Trade: "Steel Erector",  Source: "Walk-in",   Status: CandidateStatus.Active, RegDate: today.AddDays(-130)),
            (Person: FindPerson("Steven Clark",     new DateOnly(1989,  3, 21)), Trade: "Steel Erector",  Source: "LinkedIn",  Status: CandidateStatus.Active, RegDate: today.AddDays(-85)),
            (Person: FindPerson("Nancy Lewis",      new DateOnly(1993, 11,  6)), Trade: "Demolition",     Source: "Indeed",    Status: CandidateStatus.Active, RegDate: today.AddDays(-165)),
            (Person: FindPerson("Alexander Reid",   new DateOnly(1987,  7, 30)), Trade: "Marine Engineer", Source: "Referral",  Status: CandidateStatus.Active, RegDate: today.AddDays(-105)),
        };

        // Add candidates only if absent (check by PersonId + AgencyBrandId).
        var existingBrand1CandidatePersonIds = await db.Candidates
            .IgnoreQueryFilters()
            .Where(c => c.AgencyBrandId == brand1.Id)
            .Select(c => c.PersonId)
            .ToListAsync(ct);

        var existingBrand2CandidatePersonIds = await db.Candidates
            .IgnoreQueryFilters()
            .Where(c => c.AgencyBrandId == brand2.Id)
            .Select(c => c.PersonId)
            .ToListAsync(ct);

        var existingBrand3CandidatePersonIds = await db.Candidates
            .IgnoreQueryFilters()
            .Where(c => c.AgencyBrandId == brand3.Id)
            .Select(c => c.PersonId)
            .ToListAsync(ct);

        var brand1CandidatesAdded = 0;
        foreach (var s in brand1NewCandidateSeeds)
        {
            if (existingBrand1CandidatePersonIds.Contains(s.Person.Id))
                continue;

            AddCandidateIfAbsent(brand1, s.Person, s.Status, s.RegDate, s.Trade, s.Source);
            brand1CandidatesAdded++;
        }

        var brand2CandidatesAdded = 0;
        foreach (var s in brand2NewCandidateSeeds)
        {
            if (existingBrand2CandidatePersonIds.Contains(s.Person.Id))
                continue;

            AddCandidateIfAbsent(brand2, s.Person, s.Status, s.RegDate, s.Trade, s.Source);
            brand2CandidatesAdded++;
        }

        var brand3CandidatesAdded = 0;
        foreach (var s in brand3NewCandidateSeeds)
        {
            if (existingBrand3CandidatePersonIds.Contains(s.Person.Id))
                continue;

            AddCandidateIfAbsent(brand3, s.Person, s.Status, s.RegDate, s.Trade, s.Source);
            brand3CandidatesAdded++;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Placement slice candidates seeded: {B1} for Brand 1, {B2} for Brand 2, {B3} for Brand 3. " +
            "Brand 3 now has candidates for the first time. Alexander Reid is cross-brand (Brand 1 + Brand 3).",
            brand1CandidatesAdded, brand2CandidatesAdded, brand3CandidatesAdded);

        // ── WIRE SERVICES ──────────────────────────────────────────────────────────

        var seedCtx          = new SeedTenantContext();
        var vacancyService   = new VacancyService(db, seedCtx, dispatcher, lf.CreateLogger<VacancyService>());
        var placementService = new PlacementService(db, seedCtx, dispatcher, lf.CreateLogger<PlacementService>(), vacancyService);

        // ── PRE-TRANSITION DRAFT VACANCIES TO OPEN ─────────────────────────────────

        // "Scaffolders — Canary Wharf" — open if still Draft (handles DBs seeded before this fix)
        var scaffoldersVacancy = await db.Vacancies
            .IgnoreQueryFilters()
            .FirstAsync(v => v.AgencyBrandId == brand1.Id && v.RoleTitle == "Scaffolders — Canary Wharf", ct);

        if (scaffoldersVacancy.Status == VacancyStatus.Draft)
        {
            seedCtx.CurrentTenantId = new TenantId(brand1.Id);

            // Ensure a start date is set — required before a vacancy can be opened.
            // The original seed created this vacancy without one; set it now.
            if (!scaffoldersVacancy.StartDate.HasValue)
            {
                var updateResult = await vacancyService.UpdateDetailsAsync(
                    scaffoldersVacancy.Id,
                    new UpdateVacancyDetailsCommand(
                        scaffoldersVacancy.RoleTitle,
                        scaffoldersVacancy.Description,
                        scaffoldersVacancy.Location.Postcode,
                        scaffoldersVacancy.Location.Description,
                        today.AddDays(14),
                        scaffoldersVacancy.ExpectedEndDate,
                        scaffoldersVacancy.ShiftPattern,
                        scaffoldersVacancy.HeadcountRequired,
                        scaffoldersVacancy.RequiredCards),
                    ct);
                if (updateResult.IsFailure)
                    throw new InvalidOperationException($"Seed failed — could not set start date on Scaffolders vacancy: {updateResult.Error.Message}");
            }

            var openResult = await vacancyService.ChangeStatusAsync(
                scaffoldersVacancy.Id,
                new ChangeVacancyStatusCommand(VacancyStatus.Open, null, null),
                ct);
            if (openResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — could not open Scaffolders vacancy: {openResult.Error.Message}");

            logger.LogInformation("Pre-transitioned 'Scaffolders — Canary Wharf' from Draft to Open");
        }

        // "Marine Engineers" — open if still Draft (required for P17)
        var marineVacancy = await db.Vacancies
            .IgnoreQueryFilters()
            .FirstAsync(v => v.AgencyBrandId == brand3.Id && v.RoleTitle == "Marine Engineers", ct);

        if (marineVacancy.Status == VacancyStatus.Draft)
        {
            seedCtx.CurrentTenantId = new TenantId(brand3.Id);

            if (!marineVacancy.StartDate.HasValue)
            {
                var updateResult = await vacancyService.UpdateDetailsAsync(
                    marineVacancy.Id,
                    new UpdateVacancyDetailsCommand(
                        marineVacancy.RoleTitle,
                        marineVacancy.Description,
                        marineVacancy.Location.Postcode,
                        marineVacancy.Location.Description,
                        today.AddDays(14),
                        marineVacancy.ExpectedEndDate,
                        marineVacancy.ShiftPattern,
                        marineVacancy.HeadcountRequired,
                        marineVacancy.RequiredCards),
                    ct);
                if (updateResult.IsFailure)
                    throw new InvalidOperationException($"Seed failed — could not set start date on Marine Engineers vacancy: {updateResult.Error.Message}");
            }

            var openResult = await vacancyService.ChangeStatusAsync(
                marineVacancy.Id,
                new ChangeVacancyStatusCommand(VacancyStatus.Open, null, null),
                ct);
            if (openResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — could not open Marine Engineers vacancy: {openResult.Error.Message}");

            logger.LogInformation("Pre-transitioned 'Marine Engineers' from Draft to Open");
        }

        // ── RESOLVE VACANCY IDs ────────────────────────────────────────────────────

        var vacScaffolders     = scaffoldersVacancy.Id;
        var vacStratford       = await GetVacancyIdAsync(db, brand1.Id, "General Labourers — Stratford Stadium", ct);
        var vacThamesCrossing  = await GetVacancyIdAsync(db, brand1.Id, "Site Manager — Thames Crossing", ct);
        var vacManchesterPlant = await GetVacancyIdAsync(db, brand1.Id, "Plant Operators — Manchester Ring Road", ct);
        var vacBricklayers     = await GetVacancyIdAsync(db, brand1.Id, "Bricklayers — Housing Estate", ct);

        var vacWarehouse       = await GetVacancyIdAsync(db, brand2.Id, "Warehouse Operatives", ct);
        var vacForklift        = await GetVacancyIdAsync(db, brand2.Id, "Forklift Drivers", ct);

        var vacSteelErectors   = await GetVacancyIdAsync(db, brand3.Id, "Steel Erectors — Sheffield Mill", ct);
        var vacDemolition      = await GetVacancyIdAsync(db, brand3.Id, "Demolition Crew — Yorkshire Sites", ct);
        var vacMarine          = marineVacancy.Id;

        // ── RESOLVE CANDIDATE IDs ──────────────────────────────────────────────────

        // Brand 1 candidates
        var candJohnSmithB1        = await GetCandidateIdAsync(db, brand1.Id, FindPerson("John Smith",      new DateOnly(1985,  3, 14)).Id, "John Smith (Brand 1)",      ct);
        var candJaneBrownB1        = await GetCandidateIdAsync(db, brand1.Id, FindPerson("Jane Brown",      new DateOnly(1991,  5, 12)).Id, "Jane Brown",                ct);
        var candRobertDavisB1      = await GetCandidateIdAsync(db, brand1.Id, FindPerson("Robert Davis",    new DateOnly(1983,  8, 22)).Id, "Robert Davis",              ct);
        var candSarahWilsonB1      = await GetCandidateIdAsync(db, brand1.Id, FindPerson("Sarah Wilson",    new DateOnly(1979,  3,  7)).Id, "Sarah Wilson",              ct);
        var candMichaelTaylorB1    = await GetCandidateIdAsync(db, brand1.Id, FindPerson("Michael Taylor",  new DateOnly(1986, 11, 15)).Id, "Michael Taylor",            ct);
        var candEmmaJohnsonB1      = await GetCandidateIdAsync(db, brand1.Id, FindPerson("Emma Johnson",    new DateOnly(1994,  2, 28)).Id, "Emma Johnson",              ct);
        var candDanielMartinezB1   = await GetCandidateIdAsync(db, brand1.Id, FindPerson("Daniel Martinez", new DateOnly(1988,  7,  4)).Id, "Daniel Martinez",           ct);
        var candLisaAndersonB1     = await GetCandidateIdAsync(db, brand1.Id, FindPerson("Lisa Anderson",   new DateOnly(1992,  9, 19)).Id, "Lisa Anderson",             ct);
        var candAlexanderReidB1    = await GetCandidateIdAsync(db, brand1.Id, FindPerson("Alexander Reid",  new DateOnly(1987,  7, 30)).Id, "Alexander Reid (Brand 1)",  ct);

        // Brand 2 candidates
        var candChristopherLeeB2   = await GetCandidateIdAsync(db, brand2.Id, FindPerson("Christopher Lee", new DateOnly(1980, 12, 30)).Id, "Christopher Lee",           ct);
        var candPatriciaGarciaB2   = await GetCandidateIdAsync(db, brand2.Id, FindPerson("Patricia Garcia",  new DateOnly(1985,  6, 17)).Id, "Patricia Garcia",           ct);
        var candJamesRodriguezB2   = await GetCandidateIdAsync(db, brand2.Id, FindPerson("James Rodriguez",  new DateOnly(1977,  4,  2)).Id, "James Rodriguez",           ct);
        var candMaryHernandezB2    = await GetCandidateIdAsync(db, brand2.Id, FindPerson("Mary Hernandez",   new DateOnly(1990, 10, 25)).Id, "Mary Hernandez",            ct);

        // Brand 3 candidates
        var candWilliamThompsonB3  = await GetCandidateIdAsync(db, brand3.Id, FindPerson("William Thompson", new DateOnly(1982,  1, 14)).Id, "William Thompson",          ct);
        var candKarenWhiteB3       = await GetCandidateIdAsync(db, brand3.Id, FindPerson("Karen White",       new DateOnly(1975,  8,  9)).Id, "Karen White",               ct);
        var candStevenClarkB3      = await GetCandidateIdAsync(db, brand3.Id, FindPerson("Steven Clark",      new DateOnly(1989,  3, 21)).Id, "Steven Clark",              ct);
        var candNancyLewisB3       = await GetCandidateIdAsync(db, brand3.Id, FindPerson("Nancy Lewis",       new DateOnly(1993, 11,  6)).Id, "Nancy Lewis",               ct);
        var candAlexanderReidB3    = await GetCandidateIdAsync(db, brand3.Id, FindPerson("Alexander Reid",    new DateOnly(1987,  7, 30)).Id, "Alexander Reid (Brand 3)",  ct);

        // ── BRAND 1 PLACEMENTS ─────────────────────────────────────────────────────

        seedCtx.CurrentTenantId = new TenantId(brand1.Id);

        // P1 — John Smith → Scaffolders — Canary Wharf — STATUS: Offered
        if (!await PlacementExistsAsync(db, candJohnSmithB1, vacScaffolders, ct))
        {
            var p1Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacScaffolders, candJohnSmithB1, admin1Id,
                    today.AddDays(14), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p1Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P1 (John Smith → Scaffolders): {p1Result.Error.Message}");
        }

        // P2 — Jane Brown → General Labourers — Stratford Stadium — STATUS: Accepted
        if (!await PlacementExistsAsync(db, candJaneBrownB1, vacStratford, ct))
        {
            var p2Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacStratford, candJaneBrownB1, admin1Id,
                    today.AddDays(7), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p2Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P2 (Jane Brown → Stratford): {p2Result.Error.Message}");

            var p2AcceptResult = await placementService.AcceptAsync(p2Result.Value, ct);
            if (p2AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P2 accepting (Jane Brown → Stratford): {p2AcceptResult.Error.Message}");
        }

        // P3 — Robert Davis → General Labourers — Stratford Stadium — STATUS: Active
        if (!await PlacementExistsAsync(db, candRobertDavisB1, vacStratford, ct))
        {
            var p3Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacStratford, candRobertDavisB1, admin1Id,
                    today.AddDays(-3), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p3Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P3 (Robert Davis → Stratford): {p3Result.Error.Message}");

            var p3AcceptResult = await placementService.AcceptAsync(p3Result.Value, ct);
            if (p3AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P3 accepting (Robert Davis → Stratford): {p3AcceptResult.Error.Message}");

            var p3StartResult = await placementService.StartAsync(p3Result.Value, today.AddDays(-3), ct);
            if (p3StartResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P3 starting (Robert Davis → Stratford): {p3StartResult.Error.Message}");
        }

        // P4 — Sarah Wilson → Plant Operators — Manchester Ring Road — STATUS: Completed
        if (!await PlacementExistsAsync(db, candSarahWilsonB1, vacManchesterPlant, ct))
        {
            var p4Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacManchesterPlant, candSarahWilsonB1, admin1Id,
                    today.AddDays(-42), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p4Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P4 (Sarah Wilson → Manchester Plant): {p4Result.Error.Message}");

            var p4AcceptResult = await placementService.AcceptAsync(p4Result.Value, ct);
            if (p4AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P4 accepting (Sarah Wilson → Manchester Plant): {p4AcceptResult.Error.Message}");

            var p4StartResult = await placementService.StartAsync(p4Result.Value, today.AddDays(-42), ct);
            if (p4StartResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P4 starting (Sarah Wilson → Manchester Plant): {p4StartResult.Error.Message}");

            var p4CompleteResult = await placementService.CompleteAsync(p4Result.Value, today, ct);
            if (p4CompleteResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P4 completing (Sarah Wilson → Manchester Plant): {p4CompleteResult.Error.Message}");
        }

        // P5 — Michael Taylor → Plant Operators — Manchester Ring Road — STATUS: TerminatedEarly
        if (!await PlacementExistsAsync(db, candMichaelTaylorB1, vacManchesterPlant, ct))
        {
            var p5Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacManchesterPlant, candMichaelTaylorB1, admin1Id,
                    today.AddDays(-28), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p5Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P5 (Michael Taylor → Manchester Plant): {p5Result.Error.Message}");

            var p5AcceptResult = await placementService.AcceptAsync(p5Result.Value, ct);
            if (p5AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P5 accepting (Michael Taylor → Manchester Plant): {p5AcceptResult.Error.Message}");

            var p5StartResult = await placementService.StartAsync(p5Result.Value, today.AddDays(-28), ct);
            if (p5StartResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P5 starting (Michael Taylor → Manchester Plant): {p5StartResult.Error.Message}");

            var p5TermResult = await placementService.TerminateEarlyAsync(
                p5Result.Value, today.AddDays(-7),
                "Worker resigned to accept permanent position elsewhere",
                ct);
            if (p5TermResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P5 terminating (Michael Taylor → Manchester Plant): {p5TermResult.Error.Message}");
        }

        // P6 — Emma Johnson → Site Manager — Thames Crossing — STATUS: Declined
        if (!await PlacementExistsAsync(db, candEmmaJohnsonB1, vacThamesCrossing, ct))
        {
            var p6Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacThamesCrossing, candEmmaJohnsonB1, null,
                    today.AddDays(-30), null, 37.5m,
                    null, null, null, null, null, null),
                ct);
            if (p6Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P6 (Emma Johnson → Thames Crossing): {p6Result.Error.Message}");

            var p6DeclineResult = await placementService.DeclineAsync(
                p6Result.Value, "Candidate accepted competing offer", ct);
            if (p6DeclineResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P6 declining (Emma Johnson → Thames Crossing): {p6DeclineResult.Error.Message}");
        }

        // P7 — Daniel Martinez → Bricklayers — Housing Estate — STATUS: Cancelled
        if (!await PlacementExistsAsync(db, candDanielMartinezB1, vacBricklayers, ct))
        {
            var p7Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacBricklayers, candDanielMartinezB1, admin1Id,
                    today.AddDays(-14), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p7Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P7 (Daniel Martinez → Bricklayers): {p7Result.Error.Message}");

            var p7AcceptResult = await placementService.AcceptAsync(p7Result.Value, ct);
            if (p7AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P7 accepting (Daniel Martinez → Bricklayers): {p7AcceptResult.Error.Message}");

            var p7CancelResult = await placementService.CancelAsync(
                p7Result.Value, "Client withdrew the requirement before start date", ct);
            if (p7CancelResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P7 cancelling (Daniel Martinez → Bricklayers): {p7CancelResult.Error.Message}");
        }

        // P8 — Lisa Anderson → Bricklayers — Housing Estate — STATUS: Accepted (BillRate cleared)
        if (!await PlacementExistsAsync(db, candLisaAndersonB1, vacBricklayers, ct))
        {
            var p8Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacBricklayers, candLisaAndersonB1, admin1Id,
                    today.AddDays(7), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p8Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P8 (Lisa Anderson → Bricklayers): {p8Result.Error.Message}");

            var p8AcceptResult = await placementService.AcceptAsync(p8Result.Value, ct);
            if (p8AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P8 accepting (Lisa Anderson → Bricklayers): {p8AcceptResult.Error.Message}");

            // Clear BillRate: Bricklayers vacancy has BillRate = 30.00 which snapshotted on create.
            // UpdateTermsAsync with BillRate = null removes the current bill rate (snapshot is preserved).
            var p8UpdateResult = await placementService.UpdateTermsAsync(
                p8Result.Value,
                new UpdatePlacementTermsCommand(
                    PayRateAmount:       19.00m,
                    PayRateCurrency:     "GBP",
                    EngagementType:      EngagementType.CIS,
                    HolidayPayInclusive: false,
                    HolidayPayRate:      null,
                    BillRate:            null,
                    ProposedStartDate:   today.AddDays(7),
                    ExpectedEndDate:     null,
                    HoursPerWeek:        40m),
                ct);
            if (p8UpdateResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P8 clearing BillRate (Lisa Anderson → Bricklayers): {p8UpdateResult.Error.Message}");

            logger.LogInformation("P8 (Lisa Anderson → Bricklayers): BillRate cleared via UpdateTermsAsync");
        }

        // ── BRAND 2 PLACEMENTS ─────────────────────────────────────────────────────

        seedCtx.CurrentTenantId = new TenantId(brand2.Id);

        // P9 — Christopher Lee → Warehouse Operatives — STATUS: Active
        if (!await PlacementExistsAsync(db, candChristopherLeeB2, vacWarehouse, ct))
        {
            var p9Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacWarehouse, candChristopherLeeB2, admin2Id,
                    today.AddDays(-30), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p9Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P9 (Christopher Lee → Warehouse): {p9Result.Error.Message}");

            var p9AcceptResult = await placementService.AcceptAsync(p9Result.Value, ct);
            if (p9AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P9 accepting (Christopher Lee → Warehouse): {p9AcceptResult.Error.Message}");

            var p9StartResult = await placementService.StartAsync(p9Result.Value, today.AddDays(-30), ct);
            if (p9StartResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P9 starting (Christopher Lee → Warehouse): {p9StartResult.Error.Message}");
        }

        // P10 — Patricia Garcia → Warehouse Operatives — STATUS: Active
        if (!await PlacementExistsAsync(db, candPatriciaGarciaB2, vacWarehouse, ct))
        {
            var p10Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacWarehouse, candPatriciaGarciaB2, admin2Id,
                    today.AddDays(-21), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p10Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P10 (Patricia Garcia → Warehouse): {p10Result.Error.Message}");

            var p10AcceptResult = await placementService.AcceptAsync(p10Result.Value, ct);
            if (p10AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P10 accepting (Patricia Garcia → Warehouse): {p10AcceptResult.Error.Message}");

            var p10StartResult = await placementService.StartAsync(p10Result.Value, today.AddDays(-21), ct);
            if (p10StartResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P10 starting (Patricia Garcia → Warehouse): {p10StartResult.Error.Message}");
        }

        // P11 — James Rodriguez → Forklift Drivers — STATUS: Completed
        if (!await PlacementExistsAsync(db, candJamesRodriguezB2, vacForklift, ct))
        {
            var p11Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacForklift, candJamesRodriguezB2, admin2Id,
                    today.AddDays(-56), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p11Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P11 (James Rodriguez → Forklift): {p11Result.Error.Message}");

            var p11AcceptResult = await placementService.AcceptAsync(p11Result.Value, ct);
            if (p11AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P11 accepting (James Rodriguez → Forklift): {p11AcceptResult.Error.Message}");

            var p11StartResult = await placementService.StartAsync(p11Result.Value, today.AddDays(-56), ct);
            if (p11StartResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P11 starting (James Rodriguez → Forklift): {p11StartResult.Error.Message}");

            var p11CompleteResult = await placementService.CompleteAsync(p11Result.Value, today.AddDays(-14), ct);
            if (p11CompleteResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P11 completing (James Rodriguez → Forklift): {p11CompleteResult.Error.Message}");
        }

        // P12 — Mary Hernandez → Forklift Drivers — STATUS: Offered
        if (!await PlacementExistsAsync(db, candMaryHernandezB2, vacForklift, ct))
        {
            var p12Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacForklift, candMaryHernandezB2, admin2Id,
                    today.AddDays(7), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p12Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P12 (Mary Hernandez → Forklift): {p12Result.Error.Message}");
        }

        // ── BRAND 3 PLACEMENTS ─────────────────────────────────────────────────────

        seedCtx.CurrentTenantId = new TenantId(brand3.Id);

        // P13 — William Thompson → Steel Erectors — Sheffield Mill — STATUS: Active
        if (!await PlacementExistsAsync(db, candWilliamThompsonB3, vacSteelErectors, ct))
        {
            var p13Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacSteelErectors, candWilliamThompsonB3, null,
                    today.AddDays(-14), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p13Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P13 (William Thompson → Steel Erectors): {p13Result.Error.Message}");

            var p13AcceptResult = await placementService.AcceptAsync(p13Result.Value, ct);
            if (p13AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P13 accepting (William Thompson → Steel Erectors): {p13AcceptResult.Error.Message}");

            var p13StartResult = await placementService.StartAsync(p13Result.Value, today.AddDays(-14), ct);
            if (p13StartResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P13 starting (William Thompson → Steel Erectors): {p13StartResult.Error.Message}");
        }

        // P14 — Karen White → Steel Erectors — Sheffield Mill — STATUS: Active
        if (!await PlacementExistsAsync(db, candKarenWhiteB3, vacSteelErectors, ct))
        {
            var p14Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacSteelErectors, candKarenWhiteB3, null,
                    today.AddDays(-10), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p14Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P14 (Karen White → Steel Erectors): {p14Result.Error.Message}");

            var p14AcceptResult = await placementService.AcceptAsync(p14Result.Value, ct);
            if (p14AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P14 accepting (Karen White → Steel Erectors): {p14AcceptResult.Error.Message}");

            var p14StartResult = await placementService.StartAsync(p14Result.Value, today.AddDays(-10), ct);
            if (p14StartResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P14 starting (Karen White → Steel Erectors): {p14StartResult.Error.Message}");
        }

        // P15 — Steven Clark → Steel Erectors — Sheffield Mill — STATUS: Offered
        if (!await PlacementExistsAsync(db, candStevenClarkB3, vacSteelErectors, ct))
        {
            var p15Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacSteelErectors, candStevenClarkB3, null,
                    today.AddDays(7), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p15Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P15 (Steven Clark → Steel Erectors): {p15Result.Error.Message}");
        }

        // P16 — Nancy Lewis → Demolition Crew — Yorkshire Sites — STATUS: Active
        if (!await PlacementExistsAsync(db, candNancyLewisB3, vacDemolition, ct))
        {
            var p16Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacDemolition, candNancyLewisB3, null,
                    today.AddDays(-30), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p16Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P16 (Nancy Lewis → Demolition): {p16Result.Error.Message}");

            var p16AcceptResult = await placementService.AcceptAsync(p16Result.Value, ct);
            if (p16AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P16 accepting (Nancy Lewis → Demolition): {p16AcceptResult.Error.Message}");

            var p16StartResult = await placementService.StartAsync(p16Result.Value, today.AddDays(-30), ct);
            if (p16StartResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P16 starting (Nancy Lewis → Demolition): {p16StartResult.Error.Message}");
        }

        // P17 — Alexander Reid (Brand 3) → Marine Engineers — STATUS: Active
        if (!await PlacementExistsAsync(db, candAlexanderReidB3, vacMarine, ct))
        {
            var p17Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacMarine, candAlexanderReidB3, null,
                    today.AddDays(-7), null, 40m,
                    null, null, null, null, null, null),
                ct);
            if (p17Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P17 (Alexander Reid Brand 3 → Marine Engineers): {p17Result.Error.Message}");

            var p17AcceptResult = await placementService.AcceptAsync(p17Result.Value, ct);
            if (p17AcceptResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P17 accepting (Alexander Reid Brand 3 → Marine Engineers): {p17AcceptResult.Error.Message}");

            var p17StartResult = await placementService.StartAsync(p17Result.Value, today.AddDays(-7), ct);
            if (p17StartResult.IsFailure)
                throw new InvalidOperationException($"Seed failed — P17 starting (Alexander Reid Brand 3 → Marine Engineers): {p17StartResult.Error.Message}");
        }

        // P18 — Alexander Reid (Brand 1) → Site Manager — Thames Crossing — STATUS: Offered
        seedCtx.CurrentTenantId = new TenantId(brand1.Id);

        if (!await PlacementExistsAsync(db, candAlexanderReidB1, vacThamesCrossing, ct))
        {
            var p18Result = await placementService.CreateAsync(
                new CreatePlacementCommand(
                    vacThamesCrossing, candAlexanderReidB1, admin1Id,
                    today.AddDays(14), null, 37.5m,
                    null, null, null, null, null, null),
                ct);
            if (p18Result.IsFailure)
                throw new InvalidOperationException($"Seed failed — P18 (Alexander Reid Brand 1 → Thames Crossing): {p18Result.Error.Message}");
        }

        logger.LogInformation(
            "Seeded placement slice test data: {Total} placements across 3 brands. " +
            "Status coverage: Offered ×{Offered}, Accepted ×{Accepted}, Active ×{Active}, " +
            "Completed ×{Completed}, TerminatedEarly ×{TE}, Declined ×{Declined}, Cancelled ×{Cancelled}. " +
            "Cross-brand person: Alexander Reid (Brand 1 Offered + Brand 3 Active).",
            18, 4, 2, 6, 2, 1, 1, 1);
    }

    // (DisplayName, DateOfBirth, PrimaryPhoneRaw, NiNumberRaw, PassportNumberRaw)
    private static List<(string DisplayName, DateOnly? Dob, string? Phone, string? NiNumber, string? Passport)>
        PlacementPersonSeeds() =>
    [
        ("Jane Brown",         new DateOnly(1991,  5, 12), "+447700901001", "CA112233A", null),
        ("Robert Davis",       new DateOnly(1983,  8, 22), "+447700901002", "EC223344B", null),
        ("Sarah Wilson",       new DateOnly(1979,  3,  7), "+447700901003", "GH334455C", "234567001"),
        ("Michael Taylor",     new DateOnly(1986, 11, 15), "+447700901004", "JK445566D", null),
        ("Emma Johnson",       new DateOnly(1994,  2, 28), "+447700901005", "KL556677A", "234567005"),
        ("Daniel Martinez",    new DateOnly(1988,  7,  4), null,            "MN667788B", null),
        ("Lisa Anderson",      new DateOnly(1992,  9, 19), "+447700901007", "OP778899C", null),
        ("Christopher Lee",    new DateOnly(1980, 12, 30), "+447700901008", "PR889900D", "234567008"),
        ("Patricia Garcia",    new DateOnly(1985,  6, 17), "+447700901009", "ST990011A", null),
        ("James Rodriguez",    new DateOnly(1977,  4,  2), "+447700901010", "TW001122B", null),
        ("Mary Hernandez",     new DateOnly(1990, 10, 25), "+447700901011", "WX112233C", "234567011"),
        ("William Thompson",   new DateOnly(1982,  1, 14), "+447700901012", "YZ223344D", null),
        ("Karen White",        new DateOnly(1975,  8,  9), "+447700901013", "AB334455A", "234567013"),
        ("Steven Clark",       new DateOnly(1989,  3, 21), "+447700901014", "CG445566B", null),
        ("Nancy Lewis",        new DateOnly(1993, 11,  6), "+447700901015", "EJ556677C", null),
        ("Alexander Reid",     new DateOnly(1987,  7, 30), "+447700901016", "GH667788D", "234567016"),
    ];

    private static async Task<Guid> GetCandidateIdAsync(
        ElectCrmDbContext db,
        Guid brandId,
        Guid personId,
        string personName,
        CancellationToken ct)
    {
        var candidateId = await db.Candidates
            .IgnoreQueryFilters()
            .Where(c => c.AgencyBrandId == brandId && c.PersonId == personId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (!candidateId.HasValue)
            throw new InvalidOperationException($"Seed failed — candidate for '{personName}' not found in brand {brandId}.");

        return candidateId.Value;
    }

    private static async Task<Guid> GetVacancyIdAsync(
        ElectCrmDbContext db,
        Guid brandId,
        string roleTitle,
        CancellationToken ct)
    {
        var vacancyId = await db.Vacancies
            .IgnoreQueryFilters()
            .Where(v => v.AgencyBrandId == brandId && v.RoleTitle == roleTitle)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(ct);

        if (!vacancyId.HasValue)
            throw new InvalidOperationException($"Seed failed — vacancy '{roleTitle}' not found for brand {brandId}.");

        return vacancyId.Value;
    }

    private static async Task<bool> PlacementExistsAsync(
        ElectCrmDbContext db,
        Guid candidateId,
        Guid vacancyId,
        CancellationToken ct)
    {
        return await db.Placements
            .IgnoreQueryFilters()
            .AnyAsync(p => p.CandidateId == candidateId && p.VacancyId == vacancyId, ct);
    }

    private static async Task SeedUserManagementSliceDataAsync(
        ElectCrmDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger logger)
    {
        // ── PART A — Backfill existing users ─────────────────────────────────────
        // Part A always runs regardless of whether Part B has already seeded.

        logger.LogInformation("User management slice — starting Part A backfill of existing users");

        var allBranches = await dbContext.Branches.IgnoreQueryFilters().ToListAsync();

        // admin@elect.group — GroupAdmin/BrandAdmin, no branch, DisplayName="Elect Admin", JobTitle="Group Administrator"
        var adminUser = await userManager.FindByEmailAsync(AdminEmail);
        if (adminUser is null)
        {
            logger.LogWarning("Backfill: user {Email} not found — skipping", AdminEmail);
        }
        else
        {
            var adminChanged = false;

            if (string.IsNullOrEmpty(adminUser.DisplayName))
            {
                adminUser.DisplayName = "Elect Admin";
                adminChanged = true;
            }
            else if (adminUser.DisplayName != "Elect Admin")
            {
                logger.LogInformation(
                    "Backfill: {Email} DisplayName is '{Current}' — leaving as-is",
                    AdminEmail, adminUser.DisplayName);
            }

            if (adminUser.JobTitle is null)
            {
                adminUser.JobTitle = "Group Administrator";
                adminChanged = true;
            }

            // PrimaryBranchId — plan specifies NULL for this user; leave as-is.

            if (adminChanged)
            {
                adminUser.UpdatedAt = DateTimeOffset.UtcNow;
                var updateResult = await userManager.UpdateAsync(adminUser);
                if (!updateResult.Succeeded)
                    logger.LogWarning(
                        "Backfill: could not update {Email}: {Errors}",
                        AdminEmail,
                        string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                else
                    logger.LogInformation("Backfill: updated {Email}", AdminEmail);
            }
            else
            {
                logger.LogDebug("Backfill: {Email} already up to date — no changes", AdminEmail);
            }
        }

        // admin2@elect.group — same pattern
        var admin2User = await userManager.FindByEmailAsync(Admin2Email);
        if (admin2User is null)
        {
            logger.LogWarning("Backfill: user {Email} not found — skipping", Admin2Email);
        }
        else
        {
            var admin2Changed = false;

            if (string.IsNullOrEmpty(admin2User.DisplayName))
            {
                admin2User.DisplayName = "Elect Admin 2";
                admin2Changed = true;
            }
            else if (admin2User.DisplayName != "Elect Admin 2")
            {
                logger.LogInformation(
                    "Backfill: {Email} DisplayName is '{Current}' — leaving as-is",
                    Admin2Email, admin2User.DisplayName);
            }

            if (admin2User.JobTitle is null)
            {
                admin2User.JobTitle = "Group Administrator";
                admin2Changed = true;
            }

            if (admin2Changed)
            {
                admin2User.UpdatedAt = DateTimeOffset.UtcNow;
                var updateResult = await userManager.UpdateAsync(admin2User);
                if (!updateResult.Succeeded)
                    logger.LogWarning(
                        "Backfill: could not update {Email}: {Errors}",
                        Admin2Email,
                        string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                else
                    logger.LogInformation("Backfill: updated {Email}", Admin2Email);
            }
            else
            {
                logger.LogDebug("Backfill: {Email} already up to date — no changes", Admin2Email);
            }
        }

        // user@midlands-industrial.test — Consultant, Brand 4 / Coventry branch
        var midlandsUser = await userManager.FindByEmailAsync(MidlandsTestUserEmail);
        if (midlandsUser is null)
        {
            logger.LogWarning("Backfill: user {Email} not found — skipping", MidlandsTestUserEmail);
        }
        else
        {
            var midlandsChanged = false;

            if (string.IsNullOrEmpty(midlandsUser.DisplayName))
            {
                midlandsUser.DisplayName = "Midlands Test User";
                midlandsChanged = true;
            }
            else if (midlandsUser.DisplayName != "Midlands Test User")
            {
                logger.LogInformation(
                    "Backfill: {Email} DisplayName is '{Current}' — leaving as-is",
                    MidlandsTestUserEmail, midlandsUser.DisplayName);
            }

            if (midlandsUser.JobTitle is null)
            {
                midlandsUser.JobTitle = "Test Consultant";
                midlandsChanged = true;
            }

            // PrimaryBranchId — set to Coventry branch (Brand 4) if not already set
            if (midlandsUser.PrimaryBranchId is null)
            {
                var brand4 = await dbContext.AgencyBrands
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(b => b.CompaniesHouseNumber == Brand4Chn);

                if (brand4 is null)
                {
                    logger.LogWarning(
                        "Backfill: {Email} — Brand 4 not found; cannot set PrimaryBranchId",
                        MidlandsTestUserEmail);
                }
                else
                {
                    var coventryBranch = allBranches
                        .FirstOrDefault(b => b.Name == "Coventry" && b.AgencyBrandId == brand4.Id);

                    if (coventryBranch is null)
                    {
                        logger.LogWarning(
                            "Backfill: {Email} — Coventry branch not found; cannot set PrimaryBranchId",
                            MidlandsTestUserEmail);
                    }
                    else
                    {
                        midlandsUser.PrimaryBranchId = coventryBranch.Id;
                        midlandsChanged = true;
                    }
                }
            }

            if (midlandsChanged)
            {
                midlandsUser.UpdatedAt = DateTimeOffset.UtcNow;
                var updateResult = await userManager.UpdateAsync(midlandsUser);
                if (!updateResult.Succeeded)
                    logger.LogWarning(
                        "Backfill: could not update {Email}: {Errors}",
                        MidlandsTestUserEmail,
                        string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                else
                    logger.LogInformation("Backfill: updated {Email}", MidlandsTestUserEmail);
            }
            else
            {
                logger.LogDebug("Backfill: {Email} already up to date — no changes", MidlandsTestUserEmail);
            }

            // Check and add Consultant role claim if none exists
            var existingClaims = await userManager.GetClaimsAsync(midlandsUser);
            var hasRoleClaim = existingClaims.Any(c => c.Type == ElectClaimTypes.Role);
            if (!hasRoleClaim)
            {
                var brand4ForClaim = await dbContext.AgencyBrands
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(b => b.CompaniesHouseNumber == Brand4Chn);

                if (brand4ForClaim is null)
                {
                    logger.LogWarning(
                        "Backfill: {Email} — Brand 4 not found; cannot add Consultant claim",
                        MidlandsTestUserEmail);
                }
                else
                {
                    var claimValue = $"{nameof(RoleName.Consultant)}:{nameof(RoleScope.Brand)}:{brand4ForClaim.Id}";
                    var claimResult = await userManager.AddClaimAsync(
                        midlandsUser,
                        new Claim(ElectClaimTypes.Role, claimValue));

                    if (!claimResult.Succeeded)
                        logger.LogWarning(
                            "Backfill: could not add Consultant claim for {Email}: {Errors}",
                            MidlandsTestUserEmail,
                            string.Join(", ", claimResult.Errors.Select(e => e.Description)));
                    else
                        logger.LogInformation(
                            "Backfill: added Consultant claim for {Email} (Brand {BrandId})",
                            MidlandsTestUserEmail, brand4ForClaim.Id);
                }
            }
            else
            {
                logger.LogDebug(
                    "Backfill: {Email} already has role claim(s) — skipping claim add",
                    MidlandsTestUserEmail);
            }
        }

        logger.LogInformation("User management slice — Part A backfill complete");

        // ── PART B — Seed new test users ─────────────────────────────────────────
        // Sentinel: if brandadmin1@elect.group already exists, Part B has already run.

        if (await userManager.FindByEmailAsync("brandadmin1@elect.group") is not null)
        {
            logger.LogDebug("User management slice Part B already seeded — skipping");
            return;
        }

        var brandAdminPassword = config["DevSeed:BrandAdminPassword"];
        var consultantPassword = config["DevSeed:ConsultantPassword"];

        var passwordsMissing = false;

        if (string.IsNullOrWhiteSpace(brandAdminPassword))
        {
            logger.LogError(
                "User management seed Part B skipped — 'DevSeed:BrandAdminPassword' is missing from user-secrets. " +
                "Set it with: dotnet user-secrets set \"DevSeed:BrandAdminPassword\" \"<password>\" --project src/ElectCrm.Presentation");
            passwordsMissing = true;
        }

        if (string.IsNullOrWhiteSpace(consultantPassword))
        {
            logger.LogError(
                "User management seed Part B skipped — 'DevSeed:ConsultantPassword' is missing from user-secrets. " +
                "Set it with: dotnet user-secrets set \"DevSeed:ConsultantPassword\" \"<password>\" --project src/ElectCrm.Presentation");
            passwordsMissing = true;
        }

        if (passwordsMissing)
            return;

        logger.LogInformation("User management slice — starting Part B: seeding 12 new test users");

        // Load brands by CHN
        var b1 = await dbContext.AgencyBrands.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.CompaniesHouseNumber == Brand1Chn);
        var b2 = await dbContext.AgencyBrands.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.CompaniesHouseNumber == Brand2Chn);
        var b3 = await dbContext.AgencyBrands.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.CompaniesHouseNumber == Brand3Chn);

        if (b1 is null || b2 is null || b3 is null)
        {
            logger.LogWarning(
                "User management seed Part B skipped — one or more required brands not found. " +
                "Ensure SeedAdminSliceTestDataAsync has run first.");
            return;
        }

        // (email, displayName, branchName, brand, jobTitle, roleName, roleScope)
        var userPlan = new[]
        {
            // Brand 1
            ("brandadmin1@elect.group",   "Sarah Chen",       "London HQ",   b1, "Brand Administrator",    nameof(RoleName.BrandAdmin),  nameof(RoleScope.Brand),  brandAdminPassword),
            ("consultant1a@elect.group",  "Tom Hughes",       "London HQ",   b1, "Recruitment Consultant",  nameof(RoleName.Consultant),  nameof(RoleScope.Brand),  consultantPassword),
            ("consultant1b@elect.group",  "Emily Rodriguez",  "London HQ",   b1, "Recruitment Consultant",  nameof(RoleName.Consultant),  nameof(RoleScope.Brand),  consultantPassword),
            ("consultant1c@elect.group",  "David Kim",        "Manchester",  b1, "Recruitment Consultant",  nameof(RoleName.Consultant),  nameof(RoleScope.Brand),  consultantPassword),

            // Brand 2
            ("brandadmin2@elect.group",   "Marcus Webb",      "Birmingham",  b2, "Brand Administrator",    nameof(RoleName.BrandAdmin),  nameof(RoleScope.Brand),  brandAdminPassword),
            ("consultant2a@elect.group",  "Priya Patel",      "Birmingham",  b2, "Recruitment Consultant",  nameof(RoleName.Consultant),  nameof(RoleScope.Brand),  consultantPassword),
            ("consultant2b@elect.group",  "James OConnor",    "Birmingham",  b2, "Recruitment Consultant",  nameof(RoleName.Consultant),  nameof(RoleScope.Brand),  consultantPassword),

            // Brand 3
            ("brandadmin3@northern.test",  "Helen Thornton",  "Leeds",       b3, "Brand Administrator",    nameof(RoleName.BrandAdmin),  nameof(RoleScope.Brand),  brandAdminPassword),
            ("consultant3a@northern.test", "Robert Singh",    "Leeds",       b3, "Recruitment Consultant",  nameof(RoleName.Consultant),  nameof(RoleScope.Brand),  consultantPassword),
            ("consultant3b@northern.test", "Karen Foster",    "Sheffield",   b3, "Recruitment Consultant",  nameof(RoleName.Consultant),  nameof(RoleScope.Brand),  consultantPassword),
            ("consultant3c@northern.test", "Michael Park",    "Sheffield",   b3, "Recruitment Consultant",  nameof(RoleName.Consultant),  nameof(RoleScope.Brand),  consultantPassword),
        };

        var createdCount = 0;

        foreach (var (email, displayName, branchName, brand, jobTitle, roleName, roleScope, password) in userPlan)
        {
            // Check if user already exists (idempotent per-user guard)
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                logger.LogDebug("Seeding new user {Email} — already exists, skipping", email);
                continue;
            }

            // Look up branch
            var branch = allBranches.FirstOrDefault(b => b.Name == branchName && b.AgencyBrandId == brand.Id);
            if (branch is null)
            {
                logger.LogError(
                    "Seeding new user {Email} — branch '{Branch}' not found for brand '{Brand}'; skipping this user",
                    email, branchName, brand.TradingName);
                continue;
            }

            // Create domain User entity
            var userResult = User.Create(new TenantId(brand.Id), displayName, email);
            if (userResult.IsFailure)
            {
                logger.LogError(
                    "Seeding new user {Email} — could not create domain user: {Error}; skipping",
                    email, userResult.Error.Message);
                continue;
            }

            dbContext.Users.Add(userResult.Value);
            await dbContext.SaveChangesAsync();

            // Create ApplicationUser
            var appUser = new ApplicationUser
            {
                DomainUserId          = userResult.Value.Id,
                Email                 = email,
                UserName              = email,
                DisplayName           = displayName,
                PrimaryBranchId       = branch.Id,
                JobTitle              = jobTitle,
                IsActive              = true,
                RequirePasswordChange = false,
                CreatedAt             = DateTimeOffset.UtcNow,
                UpdatedAt             = DateTimeOffset.UtcNow,
                LastModifiedById      = null,
            };

            var createResult = await userManager.CreateAsync(appUser, password!);
            if (!createResult.Succeeded)
            {
                logger.LogError(
                    "Seeding new user {Email} — could not create ApplicationUser: {Errors}",
                    email,
                    string.Join(", ", createResult.Errors.Select(e => e.Description)));

                // Roll back the committed domain User row.
                dbContext.Users.Remove(userResult.Value);
                try
                {
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception rollbackEx)
                {
                    logger.LogError(rollbackEx,
                        "CRITICAL: Failed to roll back orphaned domain User {DomainUserId} for {Email} " +
                        "after ApplicationUser creation failure. Manual cleanup required.",
                        userResult.Value.Id, email);
                }

                continue;
            }

            // Add role claim
            var claimValue = $"{roleName}:{roleScope}:{brand.Id}";
            var claimsResult = await userManager.AddClaimAsync(
                appUser,
                new Claim(ElectClaimTypes.Role, claimValue));

            if (!claimsResult.Succeeded)
            {
                logger.LogError(
                    "Seeding new user {Email} — could not add role claim '{Claim}': {Errors}",
                    email, claimValue,
                    string.Join(", ", claimsResult.Errors.Select(e => e.Description)));
                // User was created; don't block continuation
                continue;
            }

            logger.LogInformation(
                "Seeded user {Email} ('{DisplayName}', {Role} for brand '{Brand}', branch '{Branch}')",
                email, displayName, roleName, brand.TradingName, branchName);
            createdCount++;
        }

        logger.LogInformation(
            "User management slice Part B complete — {Created} of {Total} new users seeded",
            createdCount, userPlan.Length);
    }

    // Simulates tenant context for seeder code that runs outside of an HTTP request.
    // The HTTP-based TenantContextAccessor returns TenantId.Empty when HttpContext is null;
    // this class lets us set the tenant explicitly before each service call.
    private sealed class SeedTenantContext : ITenantContext
    {
        public TenantId CurrentTenantId { get; set; } = TenantId.Empty;
    }
}
