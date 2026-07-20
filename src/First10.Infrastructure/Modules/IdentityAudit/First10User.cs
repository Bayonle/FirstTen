using Microsoft.AspNetCore.Identity;

namespace First10.Infrastructure.Modules.IdentityAudit;

public sealed class First10User : IdentityUser<Guid>
{
    public DateTimeOffset? InvitationAcceptedAtUtc { get; private set; }

    public DateTimeOffset? LastReauthenticatedAtUtc { get; private set; }

    public int SessionVersion { get; private set; } = 1;

    public static First10User CreateInvited(string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        Email = email,
        EmailConfirmed = true
    };

    public void AcceptInvitation(DateTimeOffset acceptedAtUtc)
    {
        InvitationAcceptedAtUtc = acceptedAtUtc;
        LastReauthenticatedAtUtc = acceptedAtUtc;
    }

    public void MarkReauthenticated(DateTimeOffset occurredAtUtc) =>
        LastReauthenticatedAtUtc = occurredAtUtc;

    public void RevokeSessions() => SessionVersion++;
}

public static class SessionVersionValidator
{
    public static bool IsCurrent(First10User user, int issuedVersion) =>
        user.SessionVersion == issuedVersion && user.InvitationAcceptedAtUtc.HasValue;
}
