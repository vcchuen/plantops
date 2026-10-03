using Microsoft.EntityFrameworkCore;
using PlantOps.Modules.Identity.Contracts;

namespace PlantOps.Modules.Identity;

internal sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public async Task<UserSummary?> FindAsync(string id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null ? null : ToSummary(user);
    }

    public async Task<IReadOnlyList<UserSummary>> ListByRoleAsync(string role, CancellationToken cancellationToken)
    {
        // A role holding the delimiter could match across two roles; no real role does, so answer "nobody".
        if (string.IsNullOrWhiteSpace(role) || role.Contains(','))
        {
            return [];
        }

        var token = User.RoleToken(role.Trim());
        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.RolesText.Contains(token))
            .OrderBy(u => u.Name)
            .ToListAsync(cancellationToken);
        return users.Select(ToSummary).ToList();
    }

    private static UserSummary ToSummary(User user) => new(user.Id, user.Name, user.Email, user.Roles);
}
