namespace ElectCrm.Domain.Common;

public sealed class BrandInactiveException : Exception
{
    public BrandInactiveException()
        : base("Your organisation's account has been suspended. Contact your administrator.")
    {
    }
}
