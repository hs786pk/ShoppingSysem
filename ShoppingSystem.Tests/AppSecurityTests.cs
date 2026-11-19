using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShoppingSysem.Data;
using Xunit;

namespace ShoppingSystem.Tests;

// Runs the real Program.cs against a temporary SQLite database. Program.cs reads
// the admin-provisioning settings from environment variables at startup, so every
// class that starts the app is in one non-parallel collection.
[CollectionDefinition("AppStartup", DisableParallelization = true)]
public sealed class AppStartupCollection { }

public sealed class ShopFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            // Replace the SQL Server registration with the SQLite test database.
            var remove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                (d.ServiceType.IsGenericType &&
                 d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration") &&
                 d.ServiceType.GenericTypeArguments.Contains(typeof(ApplicationDbContext))))
                .ToList();

            foreach (var descriptor in remove)
                services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connectionString));
        });
    }
}

internal sealed class ProvisioningEnv : IDisposable
{
    private static readonly string[] Keys =
    {
        "AdminProvisioning__Enabled",
        "AdminProvisioning__Email",
        "AdminProvisioning__Password"
    };

    public ProvisioningEnv(bool enabled, string? email, string? password)
    {
        Environment.SetEnvironmentVariable(Keys[0], enabled ? "true" : "false");
        Environment.SetEnvironmentVariable(Keys[1], email);
        Environment.SetEnvironmentVariable(Keys[2], password);
    }

    public void Dispose()
    {
        foreach (var key in Keys)
            Environment.SetEnvironmentVariable(key, null);
    }
}

[Collection("AppStartup")]
public sealed class AdminProvisioningTests : IDisposable
{
    private const string AdminEmail = "admin@shop.test";
    private const string StrongPassword = "Str0ng-Admin-Pass!";

    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();

    private void Start(bool enabled, string? email, string? password)
    {
        using var env = new ProvisioningEnv(enabled, email, password);
        using var factory = new ShopFactory(_db.ConnectionString);
        _ = factory.Services;
    }

    private string StartFails(string? email, string? password)
    {
        using var env = new ProvisioningEnv(true, email, password);
        using var factory = new ShopFactory(_db.ConnectionString);

        var ex = Assert.ThrowsAny<Exception>(() => { _ = factory.Services; });

        var messages = new StringBuilder();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            messages.AppendLine(e.Message);
        return messages.ToString();
    }

    private Task<int> UserCountAsync() =>
        _db.QueryAsync(db => db.Users.CountAsync());

    private Task<List<string>> RolesOfAsync(string email) =>
        _db.QueryAsync(db => (
            from u in db.Users
            join ur in db.UserRoles on u.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where u.Email == email
            select r.Name!).ToListAsync());

    [Fact]
    public async Task Valid_Configuration_CreatesConfirmedAdmin_WhoCanSignInWithPassword()
    {
        using var env = new ProvisioningEnv(true, AdminEmail, StrongPassword);
        using var factory = new ShopFactory(_db.ConnectionString);
        _ = factory.Services;

        Assert.Equal(new[] { "Admin" }, await RolesOfAsync(AdminEmail));

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.FindByEmailAsync(AdminEmail);

        Assert.NotNull(admin);
        Assert.True(admin!.EmailConfirmed);
        Assert.True(await users.CheckPasswordAsync(admin, StrongPassword));
    }

    [Fact]
    public async Task Restart_DoesNotDuplicateTheAdmin()
    {
        Start(true, AdminEmail, StrongPassword);
        Start(true, AdminEmail, StrongPassword);

        Assert.Equal(1, await UserCountAsync());
        Assert.Equal(new[] { "Admin" }, await RolesOfAsync(AdminEmail));
    }

    [Fact]
    public async Task Disabled_CreatesNoUser_ButSeedsAllRoles()
    {
        Start(false, AdminEmail, StrongPassword);

        Assert.Equal(0, await UserCountAsync());

        var roles = await _db.QueryAsync(db => db.Roles.Select(r => r.Name!).ToListAsync());
        Assert.Contains("Admin", roles);
        Assert.Contains("Shopkeeper", roles);
        Assert.Contains("Customer", roles);
    }

    [Theory]
    [InlineData(AdminEmail, "short")]
    [InlineData(AdminEmail, "alllowercaseonly")]
    [InlineData(AdminEmail, null)]
    [InlineData("not-an-email", StrongPassword)]
    [InlineData(null, StrongPassword)]
    public async Task Invalid_Configuration_StopsStartup_AndCreatesNoUser(string? email, string? password)
    {
        StartFails(email, password);

        Assert.Equal(0, await UserCountAsync());
    }

    [Fact]
    public async Task ExistingNonAdminAccount_IsNeverPromoted()
    {
        await _db.AddUserAsync("victim@shop.test", "Customer");

        var messages = StartFails("victim@shop.test", StrongPassword);

        Assert.Contains("privilege escalation", messages);
        Assert.Equal(new[] { "Customer" }, await RolesOfAsync("victim@shop.test"));
    }
}

public sealed class AppFixture : IDisposable
{
    public TestDb Db { get; } = new();
    public ShopFactory Factory { get; }

    public AppFixture()
    {
        // The fixture must start without any provisioning settings.
        using var env = new ProvisioningEnv(false, null, null);
        Factory = new ShopFactory(Db.ConnectionString);
        _ = Factory.Services;
    }

    public void Dispose()
    {
        Factory.Dispose();
        Db.Dispose();
    }
}

[Collection("AppStartup")]
public sealed class IdentitySecurityTests(AppFixture app) : IClassFixture<AppFixture>
{
    private const string StrongPassword = "Str0ng-Passw0rd!";

    private static string NewEmail() => $"{Guid.NewGuid():N}@shop.test";

    private async Task<(ApplicationUser User, IServiceScope Scope)> CreateUserAsync(
        string email, bool confirmed)
    {
        var scope = app.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = confirmed };

        Assert.True((await users.CreateAsync(user, StrongPassword)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "Customer")).Succeeded);
        return (user, scope);
    }

    [Fact]
    public void IdentityOptions_MatchTheSecurityRequirements()
    {
        using var scope = app.Factory.Services.CreateScope();
        var o = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.True(o.Password.RequiredLength >= 12);
        Assert.True(o.Password.RequireDigit);
        Assert.True(o.Password.RequireLowercase);
        Assert.True(o.Password.RequireUppercase);
        Assert.True(o.Password.RequireNonAlphanumeric);
        Assert.True(o.SignIn.RequireConfirmedAccount);
        Assert.True(o.User.RequireUniqueEmail);
        Assert.True(o.Lockout.AllowedForNewUsers);
        Assert.InRange(o.Lockout.MaxFailedAccessAttempts, 1, 5);
    }

    [Theory]
    [InlineData("Short1!a")]            // too short
    [InlineData("alllowercase1234!")]   // no uppercase
    [InlineData("ALLUPPERCASE1234!")]   // no lowercase
    [InlineData("NoDigitsHere!!!!")]    // no digit
    [InlineData("NoSpecial12345Aa")]    // no symbol
    public async Task WeakPasswords_AreRejected(string password)
    {
        using var scope = app.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = NewEmail();

        var result = await users.CreateAsync(
            new ApplicationUser { UserName = email, Email = email }, password);

        Assert.False(result.Succeeded);
        Assert.Null(await users.FindByEmailAsync(email));
    }

    [Fact]
    public async Task StrongPassword_IsAccepted()
    {
        var (user, scope) = await CreateUserAsync(NewEmail(), confirmed: true);
        using (scope)
            Assert.NotNull(user.Id);
    }

    [Fact]
    public async Task DuplicateEmail_IsRejected()
    {
        var email = NewEmail();
        var (_, scope) = await CreateUserAsync(email, confirmed: true);
        using (scope)
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await users.CreateAsync(
                new ApplicationUser { UserName = NewEmail(), Email = email }, StrongPassword);

            Assert.False(result.Succeeded);
        }
    }

    [Fact]
    public async Task RepeatedWrongPasswords_LockTheAccount_EvenForTheCorrectPassword()
    {
        var (user, scope) = await CreateUserAsync(NewEmail(), confirmed: true);
        using (scope)
        {
            var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            for (var i = 0; i < 5; i++)
            {
                var wrong = await signIn.CheckPasswordSignInAsync(
                    user, "Wrong-Passw0rd!x", lockoutOnFailure: true);
                Assert.False(wrong.Succeeded);
            }

            var correct = await signIn.CheckPasswordSignInAsync(
                user, StrongPassword, lockoutOnFailure: true);

            Assert.True(correct.IsLockedOut);
            Assert.False(correct.Succeeded);

            var lockoutEnd = await users.GetLockoutEndDateAsync(user);
            Assert.NotNull(lockoutEnd);
            Assert.True(lockoutEnd > DateTimeOffset.UtcNow.AddMinutes(10));
        }
    }

    [Fact]
    public async Task UnconfirmedAccount_CannotSignIn()
    {
        var (user, scope) = await CreateUserAsync(NewEmail(), confirmed: false);
        using (scope)
        {
            var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();

            var result = await signIn.CheckPasswordSignInAsync(
                user, StrongPassword, lockoutOnFailure: true);

            Assert.False(result.Succeeded);
            Assert.True(result.IsNotAllowed);
        }
    }

    [Fact]
    public async Task PublicRegistration_CreatesCustomerOnly_IgnoringOverposting()
    {
        var client = app.Factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var page = await client.GetAsync("/Account/Register");
        page.EnsureSuccessStatusCode();

        // Hidden fields carry the form handler name and the antiforgery token.
        var fields = HiddenFields(await page.Content.ReadAsStringAsync());
        Assert.True(fields.ContainsKey("__RequestVerificationToken"));

        var email = NewEmail();
        fields["Input.Email"] = email;
        fields["Input.Password"] = StrongPassword;
        fields["Input.ConfirmPassword"] = StrongPassword;

        // Fields a client might add to try to obtain a role.
        fields["Input.Role"] = "Admin";
        fields["Input.Roles"] = "Admin";
        fields["Role"] = "Admin";
        fields["IsAdmin"] = "true";

        var response = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(fields));

        // 400 would mean the antiforgery check rejected the post; 5xx would be a crash.
        Assert.True((int)response.StatusCode < 400, $"Registration post returned {(int)response.StatusCode}.");

        var roles = await app.Db.QueryAsync(db => (
            from u in db.Users
            join ur in db.UserRoles on u.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where u.Email == email
            select r.Name!).ToListAsync());

        Assert.Equal(new[] { "Customer" }, roles);
    }

    private static Dictionary<string, string> HiddenFields(string html)
    {
        var fields = new Dictionary<string, string>();

        foreach (Match input in Regex.Matches(html, "<input[^>]*type=\"hidden\"[^>]*>"))
        {
            var name = Regex.Match(input.Value, "name=\"([^\"]*)\"");
            var value = Regex.Match(input.Value, "value=\"([^\"]*)\"");

            if (name.Success)
                fields[name.Groups[1].Value] =
                    WebUtility.HtmlDecode(value.Success ? value.Groups[1].Value : "");
        }

        return fields;
    }
}

[Collection("AppStartup")]
public sealed class RateLimitingTests(AppFixture app) : IClassFixture<AppFixture>
{
    private async Task<(string Token, string Cookie)> GetAntiforgeryAsync()
    {
        using var client = app.Factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/Account/Register");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        var tokenMatch = Regex.Match(
            html,
            "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

        Assert.True(tokenMatch.Success, "The registration page did not render an antiforgery token.");
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders),
            "The registration page did not issue an antiforgery cookie.");

        var cookies = setCookieHeaders!
            .Select(header => header.Split(';', 2)[0])
            .ToArray();

        Assert.NotEmpty(cookies);
        return (WebUtility.HtmlDecode(tokenMatch.Groups[1].Value), string.Join("; ", cookies));
    }

    private async Task<int> SendPostAsync(
        string path,
        IPAddress remoteIp,
        string token,
        string cookie)
    {
        var form = "returnUrl=%2F&__RequestVerificationToken=" +
            Uri.EscapeDataString(token);
        var body = Encoding.UTF8.GetBytes(form);

        var context = await app.Factory.Server.SendAsync(httpContext =>
        {
            httpContext.Connection.RemoteIpAddress = remoteIp;
            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.Path = path;
            httpContext.Request.ContentType = "application/x-www-form-urlencoded";
            httpContext.Request.ContentLength = body.Length;
            httpContext.Request.Body = new MemoryStream(body);
            httpContext.Request.Headers.Cookie = cookie;
        });

        return context.Response.StatusCode;
    }

    [Fact]
    public async Task AccountPosts_BelowLimit_AreNotRateLimited()
    {
        var (token, cookie) = await GetAntiforgeryAsync();
        var ip = IPAddress.Parse("192.0.2.41");

        for (var request = 0; request < 20; request++)
        {
            var status = await SendPostAsync("/Account/Logout", ip, token, cookie);
            Assert.NotEqual(StatusCodes.Status429TooManyRequests, status);
        }
    }

    [Fact]
    public async Task AccountPosts_AboveLimit_ReturnHttp429()
    {
        var (token, cookie) = await GetAntiforgeryAsync();
        var ip = IPAddress.Parse("192.0.2.42");

        for (var request = 0; request < 20; request++)
            _ = await SendPostAsync("/Account/Logout", ip, token, cookie);

        var status = await SendPostAsync("/Account/Logout", ip, token, cookie);
        Assert.Equal(StatusCodes.Status429TooManyRequests, status);
    }

    [Fact]
    public async Task AccountPosts_FromDifferentIps_HaveIndependentLimits()
    {
        var (token, cookie) = await GetAntiforgeryAsync();
        var firstIp = IPAddress.Parse("192.0.2.43");
        var secondIp = IPAddress.Parse("192.0.2.44");

        for (var request = 0; request < 20; request++)
            _ = await SendPostAsync("/Account/Logout", firstIp, token, cookie);

        var secondIpStatus = await SendPostAsync("/Account/Logout", secondIp, token, cookie);
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, secondIpStatus);
    }

    [Fact]
    public async Task OrdinaryShoppingPosts_AreNotRateLimited()
    {
        var ip = IPAddress.Parse("192.0.2.45");
        var context = await app.Factory.Server.SendAsync(httpContext =>
        {
            httpContext.Connection.RemoteIpAddress = ip;
            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.Path = "/not-a-real-shopping-post";
        });

        // The ordinary route may return 404, but it must not be rejected by the limiter.
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
    }
}
