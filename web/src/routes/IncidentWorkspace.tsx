import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import { api, formatClock, incidentQueries, shortId, type IncidentDetail, type TimelineEvent } from '../lib/api'

export function IncidentWorkspace() {
  const { incidentId } = useParams({ from: '/incidents/$incidentId' })
  const queryClient = useQueryClient()
  const detail = useQuery({ queryKey: incidentQueries.detail(incidentId), queryFn: () => api<IncidentDetail>(`/api/incidents/${incidentId}`) })
  const timeline = useQuery({ queryKey: incidentQueries.timeline(incidentId), queryFn: () => api<TimelineEvent[]>(`/api/incidents/${incidentId}/timeline`) })
  const [note, setNote] = useState('')

  const refresh = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: incidentQueries.all() }),
      queryClient.invalidateQueries({ queryKey: incidentQueries.detail(incidentId) }),
      queryClient.invalidateQueries({ queryKey: incidentQueries.timeline(incidentId) }),
    ])
  }
  const command = useMutation({
    mutationFn: ({ path, body }: { path: string; body: unknown }) => api(path, { method: 'POST', body: JSON.stringify(body) }),
    onSuccess: refresh,
  })

  if (detail.isLoading) return <div className="workspace-loading">Loading incident evidence…</div>
  if (!detail.data) return <div className="state-panel"><h1>Incident unavailable</h1><Link to="/">Return to queue</Link></div>
  const incident = detail.data
  const dispatchVersion = incident.dispatch?.version ?? 1

  const review = (decision: 'Verify' | 'Reject' | 'KeepOpen') => command.mutate({
    path: `/api/incidents/${incidentId}/review`,
    body: { decisionId: crypto.randomUUID(), decision, expectedVersion: incident.version, reason: decision === 'Reject' ? 'Dispatcher rejected after evidence review' : null, reviewedIncidentLga: null },
  })
  const transition = (targetStatus: string) => command.mutate({
    path: `/api/incidents/${incidentId}/dispatch`,
    body: { transitionId: crypto.randomUUID(), targetStatus, expectedVersion: dispatchVersion, reason: targetStatus === 'Reopened' ? 'New source-linked evidence received' : null },
  })

  return (
    <article className="incident-workspace">
      <header className="incident-header">
        <div><Link className="back-link" to="/">← Active queue</Link><p className="eyebrow">Incident FT-{shortId(incident.id)} · v{incident.version}</p><h1>{incident.sources[0]?.incidentType.replace(/([a-z])([A-Z])/g, '$1 $2') ?? 'Manual review incident'}</h1><p>{incident.sources[0]?.locationDescription ?? 'No reviewed location yet'} · {incident.sources[0]?.direction.replace(/([a-z])([A-Z])/g, '$1 $2') ?? 'Direction unknown'}</p></div>
        <div className="incident-header__state"><span>{incident.verificationStatus.replace(/([a-z])([A-Z])/g, '$1 $2')}</span><strong>{incident.dispatch?.status ?? 'Awaiting verification'}</strong></div>
      </header>

      {command.error && <div className="command-error" role="alert">{command.error.message} Refresh the incident before trying again.</div>}

      <div className="incident-layout">
        <div className="incident-primary">
          {incident.verificationStatus === 'AwaitingConfirmation' && (
            <section className="decision-strip" aria-labelledby="verification-title"><div><p className="eyebrow">Required action</p><h2 id="verification-title">Verify this operational incident?</h2><p>Review every source and unresolved contradiction first.</p></div><div className="decision-strip__actions"><button onClick={() => review('Verify')} disabled={command.isPending}>Verify</button><button onClick={() => review('KeepOpen')} disabled={command.isPending}>Keep open</button><button className="danger" onClick={() => review('Reject')} disabled={command.isPending}>Reject</button></div></section>
          )}

          <section className="source-section" aria-labelledby="sources-title"><div className="section-title"><p className="eyebrow">Evidence ledger</p><h2 id="sources-title">Source claims</h2><span>{incident.sources.length} independent report{incident.sources.length === 1 ? '' : 's'}</span></div><div className="source-ledger">{incident.sources.map((source, index) => <div className="source-claim" key={source.reportId}><div className="source-claim__index">0{index + 1}</div><div><strong>{source.incidentType.replace(/([a-z])([A-Z])/g, '$1 $2')}</strong><span>{source.severity} severity · casualties {source.casualtyMinimum ?? '?'}–{source.casualtyMaximum ?? '?'}</span></div><div><strong>{source.locationDescription ?? 'Location missing'}</strong><span>{source.locationConfidence ? `${Math.round(source.locationConfidence * 100)}% location confidence` : 'Unresolved location'}</span></div><div><strong>{formatClock(source.receivedAtUtc)}</strong><span>{source.evidenceReferences.length} evidence refs</span></div></div>)}</div></section>

          {incident.conflicts.length > 0 && <section className="conflict-section" aria-labelledby="conflicts-title"><div className="section-title"><p className="eyebrow">Contradictions stay visible</p><h2 id="conflicts-title">Claim conflicts</h2></div>{incident.conflicts.map((conflict) => <div className={`conflict-row ${conflict.isResolved ? 'conflict-row--resolved' : ''}`} key={conflict.id}><div><strong>{conflict.field.replace(/([a-z])([A-Z])/g, '$1 $2')}</strong><span>{conflict.isResolved ? 'Resolved' : 'Needs dispatcher selection'}</span></div><div className="conflict-values"><span>{conflict.leftValue}</span><i>vs</i><span>{conflict.rightValue}</span></div>{!conflict.isResolved && <button type="button" disabled={command.isPending} onClick={() => command.mutate({ path: `/api/incidents/${incidentId}/conflicts/${conflict.id}/resolve`, body: { selectedReportId: conflict.leftReportId, expectedVersion: incident.version } })}>Select first source</button>}</div>)}</section>}

          <section className="timeline-section" aria-labelledby="timeline-title"><div className="section-title"><p className="eyebrow">Immutable chronology</p><h2 id="timeline-title">Timeline</h2></div><ol className="timeline">{timeline.data?.map((event) => <li key={event.id}><time>{formatClock(event.occurredAtUtc)}</time><i aria-hidden="true" /><div><strong>{event.type.replaceAll('-', ' ')}</strong><span>{event.source}</span></div></li>)}</ol></section>
        </div>

        <aside className="incident-inspector" aria-label="Incident actions">
          <section><p className="eyebrow">Dispatch state</p><h2>{incident.dispatch?.status ?? 'Awaiting verification'}</h2><div className="state-sequence">{nextDispatchActions(incident.dispatch?.status).map((action) => <button key={action} type="button" disabled={command.isPending || incident.verificationStatus === 'AwaitingConfirmation'} onClick={() => transition(action)}>{action}</button>)}</div></section>
          <section><p className="eyebrow">Reporter delivery</p><h2>{incident.guidance.length} guidance intents</h2>{incident.guidance.length === 0 ? <p>No incident status update has been generated.</p> : <ul className="delivery-list">{incident.guidance.map((item) => <li key={item.id}><span>{item.language} · {item.trigger}</span><strong className={`delivery-status delivery-status--${item.status.toLowerCase()}`}>{item.status}</strong></li>)}</ul>}</section>
          <form onSubmit={(event) => { event.preventDefault(); if (!note.trim()) return; command.mutate({ path: `/api/incidents/${incidentId}/notes`, body: { noteId: crypto.randomUUID(), expectedVersion: incident.version, text: note, sourceClaimReferences: [] } }, { onSuccess: () => setNote('') }) }}><label htmlFor="dispatcher-note"><span className="eyebrow">Append-only note</span><strong>Dispatcher context</strong></label><textarea id="dispatcher-note" value={note} onChange={(event) => setNote(event.target.value)} maxLength={2000} placeholder="Plain text; cite source claims when making an operational choice." /><button className="action action--primary" type="submit" disabled={command.isPending || !note.trim()}>Add to timeline</button></form>
        </aside>
      </div>
    </article>
  )
}

function nextDispatchActions(status?: string) {
  switch (status) {
    case 'Verified': return ['Dispatched']
    case 'Dispatched': return ['Arrived']
    case 'Arrived': return ['Transported', 'Closed']
    case 'Transported': return ['Closed']
    case 'Closed': return ['Reopened']
    case 'Reopened': return ['Dispatched']
    default: return ['Verified']
  }
}
