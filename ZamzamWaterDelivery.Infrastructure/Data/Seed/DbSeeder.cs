using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZamzamWaterDelivery.Domain.Common;
using ZamzamWaterDelivery.Domain.Entities;
using ZamzamWaterDelivery.Domain.Enums;

namespace ZamzamWaterDelivery.Infrastructure.Data.Seed;

public class SeedSettings
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; } = true;
    public string AdminEmail { get; set; } = "admin@zamzam.so";
    public string DriverEmail { get; set; } = "driver@zamzam.so";
    public string CustomerEmail { get; set; } = "customer@zamzam.so";

    /// <summary>Password for the seeded accounts. Supply via User Secrets / environment (Seed__DefaultPassword).</summary>
    public string? DefaultPassword { get; set; }

    /// <summary>Generate demo orders so the dashboard and reports have data.</summary>
    public bool SampleOrders { get; set; } = true;
}

public static class DbSeeder
{
    public static async Task MigrateAndSeedAsync(IServiceProvider services, bool applyMigrations)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var settings = sp.GetRequiredService<IOptions<SeedSettings>>().Value;

        if (applyMigrations)
        {
            if (db.Database.IsSqlServer())
            {
                logger.LogInformation("Applying database migrations...");
                await db.Database.MigrateAsync();
            }
            else
            {
                // The migrations are SQL Server specific; other providers get the schema straight from the model.
                logger.LogInformation("Creating database schema if it does not exist...");
                await db.Database.EnsureCreatedAsync();
            }
        }
        if (!settings.Enabled) return;

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var products = await SeedCatalogAsync(db);

        if (string.IsNullOrWhiteSpace(settings.DefaultPassword))
        {
            logger.LogWarning("Seed:DefaultPassword is not configured - demo users were not created. " +
                              "Set it with: dotnet user-secrets set \"Seed:DefaultPassword\" \"<password>\"");
            return;
        }

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var password = settings.DefaultPassword;

        await EnsureUserAsync(users, settings.AdminEmail, "Zamzam Administrator", "+252 90 7000000", password, Roles.Admin, logger);

        var driver1 = await EnsureDriverAsync(db, users, settings.DriverEmail, "Abdirahman Ali Warsame", "+252 90 7111111",
            "Pickup Truck", "PL-4521", "DL-GRW-10021", password, logger);
        var driver2 = await EnsureDriverAsync(db, users, "driver2@zamzam.so", "Hassan Mohamed Yusuf", "+252 90 7222222",
            "Bajaj (Tuk-tuk)", "PL-7788", "DL-GRW-10057", password, logger);

        var customer1 = await EnsureCustomerAsync(db, users, settings.CustomerEmail, "Amina Hassan Farah", "+252 90 7333333",
            "House 12, Hodan Street", "Garowe", "Near Garowe Central Market", password, logger);
        var customer2 = await EnsureCustomerAsync(db, users, "mohamed@zamzam.so", "Mohamed Abdi Nur", "+252 90 7444444",
            "Building 4, Airport Road", "Garowe", "Opposite East Africa University", password, logger);

        if (settings.SampleOrders && !await db.Orders.AnyAsync())
        {
            SeedSampleOrders(db, products, new[] { customer1, customer2 }, new[] { driver1, driver2 });
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded sample orders");
        }
    }

    private static async Task<List<Product>> SeedCatalogAsync(ApplicationDbContext db)
    {
        if (await db.Products.AnyAsync())
            return await db.Products.ToListAsync();

        var large = new Category { Name = "Large Bottles", Description = "Refillable dispenser bottles for homes and offices." };
        var small = new Category { Name = "Small Bottles", Description = "Handy bottles for families, travel and events." };
        var cartons = new Category { Name = "Cartons", Description = "Cartons of sealed personal-size bottles." };
        db.Categories.AddRange(large, small, cartons);

        var products = new List<Product>
        {
            new() { Name = "20 Liter Water Bottle", Category = large, Price = 3.00m, StockQuantity = 200,
                ImageUrl = "/images/products/bottle-20l.svg",
                Description = "Purified drinking water in a reusable 20L dispenser bottle. Ideal for homes and offices." },
            new() { Name = "10 Liter Water Bottle", Category = large, Price = 1.75m, StockQuantity = 150,
                ImageUrl = "/images/products/bottle-10l.svg",
                Description = "10 liter bottle with a carry handle - perfect for small families." },
            new() { Name = "5 Liter Water Bottle", Category = small, Price = 1.00m, StockQuantity = 300,
                ImageUrl = "/images/products/bottle-5l.svg",
                Description = "Convenient 5 liter bottle for the kitchen, car or travel." },
            new() { Name = "Water Carton (24 x 500ml)", Category = cartons, Price = 4.50m, StockQuantity = 120,
                ImageUrl = "/images/products/carton.svg",
                Description = "24 sealed 500ml bottles - great for events, schools and offices." },
            new() { Name = "Water Carton (12 x 1.5L)", Category = cartons, Price = 5.00m, StockQuantity = 80,
                ImageUrl = "/images/products/carton-large.svg",
                Description = "12 sealed 1.5 liter bottles for the whole week." },
            new() { Name = "1.5 Liter Water Bottle", Category = small, Price = 0.50m, StockQuantity = 15,
                ImageUrl = "/images/products/bottle-small.svg",
                Description = "Single 1.5 liter bottle of pure drinking water." }
        };
        db.Products.AddRange(products);
        await db.SaveChangesAsync();
        return products;
    }

    private static async Task<ApplicationUser> EnsureUserAsync(UserManager<ApplicationUser> users, string email, string fullName,
        string phone, string password, string role, ILogger logger)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is not null) return user;

        user = new ApplicationUser
        {
            UserName = email, Email = email, EmailConfirmed = true, FullName = fullName, PhoneNumber = phone, IsActive = true
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not seed {email}: " + string.Join("; ", result.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(user, role);
        logger.LogInformation("Seeded {Role} account {Email}", role, email);
        return user;
    }

    private static async Task<Driver> EnsureDriverAsync(ApplicationDbContext db, UserManager<ApplicationUser> users, string email,
        string fullName, string phone, string vehicleType, string vehicleNumber, string license, string password, ILogger logger)
    {
        var user = await EnsureUserAsync(users, email, fullName, phone, password, Roles.Driver, logger);
        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.UserId == user.Id);
        if (driver is not null) return driver;

        driver = new Driver
        {
            UserId = user.Id, FullName = fullName, Email = email, PhoneNumber = phone,
            VehicleType = vehicleType, VehicleNumber = vehicleNumber, LicenseNumber = license, Status = DriverStatus.Available
        };
        db.Drivers.Add(driver);
        await db.SaveChangesAsync();
        return driver;
    }

    private static async Task<Customer> EnsureCustomerAsync(ApplicationDbContext db, UserManager<ApplicationUser> users, string email,
        string fullName, string phone, string addressLine, string city, string location, string password, ILogger logger)
    {
        var user = await EnsureUserAsync(users, email, fullName, phone, password, Roles.Customer, logger);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.UserId == user.Id);
        if (customer is not null) return customer;

        customer = new Customer
        {
            UserId = user.Id, FullName = fullName, Email = email, PhoneNumber = phone, Cart = new Cart(),
            Addresses = { new Address { AddressLine = addressLine, City = city, Location = location, IsDefault = true } }
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static void SeedSampleOrders(ApplicationDbContext db, IReadOnlyList<Product> products,
        IReadOnlyList<Customer> customers, IReadOnlyList<Driver> drivers)
    {
        var random = new Random(2026);
        var today = DateTime.Today;
        const decimal deliveryFee = 2.00m;

        for (var i = 0; i < 70; i++)
        {
            // Half of the history falls in the last month so the dashboard charts have data.
            var daysAgo = i < 4 ? 0 : i % 2 == 0 ? random.Next(1, 31) : random.Next(31, 365);
            var date = today.AddDays(-daysAgo).AddHours(8 + random.Next(0, 11)).AddMinutes(random.Next(0, 60));
            var customer = customers[random.Next(customers.Count)];
            var address = customer.Addresses.First();

            // Older orders are mostly delivered; today's orders are still in progress.
            var status = daysAgo switch
            {
                0 => (OrderStatus)(i % 4),
                _ => random.Next(10) == 0 ? OrderStatus.Cancelled : OrderStatus.Delivered
            };

            var order = new Order
            {
                OrderNumber = $"ZW{date:yyMMdd}{1000 + i}",
                CustomerId = customer.Id,
                OrderDate = date,
                CreatedAt = date,
                Status = status,
                CustomerName = customer.FullName,
                PhoneNumber = customer.PhoneNumber,
                DeliveryAddress = $"{address.AddressLine}, {address.Location}",
                City = address.City,
                PreferredDeliveryDate = date.Date,
                PreferredTimeSlot = "Morning (8:00 AM - 12:00 PM)",
                PaymentMethod = PaymentMethod.CashOnDelivery,
                CancellationReason = status == OrderStatus.Cancelled ? "Customer changed their mind" : null
            };

            foreach (var product in products.OrderBy(_ => random.Next()).Take(random.Next(1, 3)))
            {
                var qty = random.Next(1, 5);
                order.Items.Add(new OrderItem
                {
                    ProductId = product.Id, ProductName = product.Name, UnitPrice = product.Price,
                    Quantity = qty, LineTotal = product.Price * qty, CreatedAt = date
                });
            }

            order.SubTotal = order.Items.Sum(x => x.LineTotal);
            order.DeliveryFee = order.SubTotal >= 50 ? 0 : deliveryFee;
            order.Total = order.SubTotal + order.DeliveryFee;

            var driver = drivers[random.Next(drivers.Count)];
            var hasDriver = status is OrderStatus.Confirmed or OrderStatus.OutForDelivery or OrderStatus.Delivered;
            order.Delivery = new Delivery
            {
                DeliveryAddress = $"{order.DeliveryAddress}, {order.City}",
                DeliveryFee = order.DeliveryFee,
                DeliveryDate = date.Date,
                DeliveryTime = order.PreferredTimeSlot,
                DriverId = hasDriver ? driver.Id : null,
                AssignedAt = hasDriver ? date.AddMinutes(20) : null,
                AcceptedAt = status is OrderStatus.OutForDelivery or OrderStatus.Delivered ? date.AddMinutes(30) : null,
                PickedUpAt = status is OrderStatus.OutForDelivery or OrderStatus.Delivered ? date.AddMinutes(45) : null,
                DeliveredAt = status == OrderStatus.Delivered ? date.AddMinutes(90) : null,
                CreatedAt = date,
                Status = status switch
                {
                    OrderStatus.Confirmed => DeliveryStatus.Assigned,
                    OrderStatus.OutForDelivery => DeliveryStatus.OutForDelivery,
                    OrderStatus.Delivered => DeliveryStatus.Delivered,
                    OrderStatus.Cancelled => DeliveryStatus.Cancelled,
                    _ => DeliveryStatus.Unassigned
                }
            };
            order.Payment = new Payment
            {
                Amount = order.Total,
                Method = PaymentMethod.CashOnDelivery,
                Status = status switch
                {
                    OrderStatus.Delivered => PaymentStatus.Paid,
                    OrderStatus.Cancelled => PaymentStatus.Failed,
                    _ => PaymentStatus.Pending
                },
                TransactionReference = status == OrderStatus.Delivered ? $"COD-{order.OrderNumber}" : null,
                PaymentDate = status == OrderStatus.Delivered ? date.AddMinutes(90) : null,
                CreatedAt = date
            };

            db.Orders.Add(order);
        }

        // The driver carrying today's out-for-delivery order is busy.
        foreach (var d in drivers)
        {
            if (db.ChangeTracker.Entries<Delivery>().Any(e => e.Entity.DriverId == d.Id && e.Entity.Status == DeliveryStatus.OutForDelivery))
                d.Status = DriverStatus.Busy;
        }
    }
}
