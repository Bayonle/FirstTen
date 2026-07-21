import { useQuery } from '@tanstack/react-query'
import { api } from '../lib/api'

type Health = { databaseAvailable: boolean; auditChainValid: boolean; openManualReviewAlerts: number; failedOrUnknownDeliveries: number; evaluatedAtUtc: string }
type Activation = { isOpen: boolean; blockingReasons: string[]; evaluatedAtUtc: string }

export function SystemOperations() {
  const health = useQuery({ queryKey: ['operations-health'], queryFn: () => api<Health>('/api/operations/health'), refetchInterval: 30_000 })
  const activation = useQuery({ queryKey: ['activation'], queryFn: () => api<Activation>('/api/operations/activation') })
  return <section className="secondary-workspace"><header className="workspace-heading"><div><p className="eyebrow">Operational control</p><h1>Fail closed. Know why.</h1><p className="workspace-heading__support">Public traffic stays blocked until every partner, privacy, AI, retention, and guidance approval is present.</p></div><span className={`gate-state ${activation.data?.isOpen ? 'gate-state--open' : ''}`}>{activation.data?.isOpen ? 'Gate open' : 'Gate closed'}</span></header>
    <div className="operations-grid"><section><p className="eyebrow">Runtime health</p><h2>API + worker dependencies</h2><dl className="health-list"><HealthRow label="Database" ok={health.data?.databaseAvailable} /><HealthRow label="Audit chain" ok={health.data?.auditChainValid} /><div><dt>Manual review alerts</dt><dd>{health.data?.openManualReviewAlerts ?? '—'}</dd></div><div><dt>Delivery exceptions</dt><dd>{health.data?.failedOrUnknownDeliveries ?? '—'}</dd></div></dl></section><section><p className="eyebrow">Activation blockers</p><h2>{activation.data?.blockingReasons.length ?? 0} controls outstanding</h2><ol className="blocker-list">{activation.data?.blockingReasons.map((reason) => <li key={reason}>{reason.replaceAll('_', ' ')}</li>)}</ol></section></div>
  </section>
}

function HealthRow({ label, ok }: { label: string; ok?: boolean }) { return <div><dt>{label}</dt><dd className={ok ? 'health-ok' : 'health-bad'}>{ok ? 'Healthy' : 'Unavailable'}</dd></div> }
