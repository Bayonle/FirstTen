using First10.Modules.IdentityAudit;

namespace First10.UnitTests.IdentityAudit;

public sealed class AuthorizationPolicyTests
{
    [Theory]
    [InlineData(First10Roles.Dispatcher, First10Permissions.DispatchIncident, true)]
    [InlineData(First10Roles.Dispatcher, First10Permissions.InviteUser, false)]
    [InlineData(First10Roles.Dispatcher, First10Permissions.ChangeRetention, false)]
    [InlineData(First10Roles.Administrator, First10Permissions.InviteUser, true)]
    [InlineData(First10Roles.Administrator, First10Permissions.ViewAdministrativeAudit, true)]
    [InlineData(First10Roles.Administrator, First10Permissions.DispatchIncident, false)]
    [InlineData(First10Roles.ClinicalApprover, First10Permissions.ApproveClinicalTemplate, true)]
    [InlineData(First10Roles.ClinicalApprover, First10Permissions.DispatchIncident, false)]
    public void RoleMatrixEnforcesSeparationOfDuties(string role, string permission, bool expected)
    {
        Assert.Equal(expected, First10RolePermissions.Allows(role, permission));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("totpSeed")]
    [InlineData("recoveryCode")]
    [InlineData("sessionCookie")]
    [InlineData("reporterPhone")]
    public void AuditPayloadRejectsSensitiveFields(string fieldName)
    {
        Assert.Throws<ArgumentException>(() => AuditPayload.Create(new Dictionary<string, object?>
        {
            [fieldName] = "must-not-be-recorded"
        }));
    }

    [Fact]
    public void AuditPayloadAcceptsMinimizedOperationalFields()
    {
        var payload = AuditPayload.Create(new Dictionary<string, object?>
        {
            ["role"] = First10Roles.Dispatcher,
            ["result"] = "invited"
        });

        Assert.Contains("Dispatcher", payload.Json, StringComparison.Ordinal);
    }
}
