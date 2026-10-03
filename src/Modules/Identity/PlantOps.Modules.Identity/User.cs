namespace PlantOps.Modules.Identity;

// A local copy of who the IdP says a person is, refreshed on every login. The IdP stays the source of truth;
// this exists so other modules can list and validate people without calling the IdP.
internal sealed class User
{
    public const int IdMaxLength = 200;
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = 320;
    public const int RolesMaxLength = 500;

    // For EF Core only.
    private User()
    {
    }

    // The IdP's "sub" claim.
    public string Id { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Email { get; private set; }

    // Roles live in one delimited column (",technician,supervisor,") instead of a UserRoles table: the user table is
    // hundreds of rows at most and is only ever read whole, so a join table would add a mapping, a delete+insert on
    // every login and an index nothing can use (LIKE '%,x,%' cannot seek anyway). The leading and trailing comma make
    // "technician" match as a whole word, never as a part of another role.
    public string RolesText { get; private set; } = ",";

    public DateTimeOffset LastSeenAt { get; private set; }

    public IReadOnlyList<string> Roles => RolesText.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public static string RoleToken(string role) => $",{role},";

    public static User Provision(string id, string name, string? email, IEnumerable<string> roles, DateTimeOffset now)
    {
        var user = new User { Id = id };
        user.RecordLogin(name, email, roles, now);
        return user;
    }

    public void RecordLogin(string name, string? email, IEnumerable<string> roles, DateTimeOffset now)
    {
        Name = Truncate(string.IsNullOrWhiteSpace(name) ? Id : name, NameMaxLength);
        Email = string.IsNullOrWhiteSpace(email) ? null : Truncate(email, EmailMaxLength);
        // A role containing the delimiter would corrupt the encoding; the IdP's roles never do, so drop rather than throw.
        var clean = roles.Where(r => !string.IsNullOrWhiteSpace(r) && !r.Contains(',')).Distinct().Order().ToArray();
        RolesText = Truncate(clean.Length == 0 ? "," : $",{string.Join(',', clean)},", RolesMaxLength);
        LastSeenAt = now;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
