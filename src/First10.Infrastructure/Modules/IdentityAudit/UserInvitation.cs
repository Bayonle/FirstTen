namespace First10.Infrastructure.Modules.IdentityAudit;

public sealed class UserInvitation
{
    private UserInvitation()
    {
    }

    private UserInvitation(
        Guid id,
        Guid userId,
        string role,
        string purpose,
        string tokenHash,
        string invitedBy,
        DateTimeOffset expiresAtUtc)
    {
        Id = id;
        UserId = userId;
        Role = role;
        Purpose = purpose;
        TokenHash = tokenHash;
        InvitedBy = invitedBy;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Role { get; private set; } = string.Empty;

    public string Purpose { get; private set; } = string.Empty;

    public string TokenHash { get; private set; } = string.Empty;

    public string InvitedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RedeemedAtUtc { get; private set; }

    public bool CanRedeem(DateTimeOffset now) => RedeemedAtUtc is null && ExpiresAtUtc > now;

    public void Redeem(DateTimeOffset redeemedAtUtc) => RedeemedAtUtc = redeemedAtUtc;

    public static UserInvitation Create(
        Guid userId,
        string role,
        string purpose,
        string tokenHash,
        string invitedBy,
        DateTimeOffset expiresAtUtc) =>
        new(Guid.NewGuid(), userId, role, purpose, tokenHash, invitedBy, expiresAtUtc);
}

public static class InvitationPurposes
{
    public const string AccountEnrollment = "account_enrollment";
    public const string MfaRecovery = "mfa_recovery";
}

public sealed record CreatedInvitation(Guid InvitationId, string Token, DateTimeOffset ExpiresAtUtc);

public sealed record InvitationEnrollment(Guid UserId, string SharedKey);

public sealed record EnrollmentResult(bool Succeeded, IReadOnlyList<string> RecoveryCodes, IReadOnlyList<string> Errors)
{
    public static EnrollmentResult Failure(params string[] errors) => new(false, [], errors);

    public static EnrollmentResult Success(IReadOnlyList<string> recoveryCodes) => new(true, recoveryCodes, []);
}
