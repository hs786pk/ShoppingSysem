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
    public const int SkuMaxLength = 64;
    public const int BrandMaxLength = 100;
    public const int ShortDescriptionMaxLength = 300;
    public const int ShopNameMaxLength = 100;
    public const int ShopLocationMaxLength = 200;

    public const decimal MaxPrice = 100_000_000m;
    public const int MaxStock = 1_000_000;
    public const int MaxQuantityPerTransaction = 10_000;

    public const int MinPasswordLength = 12;
    public const int MaxEmailLength = 256;

    // Product images.
    public const int MaxImageBytes = 5 * 1024 * 1024;
    public const int MinImageDimension = 300;
    public const int MaxImageDimension = 6000;
    public const int ImageMaxSide = 1600;
    public const int ThumbnailMaxSide = 300;
}