using System.ComponentModel.DataAnnotations;

namespace ShoppingSysem.Models;

public enum ProductStatus
{
    Draft = 1,
    PendingApproval = 2,
    Active = 3,
    Rejected = 4,
    Archived = 5
}

public enum ProductCondition
{
    New = 1,
    Used = 2
}

/// <summary>Limits for the product catalogue extras.</summary>
public static class ProductLimits
{
    public const int AttributeNameMaxLength = 100;
    public const int AttributeValueMaxLength = 300;
    public const int VariantOptionMaxLength = 100;
    public const int ImagePathMaxLength = 260;
    public const int RejectionReasonMaxLength = 500;
    public const int MaxImagesPerProduct = 10;
    public const int MaxWeightGrams = 1_000_000;
    public const decimal MaxDimensionCm = 10_000m;
}

public class ProductAttribute
{
    [Key]
    public int Id { get; set; }

    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    [Required, MaxLength(ProductLimits.AttributeNameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(ProductLimits.AttributeValueMaxLength)]
    public string Value { get; set; } = string.Empty;
}

public class ProductVariant
{
    [Key]
    public int Id { get; set; }

    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    // Unique within the product.
    [Required, MaxLength(Data.ValidationLimits.SkuMaxLength)]
    public string SKU { get; set; } = string.Empty;

    [MaxLength(ProductLimits.VariantOptionMaxLength)]
    public string? Size { get; set; }

    [MaxLength(ProductLimits.VariantOptionMaxLength)]
    public string? Colour { get; set; }

    [MaxLength(ProductLimits.VariantOptionMaxLength)]
    public string? Model { get; set; }

    [MaxLength(ProductLimits.VariantOptionMaxLength)]
    public string? Material { get; set; }

    [Range(0, (double)Data.ValidationLimits.MaxPrice)]
    public decimal Price { get; set; }

    [Range(0, Data.ValidationLimits.MaxStock)]
    public int Stock { get; set; }

    public bool IsActive { get; set; } = true;
}

public class ProductImage
{
    [Key]
    public int Id { get; set; }

    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    // Paths relative to the image root. Binary data is never stored in the database.
    [Required, MaxLength(ProductLimits.ImagePathMaxLength)]
    public string FilePath { get; set; } = string.Empty;

    [Required, MaxLength(ProductLimits.ImagePathMaxLength)]
    public string ThumbnailPath { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}