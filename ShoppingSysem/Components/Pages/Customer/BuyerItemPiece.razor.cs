using Microsoft.AspNetCore.Components;
using ShoppingSysem.Models;
using ShoppingSysem.Services;

namespace ShoppingSysem.Components.Pages.Customer
{
    // What the card sends to the page when Buy is clicked.
    public sealed record BuyRequest(Item Item, int? VariantId);

    public partial class BuyerItemPiece
    {
        [Parameter, EditorRequired]
        public Item Item { get; set; } = default!;

        [Parameter]
        public string? Reason { get; set; }

        [Parameter]
        public int Index { get; set; }

        [Parameter]
        public bool Busy { get; set; }

        [Parameter]
        public EventCallback<Item> OnView { get; set; }

        [Parameter]
        public EventCallback<Item> OnSave { get; set; }

        [Parameter]
        public EventCallback<BuyRequest> OnBuy { get; set; }

        private int selectedVariantId;

        private bool HasVariants => Item.Variants.Count > 0;

        private ProductVariant? Selected =>
            Item.Variants.FirstOrDefault(v => v.Id == selectedVariantId);

        private decimal DisplayPrice =>
            Selected?.Price ?? (HasVariants ? Item.Variants.Min(v => v.Price) : Item.Price);

        private int DisplayStock =>
            Selected?.Stock ?? (HasVariants ? Item.Variants.Sum(v => v.Stock) : Item.Stock);

        // Products with variants need a chosen, in-stock option.
        private bool CanBuy =>
            !Busy && (HasVariants ? Selected is { Stock: > 0 } : Item.Stock > 0);

        private string? ImageUrl
        {
            get
            {
                var primary = Item.Images.FirstOrDefault(i => i.IsPrimary) ?? Item.Images.FirstOrDefault();
                return primary is null ? null : ProductImageService.UrlFor(primary.ThumbnailPath);
            }
        }

        // "Large / Red / Cotton"; falls back to the SKU if no option is set.
        private static string VariantLabel(ProductVariant v)
        {
            var parts = new[] { v.Size, v.Colour, v.Model, v.Material }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            var label = string.Join(" / ", parts);
            return label.Length > 0 ? label : v.SKU;
        }

        private Task View() => OnView.InvokeAsync(Item);

        private Task Save() => OnSave.InvokeAsync(Item);

        private Task Buy() => OnBuy.InvokeAsync(new BuyRequest(Item, Selected?.Id));
    }
}