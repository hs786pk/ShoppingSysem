using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Models;

namespace ShoppingSysem.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Shop> Shops { get; set; }
        public DbSet<Item> Items { get; set; }
        public DbSet<Sale> Sales { get; set; }
        public DbSet<ShopAssignment> ShopAssignments { get; set; }
        public DbSet<CustomerInterest> CustomerInterests => Set<CustomerInterest>();
        public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
        public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
        public DbSet<ProductImage> ProductImages => Set<ProductImage>();

        public DbSet<Order> Orders { get; set; }

        // Keeps Item.UpdatedAt current without every page remembering to set it.
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            TouchItems();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            TouchItems();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void TouchItems()
        {
            foreach (var entry in ChangeTracker.Entries<Item>()
                         .Where(e => e.State == EntityState.Modified))
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Item>(e =>
            {
                e.Property(i => i.Name).HasMaxLength(ValidationLimits.ItemNameMaxLength);
                e.Property(i => i.Price).HasPrecision(18, 2);
                e.Property(i => i.Category).HasMaxLength(ValidationLimits.CategoryMaxLength);
                e.Property(i => i.Subcategory).HasMaxLength(ValidationLimits.CategoryMaxLength);
                e.Property(i => i.SKU).HasMaxLength(ValidationLimits.SkuMaxLength).IsRequired();
                e.Property(i => i.Brand).HasMaxLength(ValidationLimits.BrandMaxLength);
                e.Property(i => i.ShortDescription).HasMaxLength(ValidationLimits.ShortDescriptionMaxLength);
                e.Property(i => i.RejectionReason).HasMaxLength(ProductLimits.RejectionReasonMaxLength);
                e.Property(i => i.LengthCm).HasPrecision(10, 2);
                e.Property(i => i.WidthCm).HasPrecision(10, 2);
                e.Property(i => i.HeightCm).HasPrecision(10, 2);
                // Existing rows become Active when the column is added.
                e.Property(i => i.Status).HasDefaultValue(ProductStatus.Active);
                e.Property(i => i.Condition).HasDefaultValue(ProductCondition.New);
                e.HasOne(i => i.Shop)
                    .WithMany(s => s.Items)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(i => i.ShopId);
                e.HasIndex(i => i.Category);
                e.HasIndex(i => i.Subcategory);
                e.HasIndex(i => i.Status);
                // SKU is unique within a shop; different shops may reuse supplier SKUs.
                e.HasIndex(i => new { i.ShopId, i.SKU }).IsUnique();
                e.HasIndex(i => i.Name);
                // One item name per shop (application layer also enforces this).
                e.HasIndex(i => new { i.ShopId, i.Name }).IsUnique();
            });

            // Child rows follow the product. Products with orders/sales are still
            // protected because Order/Sale -> Item is Restrict.
            builder.Entity<ProductAttribute>(e =>
            {
                e.Property(a => a.Name).HasMaxLength(ProductLimits.AttributeNameMaxLength);
                e.Property(a => a.Value).HasMaxLength(ProductLimits.AttributeValueMaxLength);
                e.HasOne(a => a.Item)
                    .WithMany(i => i.Attributes)
                    .HasForeignKey(a => a.ItemId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(a => new { a.ItemId, a.Name }).IsUnique();
            });

            builder.Entity<ProductVariant>(e =>
            {
                e.Property(v => v.SKU).HasMaxLength(ValidationLimits.SkuMaxLength);
                e.Property(v => v.Price).HasPrecision(18, 2);
                e.Property(v => v.Size).HasMaxLength(ProductLimits.VariantOptionMaxLength);
                e.Property(v => v.Colour).HasMaxLength(ProductLimits.VariantOptionMaxLength);
                e.Property(v => v.Model).HasMaxLength(ProductLimits.VariantOptionMaxLength);
                e.Property(v => v.Material).HasMaxLength(ProductLimits.VariantOptionMaxLength);
                e.HasOne(v => v.Item)
                    .WithMany(i => i.Variants)
                    .HasForeignKey(v => v.ItemId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(v => new { v.ItemId, v.SKU }).IsUnique();
            });

            builder.Entity<ProductImage>(e =>
            {
                e.Property(p => p.FilePath).HasMaxLength(ProductLimits.ImagePathMaxLength);
                e.Property(p => p.ThumbnailPath).HasMaxLength(ProductLimits.ImagePathMaxLength);
                e.Property(p => p.ContentType).HasMaxLength(50);
                e.HasOne(p => p.Item)
                    .WithMany(i => i.Images)
                    .HasForeignKey(p => p.ItemId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(p => p.ItemId);
            });

            builder.Entity<Shop>(e =>
            {
                e.Property(s => s.Name).HasMaxLength(ValidationLimits.ShopNameMaxLength);
                e.Property(s => s.Location).HasMaxLength(ValidationLimits.ShopLocationMaxLength);
            });

            builder.Entity<Sale>(e =>
            {
                e.Property(s => s.TotalPrice).HasPrecision(18, 2);
                e.HasOne(s => s.Shop)
                    .WithMany()
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(s => s.Item)
                    .WithMany()
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(s => s.ShopkeeperId);
                e.HasIndex(s => s.ShopId);
                e.HasIndex(s => s.ItemId);
                e.HasIndex(s => s.SaleDate);
                // History page: filter by shopkeeper, sort by date.
                e.HasIndex(s => new { s.ShopkeeperId, s.SaleDate });
            });

            builder.Entity<ShopAssignment>(e =>
            {
                e.HasOne(sa => sa.Shop)
                    .WithMany(s => s.ShopAssignments)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(sa => sa.ShopkeeperId);
                e.HasIndex(sa => sa.ShopId);
                e.HasIndex(sa => new { sa.ShopkeeperId, sa.ShopId })
                    .IsUnique();
            });

            builder.Entity<Order>(e =>
            {
                e.Property(o => o.UnitPrice).HasPrecision(18, 2);
                e.Property(o => o.TotalPrice).HasPrecision(18, 2);
                e.HasOne(o => o.Item)
                    .WithMany()
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(o => o.Shop)
                    .WithMany()
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(o => o.Customer)
                    .WithMany()
                    .HasForeignKey(o => o.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(o => o.CustomerId);
                e.HasIndex(o => o.ShopId);
                e.HasIndex(o => o.ItemId);
                e.HasIndex(o => o.Status);
                e.HasIndex(o => o.OrderedAt);
                // Customer "My orders" and shopkeeper order boards.
                e.HasIndex(o => new { o.CustomerId, o.OrderedAt });
                e.HasIndex(o => new { o.ShopId, o.OrderedAt });
                e.HasIndex(o => new { o.ShopId, o.Status });
            });

            builder.Entity<CustomerInterest>(e =>
            {
                e.Property(i => i.Category).HasMaxLength(100);
                e.Property(i => i.UserId).HasMaxLength(450);
                e.HasIndex(i => i.UserId);
                e.HasIndex(i => i.ItemId);
                e.HasIndex(i => new { i.UserId, i.ItemId, i.Type, i.CreatedAt });
            });
        }
    }
}