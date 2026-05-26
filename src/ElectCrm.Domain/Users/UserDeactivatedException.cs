namespace ElectCrm.Domain.Users;

public sealed class UserDeactivatedException : Exception
{
    public UserDeactivatedException()
        : base("Your account has been deactivated. Contact your administrator.")
    {
    }
}
