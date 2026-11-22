using Microsoft.AspNetCore.Components;
using ShoppingSysem.Models;

namespace ShoppingSysem.Components.Pages.Customer
{
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
        public EventCallback<Item> OnBuy { get; set; }

        private Task View() => OnView.InvokeAsync(Item);

        private Task Save() => OnSave.InvokeAsync(Item);

        private Task Buy() => OnBuy.InvokeAsync(Item);
    }
}
