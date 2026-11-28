using System.ComponentModel.DataAnnotations;
using ShoppingSysem.Data;

namespace ShoppingSysem.Models
{
    public class Item
    {
        [Key]
        public int Id { get; set; }

        // Name is the persisted product title to preserve existing integrations.
        [Required]
        [MaxLength(ValidationLimits.ItemNameMaxLength)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(ValidationLimits.SkuMaxLength)]
        public string SKU { get; set; } = string.Empty;

        [MaxLength(ValidationLimits.BrandMaxLength)]
        public string? Brand { get; set; }

        [MaxLength(ValidationLimits.ShortDescriptionMaxLength)]
        public string? ShortDescription { get; set; }

        public string? FullDescription { get; set; }

        // Base price/stock. Used directly when the product has no variants.
        [Range(0, (double)ValidationLimits.MaxPrice)]
        public decimal Price { get; set; }

        [Range(0, ValidationLimits.MaxStock)]
        public int Stock { get; set; }

        [Required]
        public int ShopId { get; set; }
        public Shop Shop { get; set; } = null!;

        [MaxLength(ValidationLimits.CategoryMaxLength)]
        public string? Category { get; set; }

        [MaxLength(ValidationLimits.CategoryMaxLength)]
        public string? Subcategory { get; set; }

        public ProductCondition Condition { get; set; } = ProductCondition.New;

        // Defaults to Active so existing products and flows keep working.
        // New products created by shopkeepers are set to Draft/PendingApproval by the UI.
        public ProductStatus Status { get; set; } = ProductStatus.Active;

        [MaxLength(ProductLimits.RejectionReasonMaxLength)]
        public string? RejectionReason { get; set; }

        // Delivery calculation data.
        [Range(0, ProductLimits.MaxWeightGrams)]
        public int? WeightGrams { get; set; }

        [Range(0, (double)ProductLimits.MaxDimensionCm)]
        public decimal? LengthCm { get; set; }

        [Range(0, (double)ProductLimits.MaxDimensionCm)]
        public decimal? WidthCm { get; set; }

        [Range(0, (double)ProductLimits.MaxDimensionCm)]
        public decimal? HeightCm { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public List<ProductAttribute> Attributes { get; set; } = new();
        public List<ProductVariant> Variants { get; set; } = new();
        public List<ProductImage> Images { get; set; } = new();
    }
}