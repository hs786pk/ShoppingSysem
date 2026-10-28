using System.ComponentModel.DataAnnotations;
using ShoppingSysem.Data;

namespace ShoppingSysem.Models
{
    public class Sale
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ItemId { get; set; }
        public Item Item { get; set; } = null!;

        public int QuantitySold { get; set; }

        public decimal TotalPrice { get; set; }

        public DateTime SaleDate { get; set; } = DateTime.Now;

        // Who made this sale (a Shopkeeper)
        [Required]
        public string ShopkeeperId { get; set; } = string.Empty;
        public ApplicationUser Shopkeeper { get; set; } = null!;

        // Which shop this sale happened at (denormalized for easy history queries)
        [Required]
        public int ShopId { get; set; }
        public Shop Shop { get; set; } = null!;
    }
}