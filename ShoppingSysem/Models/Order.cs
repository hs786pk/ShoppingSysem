using System.ComponentModel.DataAnnotations;
using ShoppingSysem.Data;

namespace ShoppingSysem.Models
{
    public enum OrderStatus
    {
        Placed = 1,
        Fulfilled = 2,
        Cancelled = 3
    }
    public class Order
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string CustomerId { get; set; } = string.Empty;
        public ApplicationUser Customer { get; set; } = null!;

        [Required]
        public int ItemId { get; set; }
        public Item Item { get; set; } = null!;

        // Optional: set when the customer bought a specific variant.
        public int? VariantId { get; set; }
        public ProductVariant? Variant { get; set; }

        // Snapshot such as "Large / Red", kept even if the variant changes later.
        [MaxLength(300)]
        public string? VariantLabel { get; set; }

        [Required]
        public int ShopId { get; set; }
        public Shop Shop { get; set; } = null!;

        [Range(1, ValidationLimits.MaxQuantityPerTransaction)]
        public int Quantity { get; set; }

        // Snapshot. Later price edits must not change this order.
        [Range(0, (double)ValidationLimits.MaxPrice)]
        public decimal UnitPrice { get; set; }

        [Range(0, 1_000_000_000_000)]
        public decimal TotalPrice { get; set; }

        public DateTime OrderedAt { get; set; } = DateTime.UtcNow;

        public OrderStatus Status { get; set; } = OrderStatus.Placed;
    }
}