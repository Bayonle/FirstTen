using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using First10.Modules.Intake;
using First10.Modules.Intake.Media;
using First10.Infrastructure.Modules.Intake.Media;
using First10.Infrastructure.Modules.Intake;
using First10.Modules.Intake.Triage;
using First10.Modules.Incidents;
using First10.Modules.Dispatch;
using First10.Modules.Guidance;
using First10.Modules.BuildingBlocks.Contracts;
using First10.Modules.Recognition;
using System.Reflection;

namespace First10.Infrastructure.Messaging;

public static class WolverineConfiguration
{
    public static void Configure(
        WolverineOptions options,
        string connectionString,
        First10RuntimeRole role,
        Assembly? endpointAssembly = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        options.UsePostgresqlPersistenceAndTransport(
                connectionString,
                "wolverine",
                transportSchema: "wolverine_queues")
            .AutoProvision();
        options.UseEntityFrameworkCoreTransactions();
        options.Policies.AutoApplyTransactions();
        options.Discovery.IncludeAssembly(typeof(AcceptInboundEnvelopeHandler).Assembly);
        if (endpointAssembly is not null)
        {
            options.Discovery.IncludeAssembly(endpointAssembly);
        }
        options.PublishMessage<AcceptInboundEnvelope>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<RemindMissingLocation>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<ExpireGuidedSession>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<DeliverIntakePrompt>().ToPostgresqlQueue(First10Queues.Outbound);
        options.PublishMessage<ProcessIntakeMedia>().ToPostgresqlQueue(First10Queues.MediaAndAi);
        options.PublishMessage<MediaProcessingDegraded>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<DeleteExpiredMedia>().ToPostgresqlQueue(First10Queues.Maintenance);
        options.PublishMessage<EnforceTriageDeadline>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<TryTriageSession>().ToPostgresqlQueue(First10Queues.MediaAndAi);
        options.PublishMessage<CreateOrMatchIncident>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<ReviewSingletonIncident>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<ResolveIncidentConflict>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<ApplyLateIncidentLocation>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<AppendIncidentObservation>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<DecideSingletonIncident>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<InitialGuidanceRequested>().ToPostgresqlQueue(First10Queues.PriorityGuidance);
        options.PublishMessage<TransitionIncidentDispatch>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<DeliverGuidanceIntent>().ToPostgresqlQueue(First10Queues.Outbound);
        options.PublishMessage<ContributionDispatcherVerified>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<SetRecognitionConsent>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<AdjustRecognitionAward>().ToPostgresqlQueue(First10Queues.FastIntake);
        options.PublishMessage<IncidentChanged>().ToPostgresqlQueue(First10Queues.UiNotifications);

        if (role is First10RuntimeRole.Api)
        {
            options.ListenToPostgresqlQueue(First10Queues.UiNotifications);
            return;
        }

        options.ListenToPostgresqlQueue(First10Queues.FastIntake);
        options.ListenToPostgresqlQueue(First10Queues.MediaAndAi).MaximumParallelMessages(2);
        options.ListenToPostgresqlQueue(First10Queues.Outbound);
        options.ListenToPostgresqlQueue(First10Queues.PriorityGuidance).MaximumParallelMessages(4);
        options.ListenToPostgresqlQueue(First10Queues.Maintenance);
    }
}
