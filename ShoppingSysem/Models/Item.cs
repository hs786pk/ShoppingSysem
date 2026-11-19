using System.ComponentModel.DataAnnotations;

namespace ShoppingSysem.Models
{
    public class Item
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Range(0, 100_000_000)]
        public decimal Price { get; set; }

        [Range(0, 1_000_000)]
        public int Stock { get; set; }

        // Foreign key: which shop this item belongs to
        [Required]
        public int ShopId { get; set; }
        public Shop Shop { get; set; } = null!;

        [MaxLength(100)]
        public string? Category { get; set; }
    }
}