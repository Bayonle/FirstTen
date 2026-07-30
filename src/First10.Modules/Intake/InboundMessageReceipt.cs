namespace First10.Modules.Intake;

public sealed class InboundMessageReceipt
{
    private InboundMessageReceipt()
    {
    }

    private InboundMessageReceipt(
        Guid id,
        string scope,
        string key,
        Guid downstreamCommandId,
        DateTimeOffset acceptedAtUtc)
    {
        Id = id;
        Scope = scope;
        Key = key;
        DownstreamCommandId = downstreamCommandId;
        AcceptedAtUtc = acceptedAtUtc;
    }

    public Guid Id { get; private set; }

    public string Scope { get; private set; } = string.Empty;

    public string Key { get; private set; } = string.Empty;

    public Guid DownstreamCommandId { get; private set; }

    public DateTimeOffset AcceptedAtUtc { get; private set; }

    public static InboundMessageReceipt Create(
        string scope,
        string key,
        Guid downstreamCommandId,
        DateTimeOffset acceptedAtUtc) =>
        new(Guid.NewGuid(), scope, key, downstreamCommandId, acceptedAtUtc);
}
