namespace ElectCrm.Shared;

public readonly record struct Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error Unauthorized = new("Unauthorized", "You do not have permission to perform this action.");
    public static readonly Error NotFound = new("NotFound", "The requested resource was not found.");

    public static Error Validation(string message) => new("Validation", message);
}
