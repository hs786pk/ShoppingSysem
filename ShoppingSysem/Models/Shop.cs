using System.ComponentModel.DataAnnotations;
using ShoppingSysem.Data;

namespace ShoppingSysem.Models
{
    public class Shop
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(ValidationLimits.ShopNameMaxLength)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(ValidationLimits.ShopLocationMaxLength)]
        public string Location { get; set; } = string.Empty;

        public List<Item> Items { get; set; } = new();
        public List<ShopAssignment> ShopAssignments { get; set; } = new();
    }
}