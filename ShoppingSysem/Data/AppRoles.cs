namespace ShoppingSysem.Data;

/// <summary>
/// Canonical application role names. Use these instead of string literals
/// so role checks stay consistent across pages and services.
/// </summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Shopkeeper = "Shopkeeper";
    public const string Customer = "Customer";

    public static readonly string[] All = [Admin, Shopkeeper, Customer];
}
