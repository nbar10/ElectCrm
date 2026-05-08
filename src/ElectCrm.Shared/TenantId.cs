namespace ElectCrm.Shared;

public readonly record struct TenantId(Guid Value)
{
    public static readonly TenantId Empty = new(Guid.Empty);

    public static implicit operator Guid(TenantId id) => id.Value;
    public static implicit operator TenantId(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}
