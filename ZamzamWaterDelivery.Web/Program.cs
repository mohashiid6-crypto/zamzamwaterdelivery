using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ZamzamWaterDelivery.Application;
using ZamzamWaterDelivery.Application.Common;
using ZamzamWaterDelivery.Application.Interfaces;
using ZamzamWaterDelivery.Domain.Entities;
using ZamzamWaterDelivery.Infrastructure;
using ZamzamWaterDelivery.Infrastructure.Data;
using ZamzamWaterDelivery.Infrastructure.Data.Seed;
using ZamzamWaterDelivery.Infrastructure.Identity;
using ZamzamWaterDelivery.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Settings ----------
builder.Services.Configure<DeliverySettings>(config.GetSection(DeliverySettings.SectionName));
builder.Services.Configure<CompanySettings>(config.GetSection(CompanySettings.SectionName));
var company = config.GetSection(CompanySettings.SectionName).Get<CompanySettings>() ?? new CompanySettings();
builder.Services.AddSingleton(company);
Ui.CurrencySymbol = company.CurrencySymbol;

var jwt = config.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrWhiteSpace(jwt.Key))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Jwt:Key must be configured (environment variable Jwt__Key or a secret store).");

    // Development fallback: an ephemeral key (API tokens become invalid when the app restarts).
    jwt.Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    config[$"{JwtSettings.SectionName}:Key"] = jwt.Key;
}
if (jwt.Key.Length < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 characters long.");

// ---------- Layers ----------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();

// ---------- Identity & authentication ----------
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "Zamzam.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    // API callers get status codes instead of HTML redirects.
    options.Events.OnRedirectToLogin = ctx => ApiAwareRedirect(ctx.HttpContext, ctx.RedirectUri, StatusCodes.Status401Unauthorized);
    options.Events.OnRedirectToAccessDenied = ctx => ApiAwareRedirect(ctx.HttpContext, ctx.RedirectUri, StatusCodes.Status403Forbidden);
});

builder.Services.AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwt.GetSigningKey(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();

// ---------- MVC, API, validation ----------
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddControllersWithViews(options =>
    {
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
        options.Filters.Add<AppExceptionFilter>();
    })
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .SelectMany(e => e.Value!.Errors.Select(err => string.IsNullOrEmpty(e.Key) ? err.ErrorMessage : $"{e.Key}: {err.ErrorMessage}"))
            .ToList();
        return new BadRequestObjectResult(ApiResponse.Fail("Validation failed.", errors));
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ---------- Swagger ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Zamzam Water Delivery API",
        Version = "v1",
        Description = "REST API for products, orders, deliveries, drivers, customers and payments."
    });
    options.DocInclusionPredicate((_, api) => api.RelativePath?.StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true);
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Paste the token from POST /api/auth/login",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});

builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(180));

// Hosting platforms (e.g. Railway) terminate HTTPS at a proxy and forward the original scheme.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// ---------- Database ----------
await DbSeeder.MigrateAndSeedAsync(app.Services, config.GetValue("Database:ApplyMigrationsOnStartup", true));

// ---------- Pipeline ----------
app.UseForwardedHeaders();
app.UseExceptionHandler("/Home/Error");
if (!app.Environment.IsDevelopment()) app.UseHsts();

app.UseStatusCodePagesWithReExecute("/Home/StatusCode/{0}");
app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStaticFiles();

if (app.Environment.IsDevelopment() || config.GetValue<bool>("App:EnableSwagger"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.DocumentTitle = "Zamzam Water Delivery API");
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

app.Run();

static Task ApiAwareRedirect(HttpContext context, string redirectUri, int statusCode)
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Headers.XRequestedWith == "XMLHttpRequest")
        context.Response.StatusCode = statusCode;
    else
        context.Response.Redirect(redirectUri);
    return Task.CompletedTask;
}

/// <summary>Exposed for integration tests.</summary>
public partial class Program;
