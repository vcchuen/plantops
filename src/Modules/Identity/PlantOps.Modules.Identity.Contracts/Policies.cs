namespace PlantOps.Modules.Identity.Contracts;

/// <summary>Authorization policy names. The Identity module decides who holds each one; endpoints only name what they need.</summary>
public static class Policies
{
    /// <summary>Register, edit, relocate, re-rate and decommission assets. Held by <see cref="Roles.Supervisor"/> and <see cref="Roles.Admin"/>.</summary>
    public const string ManageAssets = "assets:manage";

    /// <summary>Approve, reject, assign, close and cancel work orders. Held by <see cref="Roles.Supervisor"/> and <see cref="Roles.Admin"/>.</summary>
    public const string SuperviseWorkOrders = "workorders:supervise";
}
