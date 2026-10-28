using System.ComponentModel.DataAnnotations;

namespace ShoppingSysem.Models
{
    public class Shop
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Location { get; set; } = string.Empty;

        public List<Item> Items { get; set; } = new();
        public List<ShopAssignment> ShopAssignments { get; set; } = new();
    }
}