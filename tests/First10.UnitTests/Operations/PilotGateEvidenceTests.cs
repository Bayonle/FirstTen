using First10.Modules.Operations;

namespace First10.UnitTests.Operations;

public sealed class PilotGateEvidenceTests
{
    [Fact]
    public void EvidenceIsAppendOnlyAndExplicitlyRevocable()
    {
        var now = DateTimeOffset.UtcNow;
        var approval = PilotGateEvidence.Record(
            Guid.NewGuid(),
            PilotGateEvidenceType.FrscApproval,
            PilotGateEvidenceDecision.Approved,
            "evidence://frsc/signed-approval",
            "administrator-1",
            now);
        var revocation = PilotGateEvidence.Record(
            Guid.NewGuid(),
            PilotGateEvidenceType.FrscApproval,
            PilotGateEvidenceDecision.Revoked,
            "evidence://frsc/revocation",
            "administrator-2",
            now.AddMinutes(1));

        Assert.Equal(PilotGateEvidenceDecision.Approved, approval.Decision);
        Assert.Equal(PilotGateEvidenceDecision.Revoked, revocation.Decision);
        Assert.NotEqual(approval.Id, revocation.Id);
    }
}
