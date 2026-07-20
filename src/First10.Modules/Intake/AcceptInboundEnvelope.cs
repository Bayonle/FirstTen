namespace First10.Modules.Intake;

public sealed record AcceptInboundEnvelope(Guid WorkId, InboundChannelEnvelope Envelope);
