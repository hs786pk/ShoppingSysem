using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ShoppingSysem.Components;
using ShoppingSysem.Components.Account;
using ShoppingSysem.Data;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Threading.RateLimiting;

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

if (builder.Environment.IsDevelopment())
{
    // The database developer page includes SQL text and other internal details.
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();
}
else
{
    // DetailedErrors in configuration must not reveal circuit exception
    // messages or stack traces outside Development.
    builder.Services.PostConfigure<CircuitOptions>(options =>
    {
        options.DetailedErrors = false;
    });
}

// Limit account-related form submissions without throttling ordinary app traffic.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (!HttpMethods.IsPost(context.Request.Method) ||
            !context.Request.Path.StartsWithSegments("/Account"))
        {
            return RateLimitPartition.GetNoLimiter("non-account");
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

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

// Secure every Identity cookie. The 8-hour lifetime applies only to the
// application cookie so the short-lived two-factor cookies keep their own.
void HardenIdentityCookie(string scheme, bool persistentApplicationCookie)
{
    builder.Services.Configure<CookieAuthenticationOptions>(scheme, options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        if (persistentApplicationCookie)
        {
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
        }
    });
}

HardenIdentityCookie(IdentityConstants.ApplicationScheme, persistentApplicationCookie: true);
HardenIdentityCookie(IdentityConstants.ExternalScheme, persistentApplicationCookie: false);
HardenIdentityCookie(IdentityConstants.TwoFactorUserIdScheme, persistentApplicationCookie: false);
HardenIdentityCookie(IdentityConstants.TwoFactorRememberMeScheme, persistentApplicationCookie: false);

builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<DevelopmentEmailOptions>(
    builder.Configuration.GetSection("DevelopmentEmail"));

// Prefer real SMTP when configured. In Development with no SMTP, capture
// emails to disk so confirmation and reset flows remain testable.
var smtpConfig = builder.Configuration.GetSection("Smtp");
var smtpHost = smtpConfig["Host"];
var smtpFrom = smtpConfig["FromAddress"];
var smtpConfigured = !string.IsNullOrWhiteSpace(smtpHost) &&
                     !string.IsNullOrWhiteSpace(smtpFrom);

if (smtpConfigured)
{
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, SmtpEmailSender>();
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, DevelopmentEmailSender>();
}
else
{
    // Production/staging without SMTP: keep the SMTP sender so startup
    // validation below fails loudly instead of silently dropping mail.
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, SmtpEmailSender>();
}

builder.Services.AddScoped<
    ShoppingSysem.Services.IRecommendationService,
    ShoppingSysem.Services.RecommendationService>();

builder.Services.AddScoped<
    ShoppingSysem.Services.IOrderService,
    ShoppingSysem.Services.OrderService>();

builder.Services.AddScoped<
    ShoppingSysem.Services.IShopAccessService,
    ShoppingSysem.Services.ShopAccessService>();

// Trust X-Forwarded-For / X-Forwarded-Proto only from configured proxies.
// With no entries, the headers are ignored so a client cannot spoof its IP.
var forwardedSection = builder.Configuration.GetSection("ForwardedHeaders");
var forwardLimit = forwardedSection.GetValue("ForwardLimit", 1);
if (forwardLimit < 1)
{
    throw new InvalidOperationException(
        "ForwardedHeaders:ForwardLimit must be at least 1.");
}

var knownProxies = new List<IPAddress>();
foreach (var value in forwardedSection.GetSection("KnownProxies").Get<string[]>() ?? [])
{
    var text = value?.Trim();
    if (string.IsNullOrEmpty(text) || !IPAddress.TryParse(text, out var proxyIp))
    {
        throw new InvalidOperationException(
            "ForwardedHeaders:KnownProxies contains an invalid IP address.");
    }

    knownProxies.Add(proxyIp);
}

var knownNetworks = new List<System.Net.IPNetwork>();
foreach (var value in forwardedSection.GetSection("KnownNetworks").Get<string[]>() ?? [])
{
    var text = value?.Trim();
    if (string.IsNullOrEmpty(text) || !System.Net.IPNetwork.TryParse(text, out var network))
    {
        throw new InvalidOperationException(
            "ForwardedHeaders:KnownNetworks contains an invalid CIDR network.");
    }

    if (network.PrefixLength == 0)
    {
        throw new InvalidOperationException(
            "ForwardedHeaders:KnownNetworks must not be a match-all network.");
    }

    knownNetworks.Add(network);
}

var trustForwardedHeaders = knownProxies.Count > 0 || knownNetworks.Count > 0;
if (trustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
            Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = forwardLimit;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxyIp in knownProxies)
            options.KnownProxies.Add(proxyIp);

        foreach (var network in knownNetworks)
            options.KnownIPNetworks.Add(network);
    });
}

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

    var devEmailDir = app.Configuration["DevelopmentEmail:OutputDirectory"] ?? "DevEmails";
    app.Logger.LogWarning(
        "SMTP is not configured. Development emails will be written under '{Dir}'.",
        Path.IsPathRooted(devEmailDir)
            ? devEmailDir
            : Path.Combine(app.Environment.ContentRootPath, devEmailDir));
}
// Seed required application roles.
using (var scope = app.Services.CreateScope())
{
    var roleManager =
        scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    string[] roleNames = AppRoles.All;

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
            await userManager.AddToRoleAsync(adminUser, AppRoles.Admin);

        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(adminUser);

            throw new InvalidOperationException(
                "Administrator role assignment failed: " +
                string.Join("; ", roleResult.Errors.Select(e => e.Code)));
        }

        app.Logger.LogInformation(
            "Initial administrator account provisioned for user {UserId}.",
            adminUser.Id);
    }
    else if (!await userManager.IsInRoleAsync(existingAdmin, AppRoles.Admin))
    {
        throw new InvalidOperationException(
            "The configured administrator email already belongs to " +
            "a non-administrator account. Automatic privilege escalation " +
            "has been refused.");
    }
}
// Configure the HTTP request pipeline.
// Only after KnownProxies / KnownNetworks are non-empty. Loopback is not trusted implicitly.
if (trustForwardedHeaders)
    app.UseForwardedHeaders();
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

    // Off unless a test explicitly enables it. The marker must never appear
    // in a client response; the exception handler logs it server-side only.
    if (app.Configuration.GetValue<bool>("Testing:EnableExceptionProbe"))
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/__test/throw"))
            {
                throw new InvalidOperationException("phase1-exception-marker");
            }

            await next();
        });
    }
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    // 'unsafe-inline' stays because Blazor emits an import map and App.razor
    // has an inline sidebar script, plus inline style attributes.
    // upgrade-insecure-requests is omitted so HTTP development and tests still work.
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "form-action 'self'; " +
        "frame-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "object-src 'none'";
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