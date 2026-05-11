namespace ElectCrm.Presentation.Validation;

using System.ComponentModel.DataAnnotations;

[AttributeUsage(AttributeTargets.Property)]
public sealed class NotEmptyGuidAttribute : ValidationAttribute
{
    public NotEmptyGuidAttribute() : base("A valid ID is required.") { }

    public override bool IsValid(object? value) =>
        value is Guid g && g != Guid.Empty;
}
