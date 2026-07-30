import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ApiError, api, formatClock, incidentQueries, shortId, type IncidentSummary } from '../lib/api'

const urgency = (incident: IncidentSummary) =>
  incident.verificationStatus === 'AwaitingConfirmation' ? 'urgent' : incident.unresolvedConflictCount ? 'attention' : 'steady'

export function OperationsHome() {
  const query = useQuery({
    queryKey: incidentQueries.all(),
    queryFn: () => api<IncidentSummary[]>('/api/incidents'),
    refetchInterval: 15_000,
  })
  const incidents = query.data ?? []

  return (
    <section className="operations-home" aria-labelledby="operations-heading">
      <header className="workspace-heading">
        <div>
          <p className="eyebrow">Live incident queue</p>
          <h1 id="operations-heading">See the next action, not the noise.</h1>
          <p className="workspace-heading__support">Singletons first, then conflicts and active response states. All evidence remains source-linked.</p>
        </div>
        <div className="queue-count" aria-label={`${incidents.length} active incidents`}>
          <strong>{String(incidents.length).padStart(2, '0')}</strong><span>active</span>
        </div>
      </header>

      {query.isLoading && <QueueSkeleton />}
      {query.error instanceof ApiError && query.error.status === 401 && <SessionRequired />}
      {query.error && !(query.error instanceof ApiError && query.error.status === 401) && (
        <ErrorState retry={() => void query.refetch()} />
      )}
      {query.isSuccess && incidents.length === 0 && <EmptyQueue />}
      {incidents.length > 0 && (
        <div className="incident-queue" aria-live="polite">
          <div className="incident-queue__labels" aria-hidden="true">
            <span>Incident</span><span>Evidence</span><span>State</span><span>Received</span><span />
          </div>
          {incidents.map((incident) => (
            <Link
              key={incident.id}
              to="/incidents/$incidentId"
              params={{ incidentId: incident.id }}
              className={`incident-row incident-row--${urgency(incident)}`}
            >
              <span className="incident-row__identity"><i aria-hidden="true" /><strong>FT-{shortId(incident.id)}</strong><small>v{incident.version}</small></span>
              <span><strong>{incident.sourceCount} source{incident.sourceCount === 1 ? '' : 's'}</strong><small>{incident.unresolvedConflictCount ? `${incident.unresolvedConflictCount} unresolved` : 'Claims aligned'}</small></span>
              <span><strong>{splitStatus(incident.dispatchStatus)}</strong><small>{splitStatus(incident.verificationStatus)}</small></span>
              <span><strong>{formatClock(incident.createdAtUtc)}</strong><small>{new Date(incident.createdAtUtc).toLocaleDateString('en-NG', { day: '2-digit', month: 'short' })}</small></span>
              <span className="incident-row__arrow" aria-hidden="true">↗</span>
            </Link>
          ))}
        </div>
      )}
    </section>
  )
}

function splitStatus(value: string) { return value.replace(/([a-z])([A-Z])/g, '$1 $2') }

function QueueSkeleton() { return <div className="queue-skeleton" aria-label="Loading incidents"><span /><span /><span /></div> }

function EmptyQueue() {
  return <div className="empty-workspace"><div className="empty-workspace__signal" aria-hidden="true"><span /><span /><span /></div><div className="empty-workspace__copy"><p className="eyebrow">Intake state</p><h2>Listening, with no active report.</h2><p>New reports will arrive here with elapsed time, source confidence, privacy state, and the next dispatcher action.</p></div></div>
}

function SessionRequired() {
  return <div className="state-panel"><p className="eyebrow">Session required</p><h2>Your operational session is not active.</h2><p>Sign in with your invited account and authenticator before incident data is shown.</p><Link className="action action--primary" to="/login">Sign in securely</Link></div>
}

function ErrorState({ retry }: { retry: () => void }) {
  return <div className="state-panel"><p className="eyebrow">REST unavailable</p><h2>The queue could not be refreshed.</h2><p>No operational state was changed. Retry when the connection is stable.</p><button className="action" type="button" onClick={retry}>Retry queue</button></div>
}
