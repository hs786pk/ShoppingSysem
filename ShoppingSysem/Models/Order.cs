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

        [Required]
        public int ShopId { get; set; }
        public Shop Shop { get; set; } = null!;

        public int Quantity { get; set; }

        // Snapshot. Later price edits must not change this order.
        public decimal UnitPrice { get; set; }

        public decimal TotalPrice { get; set; }

        public DateTime OrderedAt { get; set; } = DateTime.UtcNow;

        public OrderStatus Status { get; set; } = OrderStatus.Placed;
    }
}