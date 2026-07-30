using First10.Modules.Intake;

namespace First10.Infrastructure.Modules.Intake.Channels;

public sealed record ProviderInboundMessage(
    IntakeChannel Channel,
    string ProviderMessageId,
    string ProviderAddress,
    string CorrelationKey,
    IntakeContentKind ContentKind,
    string? ProviderMediaHandle,
    DateTimeOffset OccurredAtUtc,
    IntakeLocation? Location);

public interface IChannelInboundAdapter
{
    IReadOnlyList<ProviderInboundMessage> Parse(ReadOnlyMemory<byte> payload);
}

public sealed record ContactIdentity(Guid ContactReference, string ReporterKey);

public interface IContactIdentityResolver
{
    Task<ContactIdentity> ResolveAsync(
        IntakeChannel channel,
        string providerAddress,
        CancellationToken cancellationToken = default);
}

public sealed class ChannelEnvelopeMapper(IContactIdentityResolver identities)
{
    public async Task<InboundChannelEnvelope> MapAsync(
        ProviderInboundMessage message,
        CancellationToken cancellationToken = default)
    {
        var identity = await identities.ResolveAsync(
            message.Channel,
            message.ProviderAddress,
            cancellationToken);
        return new InboundChannelEnvelope
        {
            SchemaVersion = 1,
            Channel = message.Channel,
            ProviderMessageId = message.ProviderMessageId,
            ReporterKey = identity.ReporterKey,
            ContactReference = identity.ContactReference,
            ContentKind = message.ContentKind,
            ProviderMediaHandle = message.ProviderMediaHandle,
            OccurredAtUtc = message.OccurredAtUtc,
            Location = message.Location,
            CorrelationKey = identity.ReporterKey
        };
    }
}
