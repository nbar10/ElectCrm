namespace ElectCrm.Presentation.Authorization;

public static class PolicyNames
{
    public const string AnyStaff = nameof(AnyStaff);
    public const string Consultant = nameof(Consultant);
    public const string BranchManager = nameof(BranchManager);
    public const string BrandAdmin = nameof(BrandAdmin);
    public const string ComplianceOfficer = nameof(ComplianceOfficer);
    public const string FinanceOfficer = nameof(FinanceOfficer);
    public const string GroupAdmin = nameof(GroupAdmin);
}

public static class ElectClaimTypes
{
    public const string Role = "elect_role";
    public const string AgencyBrandId = "agency_brand_id";
}
