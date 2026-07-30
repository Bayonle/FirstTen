using System.Diagnostics.Metrics;

namespace First10.Infrastructure.Modules.Operations;

public sealed class PilotMetrics : IDisposable
{
    public const string MeterName = "First10.Pilot";
    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _webhooks;
    private readonly Counter<long> _manualFallbacks;
    private readonly Counter<long> _deliveryExceptions;
    private readonly Histogram<double> _ticketReadySeconds;
    private readonly Histogram<double> _dispatchSeconds;

    public PilotMetrics()
    {
        _webhooks = _meter.CreateCounter<long>("first10.webhooks.accepted");
        _manualFallbacks = _meter.CreateCounter<long>("first10.triage.manual_fallbacks");
        _deliveryExceptions = _meter.CreateCounter<long>("first10.delivery.exceptions");
        _ticketReadySeconds = _meter.CreateHistogram<double>("first10.ticket.ready.seconds", "s");
        _dispatchSeconds = _meter.CreateHistogram<double>("first10.dispatch.seconds", "s");
    }

    public void WebhookAccepted(string channel) =>
        _webhooks.Add(1, new KeyValuePair<string, object?>("channel", channel));
    public void ManualFallback(string reason) =>
        _manualFallbacks.Add(1, new KeyValuePair<string, object?>("reason", reason));
    public void DeliveryException(string channel, string status) =>
        _deliveryExceptions.Add(1, new("channel", channel), new("status", status));
    public void TicketReady(TimeSpan duration) => _ticketReadySeconds.Record(duration.TotalSeconds);
    public void Dispatched(TimeSpan duration) => _dispatchSeconds.Record(duration.TotalSeconds);
    public void Dispose() => _meter.Dispose();
}
