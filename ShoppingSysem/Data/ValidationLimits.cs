namespace ShoppingSysem.Data;

/// <summary>
/// Single source of truth for numeric and length limits used by
/// forms, services, and model attributes. Keep these aligned with
/// DataAnnotations on the entity models.
/// </summary>
public static class ValidationLimits
{
    public const int ItemNameMaxLength = 100;
    public const int CategoryMaxLength = 100;
    public const int ShopNameMaxLength = 100;
    public const int ShopLocationMaxLength = 200;

    public const decimal MaxPrice = 100_000_000m;
    public const int MaxStock = 1_000_000;
    public const int MaxQuantityPerTransaction = 10_000;

    public const int MinPasswordLength = 12;
    public const int MaxEmailLength = 256;
}
