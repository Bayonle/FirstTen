using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace First10.Modules.IdentityAudit;

public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    private AuditEvent(
        long sequence,
        DateTimeOffset occurredAtUtc,
        string action,
        string actorId,
        string payloadJson,
        string previousHash,
        string hash)
    {
        Sequence = sequence;
        OccurredAtUtc = occurredAtUtc;
        Action = action;
        ActorId = actorId;
        PayloadJson = payloadJson;
        PreviousHash = previousHash;
        Hash = hash;
    }

    public long Sequence { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string ActorId { get; private set; } = string.Empty;

    public string PayloadJson { get; private set; } = string.Empty;

    public string PreviousHash { get; private set; } = string.Empty;

    public string Hash { get; private set; } = string.Empty;

    public static AuditEvent Create(
        long sequence,
        DateTimeOffset occurredAtUtc,
        string action,
        string actorId,
        AuditPayload payload,
        string previousHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentNullException.ThrowIfNull(payload);

        var normalizedTimestamp = NormalizeTimestamp(occurredAtUtc);
        var hash = CalculateHash(sequence, normalizedTimestamp, action, actorId, payload.Json, previousHash);
        return new AuditEvent(sequence, normalizedTimestamp, action, actorId, payload.Json, previousHash, hash);
    }

    internal static string CalculateHash(
        long sequence,
        DateTimeOffset occurredAtUtc,
        string action,
        string actorId,
        string payloadJson,
        string previousHash)
    {
        var canonical = string.Join('\n',
            sequence.ToString(CultureInfo.InvariantCulture),
            occurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            action,
            actorId,
            payloadJson,
            previousHash);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}

public static class AuditChainVerifier
{
    public static bool IsValid(IEnumerable<AuditEvent> events)
    {
        var previousHash = string.Empty;
        long expectedSequence = 1;

        foreach (var auditEvent in events.OrderBy(x => x.Sequence))
        {
            if (auditEvent.Sequence != expectedSequence || auditEvent.PreviousHash != previousHash)
            {
                return false;
            }

            var expectedHash = AuditEvent.CalculateHash(
                auditEvent.Sequence,
                auditEvent.OccurredAtUtc,
                auditEvent.Action,
                auditEvent.ActorId,
                auditEvent.PayloadJson,
                auditEvent.PreviousHash);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedHash),
                    Convert.FromHexString(auditEvent.Hash)))
            {
                return false;
            }

            previousHash = auditEvent.Hash;
            expectedSequence++;
        }

        return true;
    }
}
