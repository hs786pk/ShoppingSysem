using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;
using ShoppingSysem.Services;

namespace ShoppingSysem.Components.Pages
{
    public partial class ProductEditor
    {
        [Parameter] public int? Id { get; set; }

        [Inject] private AuthenticationStateProvider AuthProvider { get; set; } = default!;
        [Inject] private IProductService Products { get; set; } = default!;
        [Inject] private IShopAccessService ShopAccess { get; set; } = default!;
        [Inject] private ApplicationDbContext Db { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;

        private string userId = string.Empty;
        private int lastKey = -1;
        private int shopId;
        private bool busy;
        private bool notFound;
        private string? message;
        private bool messageIsError;

        private Item? current;
        private ProductInput input = new();
        private List<ProductImage> images = new();
        private List<Shop> shops = new();

        private bool CanSubmit =>
            current is not null && current.Status is ProductStatus.Draft or ProductStatus.Rejected;

        private string StatusLabel => current?.Status == ProductStatus.PendingApproval
            ? "Pending approval"
            : current?.Status.ToString() ?? "";

        // Reuses the existing order status colours.
        private string StatusClass => current?.Status switch
        {
            ProductStatus.PendingApproval => "status-placed",
            ProductStatus.Active => "status-fulfilled",
            ProductStatus.Rejected or ProductStatus.Archived => "status-cancelled",
            _ => ""
        };

        protected override async Task OnParametersSetAsync()
        {
            var key = Id ?? 0;
            if (key == lastKey) return;
            lastKey = key;

            if (string.IsNullOrEmpty(userId))
            {
                var state = await AuthProvider.GetAuthenticationStateAsync();
                userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            }

            message = null;
            notFound = false;

            if (Id is null)
            {
                current = null;
                input = new ProductInput();
                images = new();
                await LoadShops();
                return;
            }

            await Reload(resetForm: true);
        }

        private async Task LoadShops()
        {
            var ids = await ShopAccess.GetAssignedShopIdsAsync(userId);
            shops = ids.Count == 0
                ? new()
                : await Db.Shops.AsNoTracking()
                    .Where(s => ids.Contains(s.Id))
                    .OrderBy(s => s.Name)
                    .ToListAsync();

            if (shops.Count == 1)
                shopId = shops[0].Id;
        }

        // Ownership is checked by the service; a foreign product returns null.
        private async Task Reload(bool resetForm)
        {
            var item = await Products.GetForEditAsync(userId, Id!.Value);
            if (item is null)
            {
                notFound = true;
                current = null;
                return;
            }

            current = item;
            images = item.Images.OrderBy(i => i.SortOrder).ToList();
            if (resetForm)
                input = ToInput(item);
        }

        private async Task Save()
        {
            if (busy) return;
            busy = true;
            try
            {
                if (current is null)
                {
                    var created = await Products.CreateAsync(userId, shopId, input);
                    if (created.Ok && created.Item is not null)
                    {
                        Nav.NavigateTo($"/shopkeeper/products/{created.Item.Id}");
                        return;
                    }

                    Show(created);
                    return;
                }

                var result = await Products.UpdateAsync(userId, current.Id, input);
                if (result.Ok)
                    await Reload(resetForm: true);

                Show(result);
            }
            catch (Exception)
            {
                Fail("We could not save the product. Please try again.");
            }
            finally
            {
                busy = false;
            }
        }

        private async Task SubmitForApproval()
        {
            if (busy || current is null) return;
            busy = true;
            try
            {
                var result = await Products.SubmitForApprovalAsync(userId, current.Id);
                // Refresh status only, so unsaved form edits are not discarded.
                await Reload(resetForm: false);
                Show(result);
            }
            catch (Exception)
            {
                Fail("We could not submit the product. Please try again.");
            }
            finally
            {
                busy = false;
            }
        }

        private async Task Upload(InputFileChangeEventArgs e)
        {
            if (busy || current is null) return;
            busy = true;

            var uploaded = 0;
            string? error = null;
            try
            {
                foreach (var file in e.GetMultipleFiles(ProductLimits.MaxImagesPerProduct))
                {
                    if (file.Size > ValidationLimits.MaxImageBytes)
                    {
                        error = $"{file.Name} is larger than {ValidationLimits.MaxImageBytes / 1024 / 1024} MB.";
                        continue;
                    }

                    await using var stream = file.OpenReadStream(ValidationLimits.MaxImageBytes);
                    var result = await Products.AddImageAsync(userId, current.Id, stream);
                    if (result.Ok) uploaded++;
                    else error = result.Message;
                }
            }
            catch (IOException)
            {
                error = "The upload was interrupted or too large. Please try again.";
            }
            catch (Exception)
            {
                error = "We could not upload the image. Please try again.";
            }
            finally
            {
                busy = false;
            }

            await Reload(resetForm: false);

            if (error is not null) Fail(uploaded > 0 ? $"{uploaded} uploaded. {error}" : error);
            else Show(new ProductResult(true, $"{uploaded} image(s) uploaded."));
        }

        private Task MakePrimary(int imageId) =>
            ImageAction(() => Products.SetPrimaryImageAsync(userId, current!.Id, imageId));

        private Task DeleteImage(int imageId) =>
            ImageAction(() => Products.DeleteImageAsync(userId, imageId));

        private async Task ImageAction(Func<Task<ProductResult>> action)
        {
            if (busy || current is null) return;
            busy = true;
            try
            {
                var result = await action();
                await Reload(resetForm: false);
                Show(result);
            }
            catch (Exception)
            {
                Fail("We could not update the image. Please try again.");
            }
            finally
            {
                busy = false;
            }
        }

        private void AddAttribute() => input.Attributes.Add(new AttributeInput());
        private void RemoveAttribute(AttributeInput a) => input.Attributes.Remove(a);
        private void AddVariant() => input.Variants.Add(new VariantInput());
        private void RemoveVariant(VariantInput v) => input.Variants.Remove(v);

        private static string ImageUrl(ProductImage i) => ProductImageService.UrlFor(i.ThumbnailPath);

        private void Show(ProductResult result)
        {
            message = result.Message;
            messageIsError = !result.Ok;
        }

        private void Fail(string text)
        {
            message = text;
            messageIsError = true;
        }

        private static ProductInput ToInput(Item item) => new()
        {
            Name = item.Name,
            SKU = item.SKU,
            Brand = item.Brand,
            ShortDescription = item.ShortDescription,
            FullDescription = item.FullDescription,
            Category = item.Category,
            Subcategory = item.Subcategory,
            Condition = item.Condition,
            Price = item.Price,
            Stock = item.Stock,
            WeightGrams = item.WeightGrams,
            LengthCm = item.LengthCm,
            WidthCm = item.WidthCm,
            HeightCm = item.HeightCm,
            Attributes = item.Attributes
                .OrderBy(a => a.Name)
                .Select(a => new AttributeInput { Name = a.Name, Value = a.Value })
                .ToList(),
            Variants = item.Variants
                .OrderBy(v => v.SKU)
                .Select(v => new VariantInput
                {
                    SKU = v.SKU,
                    Size = v.Size,
                    Colour = v.Colour,
                    Model = v.Model,
                    Material = v.Material,
                    Price = v.Price,
                    Stock = v.Stock,
                    IsActive = v.IsActive
                })
                .ToList()
        };
    }
}