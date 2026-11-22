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

        public DbSet<Order> Orders { get; set; }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Item>(e =>
            {
                e.Property(i => i.Name).HasMaxLength(ValidationLimits.ItemNameMaxLength);
                e.Property(i => i.Description).HasMaxLength(ValidationLimits.ItemDescriptionMaxLength);
                e.Property(i => i.Price).HasPrecision(18, 2);
                e.Property(i => i.Category).HasMaxLength(ValidationLimits.CategoryMaxLength);
                e.HasOne(i => i.Shop)
                    .WithMany(s => s.Items)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(i => i.ShopId);
                e.HasIndex(i => i.Category);
                e.HasIndex(i => i.Name);
                // One item name per shop (application layer also enforces this).
                e.HasIndex(i => new { i.ShopId, i.Name }).IsUnique();
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