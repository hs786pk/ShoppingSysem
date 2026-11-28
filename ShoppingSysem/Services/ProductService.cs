using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;

namespace ShoppingSysem.Services;

public sealed record ProductResult(bool Ok, string Message, Item? Item = null);

public sealed class AttributeInput
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class VariantInput
{
    public string SKU { get; set; } = "";
    public string? Size { get; set; }
    public string? Colour { get; set; }
    public string? Model { get; set; }
    public string? Material { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ProductInput
{
    public string Name { get; set; } = "";
    public string SKU { get; set; } = "";
    public string? Brand { get; set; }
    public string? ShortDescription { get; set; }
    public string? FullDescription { get; set; }
    public string? Category { get; set; }
    public string? Subcategory { get; set; }
    public ProductCondition Condition { get; set; } = ProductCondition.New;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public int? WeightGrams { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public List<AttributeInput> Attributes { get; set; } = new();
    public List<VariantInput> Variants { get; set; } = new();
}

public interface IProductService
{
    Task<List<Item>> GetMyProductsAsync(string userId);
    Task<Item?> GetForEditAsync(string userId, int itemId);
    Task<ProductResult> CreateAsync(string userId, int shopId, ProductInput input);
    Task<ProductResult> UpdateAsync(string userId, int itemId, ProductInput input);
    Task<ProductResult> SubmitForApprovalAsync(string userId, int itemId);
    Task<ProductResult> DeactivateAsync(string userId, int itemId);
    Task<ProductResult> ReactivateAsync(string userId, int itemId);
    Task<ProductResult> DeleteAsync(string userId, int itemId);
    Task<ProductResult> AddImageAsync(string userId, int itemId, Stream content);
    Task<ProductResult> DeleteImageAsync(string userId, int imageId);
    Task<ProductResult> SetPrimaryImageAsync(string userId, int itemId, int imageId);
    Task<List<Item>> GetPendingApprovalAsync(string adminId);
    Task<ProductResult> ApproveAsync(string adminId, int itemId);
    Task<ProductResult> RejectAsync(string adminId, int itemId, string reason);
}

/// <summary>
/// The only place pages change products. Identity comes from the authenticated
/// server-side principal; the userId a component passes is only cross-checked.
/// </summary>
public sealed class ProductService : IProductService
{
    private const int FullDescriptionMaxLength = 5000;
    private const int MaxAttributes = 50;
    private const int MaxVariants = 100;

    private readonly ApplicationDbContext _db;
    private readonly AuthenticationStateProvider _auth;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IShopAccessService _shopAccess;
    private readonly IProductImageService _images;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        ApplicationDbContext db,
        AuthenticationStateProvider auth,
        UserManager<ApplicationUser> users,
        IShopAccessService shopAccess,
        IProductImageService images,
        ILogger<ProductService> logger)
    {
        _db = db;
        _auth = auth;
        _users = users;
        _shopAccess = shopAccess;
        _images = images;
        _logger = logger;
    }

    // ---- Queries ---------------------------------------------------------

    public async Task<List<Item>> GetMyProductsAsync(string userId)
    {
        var user = await CurrentAsync(userId);
        if (user is null || !await _users.IsInRoleAsync(user, AppRoles.Shopkeeper))
            return new();

        var shopIds = await _shopAccess.GetAssignedShopIdsAsync(user.Id);
        if (shopIds.Count == 0)
            return new();

        return await _db.Items
            .AsNoTracking()
            .Where(i => shopIds.Contains(i.ShopId))
            .Include(i => i.Shop)
            .Include(i => i.Images.Where(p => p.IsPrimary))
            .OrderByDescending(i => i.UpdatedAt)
            .Take(500)
            .ToListAsync();
    }

    public async Task<Item?> GetForEditAsync(string userId, int itemId)
    {
        var user = await CurrentAsync(userId);
        if (user is null)
            return null;

        // Untracked copy: edits in the UI never leak into this DbContext.
        var item = await _db.Items
            .AsNoTracking()
            .Include(i => i.Attributes)
            .Include(i => i.Variants)
            .Include(i => i.Images)
            .FirstOrDefaultAsync(i => i.Id == itemId);

        return item is not null && await CanManageAsync(user, item.ShopId) ? item : null;
    }

    public async Task<List<Item>> GetPendingApprovalAsync(string adminId)
    {
        if (await AdminAsync(adminId) is null)
            return new();

        return await _db.Items
            .AsNoTracking()
            .Where(i => i.Status == ProductStatus.PendingApproval)
            .Include(i => i.Shop)
            .Include(i => i.Images.Where(p => p.IsPrimary))
            .OrderBy(i => i.UpdatedAt)
            .Take(200)
            .ToListAsync();
    }

    // ---- Create / update / delete ---------------------------------------

    public async Task<ProductResult> CreateAsync(string userId, int shopId, ProductInput input)
    {
        var user = await CurrentAsync(userId);
        if (user is null)
            return Fail("You must be signed in.");

        if (shopId <= 0 ||
            !await _db.Shops.AnyAsync(s => s.Id == shopId) ||
            !await CanManageAsync(user, shopId))
        {
            _logger.LogWarning("Product create denied for user {UserId} in shop {ShopId}.", user.Id, shopId);
            return Fail("You cannot add products to that shop.");
        }

        Normalize(input);
        if (Validate(input) is { } error)
            return Fail(error);
        if (await DuplicateAsync(shopId, 0, input) is { } duplicate)
            return Fail(duplicate);

        var item = new Item { ShopId = shopId, Status = ProductStatus.Draft };
        Apply(item, input);
        _db.Items.Add(item);

        return await TrySaveAsync(item, "Product created as a draft.", "create the product");
    }

    public async Task<ProductResult> UpdateAsync(string userId, int itemId, ProductInput input)
    {
        var (item, denied) = await LoadAsync(userId, itemId, includeChildren: true);
        if (item is null)
            return denied!;

        if (item.Status == ProductStatus.Archived)
            return Fail("Reactivate the product before editing it.");

        Normalize(input);
        if (Validate(input) is { } error)
            return Fail(error);
        if (await DuplicateAsync(item.ShopId, item.Id, input) is { } duplicate)
            return Fail(duplicate);

        // Any variant the shopkeeper dropped from the form that still has order
        // or sales history must not be deleted (it would fail on the Orders/Sales
        // foreign key). Keep it, but force it inactive instead.
        await PreserveVariantsWithHistoryAsync(item, input);

        Apply(item, input);

        // A rejected product goes back to draft so it can be fixed and resubmitted.
        if (item.Status == ProductStatus.Rejected)
        {
            item.Status = ProductStatus.Draft;
            item.RejectionReason = null;
        }

        return await TrySaveAsync(item, "Product saved.", "save the product");
    }

    // Re-adds (as inactive) any existing variant that the input no longer
    // lists, when that variant has order or sales history, so Apply() keeps
    // it instead of deleting it.
    private async Task PreserveVariantsWithHistoryAsync(Item item, ProductInput input)
    {
        var removed = item.Variants
            .Where(v => !input.Variants.Any(x => x.SKU.Equals(v.SKU, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (removed.Count == 0)
            return;

        var removedIds = removed.Select(v => v.Id).ToList();

        var idsWithHistory = await _db.Orders
            .Where(o => o.VariantId != null && removedIds.Contains(o.VariantId!.Value))
            .Select(o => o.VariantId!.Value)
            .Union(_db.Sales
                .Where(s => s.VariantId != null && removedIds.Contains(s.VariantId!.Value))
                .Select(s => s.VariantId!.Value))
            .ToListAsync();

        if (idsWithHistory.Count == 0)
            return;

        var protect = new HashSet<int>(idsWithHistory);

        foreach (var v in removed.Where(v => protect.Contains(v.Id)))
        {
            input.Variants.Add(new VariantInput
            {
                SKU = v.SKU,
                Size = v.Size,
                Colour = v.Colour,
                Model = v.Model,
                Material = v.Material,
                Price = v.Price,
                Stock = v.Stock,
                IsActive = false
            });
        }
    }

    public async Task<ProductResult> DeleteAsync(string userId, int itemId)
    {
        var (item, denied) = await LoadAsync(userId, itemId);
        if (item is null)
            return denied!;

        // Orders and sales keep pointing at the product, so history is never lost.
        if (await _db.Orders.AnyAsync(o => o.ItemId == item.Id) ||
            await _db.Sales.AnyAsync(s => s.ItemId == item.Id))
        {
            if (item.Status == ProductStatus.Archived)
                return new(true, "This product has order or sales history, so it stays archived.");

            item.Status = ProductStatus.Archived;
            return await TrySaveAsync(item,
                "This product has order or sales history, so it was archived instead of deleted.",
                "archive the product");
        }

        var imageIds = await _db.ProductImages
            .Where(i => i.ItemId == item.Id)
            .Select(i => i.Id)
            .ToListAsync();

        foreach (var imageId in imageIds)
            await _images.DeleteAsync(imageId);

        // Variants and attributes are removed by the cascade.
        _db.Items.Remove(item);
        return await TrySaveAsync(item, "Product deleted.", "delete the product");
    }

    // ---- Status changes (shopkeeper) ------------------------------------

    public Task<ProductResult> SubmitForApprovalAsync(string userId, int itemId) =>
        ChangeStatusAsync(userId, itemId,
            new[] { ProductStatus.Draft, ProductStatus.Rejected },
            ProductStatus.PendingApproval,
            "Submitted for approval.",
            async item =>
            {
                if (string.IsNullOrWhiteSpace(item.ShortDescription))
                    return "Add a short description before submitting.";
                if (!await _db.ProductImages.AnyAsync(i => i.ItemId == item.Id))
                    return "Add at least one image before submitting.";
                return null;
            });

    public Task<ProductResult> DeactivateAsync(string userId, int itemId) =>
        ChangeStatusAsync(userId, itemId,
            new[] { ProductStatus.Active }, ProductStatus.Archived,
            "Product deactivated.");

    // Reactivation is reviewed again, so an admin's archive cannot be bypassed.
    public Task<ProductResult> ReactivateAsync(string userId, int itemId) =>
        ChangeStatusAsync(userId, itemId,
            new[] { ProductStatus.Archived }, ProductStatus.PendingApproval,
            "Product resubmitted for approval.");

    // ---- Images ----------------------------------------------------------

    public async Task<ProductResult> AddImageAsync(string userId, int itemId, Stream content)
    {
        var (item, denied) = await LoadAsync(userId, itemId);
        if (item is null)
            return denied!;

        var result = await _images.SaveAsync(item.Id, content);
        return new(result.Ok, result.Message);
    }

    public async Task<ProductResult> DeleteImageAsync(string userId, int imageId)
    {
        var itemId = await _db.ProductImages
            .Where(i => i.Id == imageId)
            .Select(i => (int?)i.ItemId)
            .FirstOrDefaultAsync();

        if (itemId is null)
            return Fail("Image not found.");

        var (item, denied) = await LoadAsync(userId, itemId.Value);
        if (item is null)
            return denied!;

        return await _images.DeleteAsync(imageId)
            ? new(true, "Image removed.")
            : Fail("Unable to remove the image.");
    }

    public async Task<ProductResult> SetPrimaryImageAsync(string userId, int itemId, int imageId)
    {
        var (item, denied) = await LoadAsync(userId, itemId);
        if (item is null)
            return denied!;

        return await _images.SetPrimaryAsync(item.Id, imageId)
            ? new(true, "Primary image updated.")
            : Fail("Unable to set the primary image.");
    }

    // ---- Admin approval --------------------------------------------------

    public Task<ProductResult> ApproveAsync(string adminId, int itemId) =>
        ReviewAsync(adminId, itemId, ProductStatus.Active, null);

    public Task<ProductResult> RejectAsync(string adminId, int itemId, string reason)
    {
        reason = reason?.Trim() ?? "";
        if (reason.Length == 0)
            return Task.FromResult(Fail("A rejection reason is required."));
        if (reason.Length > ProductLimits.RejectionReasonMaxLength)
            return Task.FromResult(Fail(
                $"The reason cannot exceed {ProductLimits.RejectionReasonMaxLength} characters."));

        return ReviewAsync(adminId, itemId, ProductStatus.Rejected, reason);
    }

    private async Task<ProductResult> ReviewAsync(
        string adminId, int itemId, ProductStatus to, string? reason)
    {
        if (await AdminAsync(adminId) is null)
            return Fail("Only administrators can review products.");

        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == itemId);
        if (item is null)
            return Fail("Product not found.");
        if (item.Status != ProductStatus.PendingApproval)
            return Fail("Only products pending approval can be reviewed.");

        item.Status = to;
        item.RejectionReason = reason;
        return await TrySaveAsync(item,
            to == ProductStatus.Active ? "Product approved." : "Product rejected.",
            "review the product");
    }

    // ---- Helpers ---------------------------------------------------------

    private static ProductResult Fail(string message) => new(false, message);

    private async Task<ApplicationUser?> CurrentAsync(string userId)
    {
        var state = await _auth.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated != true)
            return null;

        var user = await _users.GetUserAsync(state.User);
        if (user is null)
            return null;

        if (user.Id != userId)
        {
            _logger.LogWarning("Product access denied: caller {CallerId} acted as {RequestedId}.", user.Id, userId);
            return null;
        }

        return user;
    }

    private async Task<ApplicationUser?> AdminAsync(string userId)
    {
        var user = await CurrentAsync(userId);
        return user is not null && await _users.IsInRoleAsync(user, AppRoles.Admin) ? user : null;
    }

    private async Task<bool> CanManageAsync(ApplicationUser user, int shopId) =>
        await _users.IsInRoleAsync(user, AppRoles.Admin) ||
        (await _users.IsInRoleAsync(user, AppRoles.Shopkeeper) &&
         await _shopAccess.IsShopkeeperAssignedAsync(user.Id, shopId));

    // Missing and foreign products give the same answer so IDs cannot be probed.
    private async Task<(Item? Item, ProductResult? Denied)> LoadAsync(
        string userId, int itemId, bool includeChildren = false)
    {
        var user = await CurrentAsync(userId);
        if (user is null)
            return (null, Fail("You must be signed in."));

        IQueryable<Item> query = _db.Items;
        if (includeChildren)
            query = query.Include(i => i.Attributes).Include(i => i.Variants);

        var item = itemId > 0 ? await query.FirstOrDefaultAsync(i => i.Id == itemId) : null;
        if (item is null)
            return (null, Fail("Product not found."));

        if (!await CanManageAsync(user, item.ShopId))
        {
            _logger.LogWarning("Product {ItemId} access denied for user {UserId}.", itemId, user.Id);
            return (null, Fail("Product not found."));
        }

        return (item, null);
    }

    private async Task<ProductResult> ChangeStatusAsync(
        string userId, int itemId, ProductStatus[] allowedFrom, ProductStatus to,
        string done, Func<Item, Task<string?>>? check = null)
    {
        var (item, denied) = await LoadAsync(userId, itemId);
        if (item is null)
            return denied!;

        if (!allowedFrom.Contains(item.Status))
            return Fail($"A {item.Status} product cannot be moved to {to}.");

        if (check is not null && await check(item) is { } problem)
            return Fail(problem);

        item.Status = to;
        item.RejectionReason = null;
        return await TrySaveAsync(item, done, "update the product");
    }

    private async Task<ProductResult> TrySaveAsync(Item item, string ok, string action)
    {
        try
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Product {ItemId}: {Action} succeeded.", item.Id, action);
            return new(true, ok, item);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Failed to {Action} for product {ItemId}.", action, item.Id);
            return Fail($"Unable to {action}. Check that the title and SKUs are unique, then try again.");
        }
    }

    private async Task<string?> DuplicateAsync(int shopId, int exceptId, ProductInput p)
    {
        if (await _db.Items.AnyAsync(i => i.ShopId == shopId && i.Id != exceptId && i.Name == p.Name))
            return "A product with this title already exists in the shop.";
        if (await _db.Items.AnyAsync(i => i.ShopId == shopId && i.Id != exceptId && i.SKU == p.SKU))
            return "This SKU is already used by another product in the shop.";
        return null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Trims text and drops completely blank specification/variant rows.
    private static void Normalize(ProductInput p)
    {
        p.Name = p.Name?.Trim() ?? "";
        p.SKU = p.SKU?.Trim() ?? "";
        p.Brand = Clean(p.Brand);
        p.ShortDescription = Clean(p.ShortDescription);
        p.FullDescription = Clean(p.FullDescription);
        p.Category = Clean(p.Category);
        p.Subcategory = Clean(p.Subcategory);

        p.Attributes = (p.Attributes ?? new())
            .Select(a => new AttributeInput { Name = a.Name?.Trim() ?? "", Value = a.Value?.Trim() ?? "" })
            .Where(a => a.Name.Length > 0 || a.Value.Length > 0)
            .ToList();

        p.Variants = (p.Variants ?? new())
            .Select(v => new VariantInput
            {
                SKU = v.SKU?.Trim() ?? "",
                Size = Clean(v.Size),
                Colour = Clean(v.Colour),
                Model = Clean(v.Model),
                Material = Clean(v.Material),
                Price = v.Price,
                Stock = v.Stock,
                IsActive = v.IsActive
            })
            .Where(v => v.SKU.Length > 0 || v.Size is not null || v.Colour is not null ||
                        v.Model is not null || v.Material is not null)
            .ToList();
    }

    private static bool BadPrice(decimal price) =>
        price < 0 || price > ValidationLimits.MaxPrice || decimal.Round(price, 2) != price;

    private static bool BadDimension(decimal? value) =>
        value is { } v && (v < 0 || v > ProductLimits.MaxDimensionCm || decimal.Round(v, 2) != v);

    private static string? Validate(ProductInput p)
    {
        if (p.Name.Length == 0 || p.Name.Length > ValidationLimits.ItemNameMaxLength)
            return $"Title is required and cannot exceed {ValidationLimits.ItemNameMaxLength} characters.";
        if (p.SKU.Length == 0 || p.SKU.Length > ValidationLimits.SkuMaxLength)
            return $"SKU is required and cannot exceed {ValidationLimits.SkuMaxLength} characters.";
        if (p.Brand is { Length: > ValidationLimits.BrandMaxLength })
            return $"Brand cannot exceed {ValidationLimits.BrandMaxLength} characters.";
        if (p.ShortDescription is { Length: > ValidationLimits.ShortDescriptionMaxLength })
            return $"Short description cannot exceed {ValidationLimits.ShortDescriptionMaxLength} characters.";
        if (p.FullDescription is { Length: > FullDescriptionMaxLength })
            return $"Full description cannot exceed {FullDescriptionMaxLength} characters.";
        if (p.Category is { Length: > ValidationLimits.CategoryMaxLength } ||
            p.Subcategory is { Length: > ValidationLimits.CategoryMaxLength })
            return $"Category and subcategory cannot exceed {ValidationLimits.CategoryMaxLength} characters.";
        if (!Enum.IsDefined(p.Condition))
            return "Select a valid condition.";
        if (BadPrice(p.Price))
            return $"Price must be between 0 and {ValidationLimits.MaxPrice:N0} with at most 2 decimal places.";
        if (p.Stock < 0 || p.Stock > ValidationLimits.MaxStock)
            return $"Stock must be between 0 and {ValidationLimits.MaxStock:N0}.";
        if (p.WeightGrams is { } w && (w < 0 || w > ProductLimits.MaxWeightGrams))
            return $"Weight must be between 0 and {ProductLimits.MaxWeightGrams:N0} grams.";
        if (BadDimension(p.LengthCm) || BadDimension(p.WidthCm) || BadDimension(p.HeightCm))
            return $"Dimensions must be between 0 and {ProductLimits.MaxDimensionCm:N0} cm with at most 2 decimal places.";

        if (p.Attributes.Count > MaxAttributes)
            return $"A product can have at most {MaxAttributes} specifications.";
        foreach (var a in p.Attributes)
        {
            if (a.Name.Length == 0 || a.Value.Length == 0)
                return "Each specification needs both a name and a value.";
            if (a.Name.Length > ProductLimits.AttributeNameMaxLength ||
                a.Value.Length > ProductLimits.AttributeValueMaxLength)
                return "A specification name or value is too long.";
        }
        if (p.Attributes.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            return "Specification names must be unique.";

        if (p.Variants.Count > MaxVariants)
            return $"A product can have at most {MaxVariants} variants.";
        foreach (var v in p.Variants)
        {
            if (v.SKU.Length == 0 || v.SKU.Length > ValidationLimits.SkuMaxLength)
                return $"Each variant needs a SKU of up to {ValidationLimits.SkuMaxLength} characters.";
            if (v.Size is null && v.Colour is null && v.Model is null && v.Material is null)
                return "Each variant needs at least one of size, colour, model or material.";
            if (new[] { v.Size, v.Colour, v.Model, v.Material }
                .Any(o => o is { Length: > ProductLimits.VariantOptionMaxLength }))
                return $"Variant options cannot exceed {ProductLimits.VariantOptionMaxLength} characters.";
            if (BadPrice(v.Price))
                return "A variant price is invalid (0 or more, at most 2 decimal places).";
            if (v.Stock < 0 || v.Stock > ValidationLimits.MaxStock)
                return $"Variant stock must be between 0 and {ValidationLimits.MaxStock:N0}.";
        }
        if (p.Variants.GroupBy(v => v.SKU, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            return "Variant SKUs must be unique within the product.";

        return null;
    }

    // Copies validated input onto the item. Variants are matched by SKU and
    // attributes by name, so existing rows keep their IDs.
    private static void Apply(Item item, ProductInput p)
    {
        item.Name = p.Name;
        item.SKU = p.SKU;
        item.Brand = p.Brand;
        item.ShortDescription = p.ShortDescription;
        item.FullDescription = p.FullDescription;
        item.Category = p.Category;
        item.Subcategory = p.Subcategory;
        item.Condition = p.Condition;
        item.Price = p.Price;
        item.Stock = p.Stock;
        item.WeightGrams = p.WeightGrams;
        item.LengthCm = p.LengthCm;
        item.WidthCm = p.WidthCm;
        item.HeightCm = p.HeightCm;
        item.UpdatedAt = DateTime.UtcNow;

        foreach (var old in item.Attributes
                     .Where(a => !p.Attributes.Any(x => x.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase)))
                     .ToList())
            item.Attributes.Remove(old);

        foreach (var a in p.Attributes)
        {
            var existing = item.Attributes.FirstOrDefault(
                x => x.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                item.Attributes.Add(new ProductAttribute { Name = a.Name, Value = a.Value });
            else
                existing.Value = a.Value;
        }

        foreach (var old in item.Variants
                     .Where(v => !p.Variants.Any(x => x.SKU.Equals(v.SKU, StringComparison.OrdinalIgnoreCase)))
                     .ToList())
            item.Variants.Remove(old);

        foreach (var v in p.Variants)
        {
            var existing = item.Variants.FirstOrDefault(
                x => x.SKU.Equals(v.SKU, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new ProductVariant { SKU = v.SKU };
                item.Variants.Add(existing);
            }

            existing.Size = v.Size;
            existing.Colour = v.Colour;
            existing.Model = v.Model;
            existing.Material = v.Material;
            existing.Price = v.Price;
            existing.Stock = v.Stock;
            existing.IsActive = v.IsActive;
        }
    }
}