namespace First10.Modules.Dispatch;

public enum DispatchStatus
{
    AwaitingVerification = 1,
    Verified = 2,
    Dispatched = 3,
    Arrived = 4,
    Transported = 5,
    Closed = 6,
    Reopened = 7
}

public sealed class IncidentDispatch
{
    private IncidentDispatch()
    {
    }

    private IncidentDispatch(Guid incidentId, DateTimeOffset createdAtUtc)
    {
        IncidentId = incidentId;
        Status = DispatchStatus.AwaitingVerification;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid IncidentId { get; private set; }
    public DispatchStatus Status { get; private set; }
    public int Version { get; private set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public string? LastReopenReason { get; private set; }

    public static IncidentDispatch Create(Guid incidentId, DateTimeOffset createdAtUtc)
    {
        if (incidentId == Guid.Empty)
        {
            throw new ArgumentException("Incident ID is required.", nameof(incidentId));
        }

        return new IncidentDispatch(incidentId, createdAtUtc);
    }

    public DispatchTransitionResult TryTransition(
        DispatchStatus target,
        bool isAuthorized,
        string? reason,
        int expectedVersion,
        DateTimeOffset occurredAtUtc)
    {
        if (!isAuthorized)
        {
            return DispatchTransitionResult.Unauthorized;
        }

        if (expectedVersion != Version)
        {
            return DispatchTransitionResult.Stale;
        }

        if (target == Status)
        {
            return DispatchTransitionResult.Duplicate;
        }

        if (!IsAllowed(Status, target))
        {
            return DispatchTransitionResult.Invalid;
        }

        if (target == DispatchStatus.Reopened && string.IsNullOrWhiteSpace(reason))
        {
            return DispatchTransitionResult.ReasonRequired;
        }

        Status = target;
        UpdatedAtUtc = occurredAtUtc;
        LastReopenReason = target == DispatchStatus.Reopened ? reason!.Trim() : LastReopenReason;
        Version++;
        return DispatchTransitionResult.Applied;
    }

    private static bool IsAllowed(DispatchStatus current, DispatchStatus target) =>
        (current, target) switch
        {
            (DispatchStatus.AwaitingVerification, DispatchStatus.Verified) => true,
            (DispatchStatus.Verified, DispatchStatus.Dispatched) => true,
            (DispatchStatus.Reopened, DispatchStatus.Dispatched) => true,
            (DispatchStatus.Dispatched, DispatchStatus.Arrived) => true,
            (DispatchStatus.Arrived, DispatchStatus.Transported) => true,
            (DispatchStatus.Arrived, DispatchStatus.Closed) => true,
            (DispatchStatus.Transported, DispatchStatus.Closed) => true,
            (DispatchStatus.Closed, DispatchStatus.Reopened) => true,
            _ => false
        };
}

public enum DispatchTransitionResult
{
    Applied = 1,
    Duplicate = 2,
    Stale = 3,
    Unauthorized = 4,
    Invalid = 5,
    ReasonRequired = 6,
    IncidentNotFound = 7
}

public sealed class DispatchTransition
{
    private DispatchTransition()
    {
    }

    public Guid Id { get; private set; }
    public Guid IncidentId { get; private set; }
    public DispatchStatus FromStatus { get; private set; }
    public DispatchStatus ToStatus { get; private set; }
    public string DispatcherId { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public int ResultingVersion { get; private set; }

    public static DispatchTransition Create(
        Guid id,
        Guid incidentId,
        DispatchStatus from,
        DispatchStatus to,
        string dispatcherId,
        string? reason,
        DateTimeOffset occurredAtUtc,
        int resultingVersion)
    {
        if (id == Guid.Empty || incidentId == Guid.Empty)
        {
            throw new ArgumentException("Transition and incident IDs are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(dispatcherId);
        return new DispatchTransition
        {
            Id = id,
            IncidentId = incidentId,
            FromStatus = from,
            ToStatus = to,
            DispatcherId = dispatcherId.Trim(),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            OccurredAtUtc = occurredAtUtc,
            ResultingVersion = resultingVersion
        };
    }
}

public sealed record TransitionIncidentDispatch(
    Guid TransitionId,
    Guid IncidentId,
    DispatchStatus TargetStatus,
    int ExpectedVersion,
    string DispatcherId,
    string DispatcherRole,
    string? Reason = null);
