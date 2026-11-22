using System.ComponentModel.DataAnnotations;
using ShoppingSysem.Data;

namespace ShoppingSysem.Models
{
    public class Item
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(ValidationLimits.ItemNameMaxLength)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(250)]
        public string? ShortDescription { get; set; }

        [MaxLength(5000)]
        public string? FullDescription { get; set; }

        [Range(0, (double)ValidationLimits.MaxPrice)]
        public decimal Price { get; set; }

        [Range(0, ValidationLimits.MaxStock)]
        public int Stock { get; set; }

        // Foreign key: which shop this item belongs to
        [Required]
        public int ShopId { get; set; }
        public Shop Shop { get; set; } = null!;

        [MaxLength(ValidationLimits.CategoryMaxLength)]
        public string? Category { get; set; }
    }
}
