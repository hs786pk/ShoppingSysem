using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ShoppingSysem.Data;

/// <summary>
/// Design-time factory so EF tools can create ApplicationDbContext without
/// running the full web host (which may fail if optional config is incomplete).
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        // Prefer the project directory when tools run from the solution root.
        if (!File.Exists(Path.Combine(basePath, "appsettings.json")))
        {
            var projectPath = Path.Combine(basePath, "ShoppingSysem");
            if (File.Exists(Path.Combine(projectPath, "appsettings.json")))
                basePath = projectPath;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? "Data Source=(localdb)\\MSSQLLocalDB;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}
