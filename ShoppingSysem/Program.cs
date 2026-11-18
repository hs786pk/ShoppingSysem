
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Components;
using ShoppingSysem.Components.Account;
using ShoppingSysem.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider,
    IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
.AddIdentityCookies();

var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Identity security configuration.
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;

    options.User.RequireUniqueEmail = true;

    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;

    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan =
        TimeSpan.FromMinutes(15);

    options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

// Secure the application authentication cookie.
builder.Services.Configure<CookieAuthenticationOptions>(
    IdentityConstants.ApplicationScheme,
    options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.Cookie.SecurePolicy =
            builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;

        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection("Smtp"));

builder.Services.AddSingleton<IEmailSender<ApplicationUser>,
    SmtpEmailSender>();
builder.Services.AddScoped<
    ShoppingSysem.Services.IRecommendationService,
    ShoppingSysem.Services.RecommendationService>();

builder.Services.AddScoped<
    ShoppingSysem.Services.IOrderService,
    ShoppingSysem.Services.OrderService>();

var app = builder.Build();

// Email confirmation is mandatory, so outside Development a missing SMTP
// configuration must stop startup rather than silently lock users out.
var smtpOptions = app.Configuration.GetSection("Smtp").Get<SmtpOptions>();
if (string.IsNullOrWhiteSpace(smtpOptions?.Host) ||
    string.IsNullOrWhiteSpace(smtpOptions.FromAddress))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "SMTP is not configured. Set Smtp:Host and Smtp:FromAddress " +
            "(plus Smtp:Username and Smtp:Password) in protected configuration.");
    }

    app.Logger.LogWarning(
        "SMTP is not configured; confirmation and password-reset emails will not be sent.");
}
// Seed required application roles.
using (var scope = app.Services.CreateScope())
{
    var roleManager =
        scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    string[] roleNames =
    {
        "Admin",
        "Shopkeeper",
        "Customer"
    };

    foreach (var roleName in roleNames)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var result = await roleManager.CreateAsync(
                new IdentityRole(roleName));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create required role '{roleName}': " +
                    string.Join("; ", result.Errors.Select(e => e.Code)));
            }
        }
    }
}

// Secure, explicitly enabled administrator provisioning.
// Credentials must come from protected configuration.
if (app.Configuration.GetValue<bool>("AdminProvisioning:Enabled"))
{
    var adminEmail = app.Configuration[
        "AdminProvisioning:Email"]?.Trim();

    var adminPassword = app.Configuration[
        "AdminProvisioning:Password"];

    if (string.IsNullOrWhiteSpace(adminEmail) ||
        !new EmailAddressAttribute().IsValid(adminEmail) ||
        string.IsNullOrWhiteSpace(adminPassword) ||
        adminPassword.Length < 12)
    {
        throw new InvalidOperationException(
            "Administrator provisioning is enabled, but its " +
            "configuration is missing or invalid.");
    }

    using var scope = app.Services.CreateScope();

    var userManager =
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    var existingAdmin =
        await userManager.FindByEmailAsync(adminEmail);

    if (existingAdmin is null)
    {
        var adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };

        var createResult =
            await userManager.CreateAsync(adminUser, adminPassword);

        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Administrator account creation failed: " +
                string.Join("; ", createResult.Errors.Select(e => e.Code)));
        }

        var roleResult =
            await userManager.AddToRoleAsync(adminUser, "Admin");

        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(adminUser);

            throw new InvalidOperationException(
                "Administrator role assignment failed: " +
                string.Join("; ", roleResult.Errors.Select(e => e.Code)));
        }

        app.Logger.LogInformation(
            "Initial administrator account provisioned successfully.");
    }
    else if (!await userManager.IsInRoleAsync(existingAdmin, "Admin"))
    {
        throw new InvalidOperationException(
            "The configured administrator email already belongs to " +
            "a non-administrator account. Automatic privilege escalation " +
            "has been refused.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler(
        "/Error",
        createScopeForErrors: true);

    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // Blocks clickjacking and plugin content. Scripts and styles are unrestricted,
    // so Blazor's inline scripts keep working.
    headers["Content-Security-Policy"] =
        "frame-ancestors 'none'; base-uri 'self'; object-src 'none'";
    await next();
});
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

app.Run();
public partial class Program { }