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

            builder.Entity<Item>()
        .Property(i => i.Price)
        .HasPrecision(18, 2);

            builder.Entity<Sale>()
                .Property(s => s.TotalPrice)
                .HasPrecision(18, 2);

            builder.Entity<Item>()
                .HasOne(i => i.Shop)
                .WithMany(s => s.Items)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Sale>()
                .HasOne(s => s.Shop)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Sale>()
                .HasOne(s => s.Item)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ShopAssignment>()
                .HasOne(sa => sa.Shop)
                .WithMany(s => s.ShopAssignments)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Order>()
    .Property(o => o.UnitPrice)
    .HasPrecision(18, 2);

            builder.Entity<Order>()
                .Property(o => o.TotalPrice)
                .HasPrecision(18, 2);

            builder.Entity<Order>()
                .HasOne(o => o.Item)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Order>()
                .HasOne(o => o.Shop)
                .WithMany()
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Order>()
                .HasOne(o => o.Customer)
                .WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}