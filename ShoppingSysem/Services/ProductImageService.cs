using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Data;
using ShoppingSysem.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using static System.Net.Mime.MediaTypeNames;
using Image = SixLabors.ImageSharp.Image;
namespace ShoppingSysem.Services;

public sealed record ImageResult(bool Ok, string Message, ProductImage? Image = null);

public interface IProductImageService
{
    Task<ImageResult> SaveAsync(int itemId, Stream content, CancellationToken ct = default);
    Task<bool> DeleteAsync(int imageId);
    Task<bool> SetPrimaryAsync(int itemId, int imageId);
}

/// <summary>
/// Validates, re-encodes and stores product images. It performs no authorization;
/// callers (ProductService) must verify shop ownership first.
/// </summary>
public sealed class ProductImageService : IProductImageService
{
    public const string RequestPath = "/product-images";

    private readonly ApplicationDbContext _db;
    private readonly ILogger<ProductImageService> _logger;
    private readonly string _root;

    public ProductImageService(
        ApplicationDbContext db,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<ProductImageService> logger)
    {
        _db = db;
        _logger = logger;
        _root = ResolveRoot(config, env);
    }

    // Shared with Program.cs so storage and serving always use the same folder.
    public static string ResolveRoot(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["ProductImages:RootPath"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "ProductImages")
            : Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(env.ContentRootPath, configured);
        return Path.GetFullPath(root);
    }

    public static string UrlFor(string relativePath) => $"{RequestPath}/{relativePath}";

    public async Task<ImageResult> SaveAsync(int itemId, Stream content, CancellationToken ct = default)
    {
        if (!await _db.Items.AnyAsync(i => i.Id == itemId, ct))
            return new(false, "Product not found.");

        var count = await _db.ProductImages.CountAsync(i => i.ItemId == itemId, ct);
        if (count >= ProductLimits.MaxImagesPerProduct)
            return new(false, $"A product can have at most {ProductLimits.MaxImagesPerProduct} images.");

        // Hard size cap while reading; the reported length is never trusted.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > ValidationLimits.MaxImageBytes)
                return new(false, $"Image must be {ValidationLimits.MaxImageBytes / 1024 / 1024} MB or smaller.");
            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
            return new(false, "The image file is empty.");

        // Format and size are read from the file contents, not the name or MIME type.
        buffer.Position = 0;
        ImageInfo info;
        try
        {
            info = await Image.IdentifyAsync(buffer, ct);
        }
        catch (Exception ex) when (IsImageError(ex))
        {
            return new(false, "That file is not a valid image.");
        }

        if (info.Metadata.DecodedImageFormat is not (JpegFormat or PngFormat or WebpFormat))
            return new(false, "Only JPEG, PNG and WebP images are allowed.");

        if (info.Width < ValidationLimits.MinImageDimension ||
            info.Height < ValidationLimits.MinImageDimension ||
            info.Width > ValidationLimits.MaxImageDimension ||
            info.Height > ValidationLimits.MaxImageDimension)
            return new(false,
                $"Image must be between {ValidationLimits.MinImageDimension} and " +
                $"{ValidationLimits.MaxImageDimension} pixels on each side.");

        var name = Guid.NewGuid().ToString("N");
        var relMain = $"{itemId}/{name}.webp";
        var relThumb = $"{itemId}/{name}_thumb.webp";
        var absMain = Resolve(relMain);
        var absThumb = Resolve(relThumb);
        int width, height;
        long size;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absMain)!);
            buffer.Position = 0;

            // Full decode and re-encode drops anything that is not pixel data.
            using var image = await Image.LoadAsync(buffer, ct);
            image.Mutate(x => x.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;

            if (Math.Max(image.Width, image.Height) > ValidationLimits.ImageMaxSide)
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(ValidationLimits.ImageMaxSide, ValidationLimits.ImageMaxSide)
                }));

            await image.SaveAsync(absMain, new WebpEncoder { Quality = 85 }, ct);
            width = image.Width;
            height = image.Height;
            size = new FileInfo(absMain).Length;

            using var thumb = image.Clone(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(ValidationLimits.ThumbnailMaxSide, ValidationLimits.ThumbnailMaxSide)
            }));
            await thumb.SaveAsync(absThumb, new WebpEncoder { Quality = 80 }, ct);
        }
        catch (Exception ex) when (IsImageError(ex) || ex is IOException)
        {
            DeleteFiles(relMain, relThumb);
            _logger.LogWarning(ex, "Image processing failed for item {ItemId}.", itemId);
            return new(false, "The image could not be processed.");
        }

        var entity = new ProductImage
        {
            ItemId = itemId,
            FilePath = relMain,
            ThumbnailPath = relThumb,
            ContentType = "image/webp",
            SizeBytes = size,
            Width = width,
            Height = height,
            IsPrimary = count == 0,
            SortOrder = count
        };

        _db.ProductImages.Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            DeleteFiles(relMain, relThumb);
            _logger.LogError(ex, "Failed to save image metadata for item {ItemId}.", itemId);
            return new(false, "Unable to save the image. Please try again.");
        }

        return new(true, "Image uploaded.", entity);
    }

    public async Task<bool> DeleteAsync(int imageId)
    {
        var image = await _db.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId);
        if (image is null)
            return false;

        _db.ProductImages.Remove(image);

        // Keep one primary image whenever images remain.
        if (image.IsPrimary)
        {
            var next = await _db.ProductImages
                .Where(i => i.ItemId == image.ItemId && i.Id != imageId)
                .OrderBy(i => i.SortOrder)
                .FirstOrDefaultAsync();
            if (next is not null)
                next.IsPrimary = true;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Failed to delete image {ImageId}.", imageId);
            return false;
        }

        DeleteFiles(image.FilePath, image.ThumbnailPath);
        return true;
    }

    public async Task<bool> SetPrimaryAsync(int itemId, int imageId)
    {
        var images = await _db.ProductImages.Where(i => i.ItemId == itemId).ToListAsync();
        if (!images.Any(i => i.Id == imageId))
            return false;

        foreach (var image in images)
            image.IsPrimary = image.Id == imageId;

        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Failed to set primary image {ImageId}.", imageId);
            return false;
        }
    }

    private static bool IsImageError(Exception ex) =>
        ex is ImageFormatException ||
        ex is UnknownImageFormatException ||
        ex is InvalidImageContentException ||
        ex is NotSupportedException;

    // Every path is checked to stay inside the image root.
    private string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid image path.");
        return full;
    }

    private void DeleteFiles(params string[] relativePaths)
    {
        foreach (var path in relativePaths)
        {
            try
            {
                File.Delete(Resolve(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Could not delete image file {Path}.", path);
            }
        }
    }
}