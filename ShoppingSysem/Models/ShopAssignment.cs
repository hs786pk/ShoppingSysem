using System.ComponentModel.DataAnnotations;
using ShoppingSysem.Data;

namespace ShoppingSysem.Models
{
    public class ShopAssignment
    {
        [Key]
        public int Id { get; set; }

        // Foreign key to the Shopkeeper (an Identity user)
        [Required]
        public string ShopkeeperId { get; set; } = string.Empty;
        public ApplicationUser Shopkeeper { get; set; } = null!;

        // Foreign key to the Shop
        [Required]
        public int ShopId { get; set; }
        public Shop Shop { get; set; } = null!;
    }
}