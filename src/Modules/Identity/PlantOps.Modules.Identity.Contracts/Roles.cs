namespace PlantOps.Modules.Identity.Contracts;

/// <summary>
/// Role values as they appear in the <c>roles</c> claim. Lower-case on purpose: they must match the Keycloak
/// realm roles and the Entra ID app-role values exactly (design 03, Decision 4).
/// </summary>
public static class Roles
{
    public const string Operator = "operator";
    public const string Technician = "technician";
    public const string Supervisor = "supervisor";
    public const string Admin = "admin";
}
