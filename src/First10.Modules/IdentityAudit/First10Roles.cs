namespace First10.Modules.IdentityAudit;

public static class First10Roles
{
    public const string Dispatcher = "Dispatcher";
    public const string Administrator = "Administrator";
    public const string ClinicalApprover = "ClinicalApprover";

    public static IReadOnlyList<string> All { get; } =
        [Dispatcher, Administrator, ClinicalApprover];
}

public static class First10Permissions
{
    public const string DispatchIncident = "incidents.dispatch";
    public const string InviteUser = "identity.invite";
    public const string ChangeRetention = "operations.retention.change";
    public const string ViewAdministrativeAudit = "audit.administrative.view";
    public const string ApproveClinicalTemplate = "guidance.clinical.approve";
}

public static class First10RolePermissions
{
    private static readonly Dictionary<string, IReadOnlySet<string>> Matrix =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [First10Roles.Dispatcher] = new HashSet<string>(StringComparer.Ordinal)
            {
                First10Permissions.DispatchIncident
            },
            [First10Roles.Administrator] = new HashSet<string>(StringComparer.Ordinal)
            {
                First10Permissions.InviteUser,
                First10Permissions.ChangeRetention,
                First10Permissions.ViewAdministrativeAudit
            },
            [First10Roles.ClinicalApprover] = new HashSet<string>(StringComparer.Ordinal)
            {
                First10Permissions.ApproveClinicalTemplate
            }
        };

    public static bool Allows(string role, string permission) =>
        Matrix.TryGetValue(role, out var permissions) && permissions.Contains(permission);
}
