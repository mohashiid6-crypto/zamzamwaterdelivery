using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ZamzamWaterDelivery.Domain.Entities;

namespace ZamzamWaterDelivery.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Filtered unique indexes are written in SQL Server syntax ("[IsDeleted] = 0"); translate for PostgreSQL.
        if (Database.IsNpgsql())
        {
            foreach (var index in builder.Model.GetEntityTypes().SelectMany(e => e.GetIndexes()))
            {
                var filter = index.GetFilter();
                if (filter is null) continue;
                index.SetFilter(filter.Replace("[", "\"").Replace("]", "\"").Replace("= 0", "= false").Replace("= 1", "= true"));
            }
        }
    }
}
