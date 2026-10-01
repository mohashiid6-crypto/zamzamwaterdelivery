using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZamzamWaterDelivery.Domain.Entities;

namespace ZamzamWaterDelivery.Infrastructure.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.Property(c => c.Name).HasMaxLength(80).IsRequired();
        b.Property(c => c.Description).HasMaxLength(500);
        b.HasIndex(c => c.Name).IsUnique().HasFilter("\"IsDeleted\" = false");
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Products_StockQuantity", "\"StockQuantity\" >= 0");
            t.HasCheckConstraint("CK_Products_Price", "\"Price\" > 0");
        });

        b.Property(p => p.Name).HasMaxLength(100).IsRequired();
        b.Property(p => p.Description).HasMaxLength(1000).IsRequired();
        b.Property(p => p.Price).HasPrecision(18, 2);
        b.Property(p => p.ImageUrl).HasMaxLength(500);

        b.HasIndex(p => p.Name).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasIndex(p => new { p.IsDeleted, p.IsActive });

        b.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> b)
    {
        b.HasIndex(c => c.CustomerId).IsUnique();

        b.HasOne(c => c.Customer)
            .WithOne(c => c.Cart)
            .HasForeignKey<Cart>(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.ToTable(t => t.HasCheckConstraint("CK_CartItems_Quantity", "\"Quantity\" > 0"));
        b.HasIndex(i => new { i.CartId, i.ProductId }).IsUnique();

        b.HasOne(i => i.Cart)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(i => i.Product)
            .WithMany(p => p.CartItems)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

