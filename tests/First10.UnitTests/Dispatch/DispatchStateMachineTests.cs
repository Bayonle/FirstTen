using First10.Modules.Dispatch;

namespace First10.UnitTests.Dispatch;

public sealed class DispatchStateMachineTests
{
    [Fact]
    public void GuardedLifecycleRequiresDispatcherAndExpectedVersion()
    {
        var dispatch = IncidentDispatch.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(DispatchTransitionResult.Unauthorized,
            dispatch.TryTransition(DispatchStatus.Verified, false, null, 1, DateTimeOffset.UtcNow));
        Assert.Equal(DispatchTransitionResult.Applied,
            dispatch.TryTransition(DispatchStatus.Verified, true, null, 1, DateTimeOffset.UtcNow));
        Assert.Equal(DispatchTransitionResult.Stale,
            dispatch.TryTransition(DispatchStatus.Dispatched, true, null, 1, DateTimeOffset.UtcNow));
        Assert.Equal(DispatchTransitionResult.Applied,
            dispatch.TryTransition(DispatchStatus.Dispatched, true, null, 2, DateTimeOffset.UtcNow));
        Assert.Equal(DispatchTransitionResult.Applied,
            dispatch.TryTransition(DispatchStatus.Arrived, true, null, 3, DateTimeOffset.UtcNow));
        Assert.Equal(DispatchTransitionResult.Applied,
            dispatch.TryTransition(DispatchStatus.Closed, true, null, 4, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ReopenIsClosedOnlyAndRequiresReason()
    {
        var dispatch = IncidentDispatch.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        dispatch.TryTransition(DispatchStatus.Verified, true, null, 1, DateTimeOffset.UtcNow);

        Assert.Equal(DispatchTransitionResult.Invalid,
            dispatch.TryTransition(DispatchStatus.Reopened, true, "new evidence", 2, DateTimeOffset.UtcNow));
        dispatch.TryTransition(DispatchStatus.Dispatched, true, null, 2, DateTimeOffset.UtcNow);
        dispatch.TryTransition(DispatchStatus.Arrived, true, null, 3, DateTimeOffset.UtcNow);
        dispatch.TryTransition(DispatchStatus.Closed, true, null, 4, DateTimeOffset.UtcNow);

        Assert.Equal(DispatchTransitionResult.ReasonRequired,
            dispatch.TryTransition(DispatchStatus.Reopened, true, null, 5, DateTimeOffset.UtcNow));
        Assert.Equal(DispatchTransitionResult.Applied,
            dispatch.TryTransition(DispatchStatus.Reopened, true, "caller reports renewed danger", 5, DateTimeOffset.UtcNow));
    }
}
