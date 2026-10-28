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

        public decimal Price { get; set; }

        public int Stock { get; set; }

        // Foreign key: which shop this item belongs to
        [Required]
        public int ShopId { get; set; }
        public Shop Shop { get; set; } = null!;
    }
}