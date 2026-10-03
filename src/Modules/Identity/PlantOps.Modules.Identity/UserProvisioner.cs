using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PlantOps.Modules.Identity;

// Just-in-time provisioning: every successful sign-in refreshes the local user row (design 04, Decision 5).
internal sealed class UserProvisioner(IdentityDbContext db, TimeProvider time, ILogger<UserProvisioner> logger)
{
    public async Task UpsertAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        var id = principal?.FindFirstValue(Claims.Subject);
        if (principal is null || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var name = principal.FindFirstValue(Claims.Name) ?? id;
        var email = principal.FindFirstValue(Claims.Email);
        var roles = principal.FindAll(Claims.Roles).Select(c => c.Value);
        var now = time.GetUtcNow();

        try
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null)
            {
                db.Users.Add(User.Provision(id, name, email, roles, now));
            }
            else
            {
                user.RecordLogin(name, email, roles, now);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            // The directory is a convenience for assignment lists; the IdP already authenticated the person, so a
            // database hiccup (or two tabs signing in at once, racing on the primary key) must not block sign-in.
            logger.LogError(ex, "Could not record sign-in of user {UserId} in the identity directory", id);
        }
    }
}
