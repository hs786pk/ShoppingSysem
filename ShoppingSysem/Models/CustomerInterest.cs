namespace ShoppingSysem.Models;

public enum InteractionType
{
    View = 1,       // buyer opened / clicked a product
    Wishlist = 2,   // buyer saved a product
    Purchase = 3    // buyer bought a product
}

/// <summary>
/// One row per buyer action. The recommendation service reads these
/// to learn which categories / items each buyer is interested in.
/// </summary>
public class CustomerInterest
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;   // AspNetUsers.Id

    public int ItemId { get; set; }

    public string Category { get; set; } = "General";    // copied from Item at record time

    public InteractionType Type { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}