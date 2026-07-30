import { useQuery } from '@tanstack/react-query'
import { api } from '../lib/api'

type Aggregate = { lga: string; count: number }
type Reconciliation = { awardCount: number; distinctContributionCount: number; notificationCount: number; reconciled: boolean; serviceHoursEnabled: boolean; monetaryValueEnabled: boolean }

export function RecognitionWorkspace() {
  const aggregates = useQuery({ queryKey: ['recognition-aggregates'], queryFn: () => api<Aggregate[]>('/api/recognition/aggregates') })
  const reconciliation = useQuery({ queryKey: ['recognition-reconciliation'], queryFn: () => api<Reconciliation>('/api/recognition/reconciliation') })
  return <section className="secondary-workspace"><header className="workspace-heading"><div><p className="eyebrow">Privacy-preserving recognition</p><h1>Contribution without exposure.</h1><p className="workspace-heading__support">Only dispatcher-verified contributions count. Names, incidents, victims, rankings, money, and service hours stay out of the public view.</p></div></header>
    <div className="recognition-layout"><section><p className="eyebrow">Opted-in anonymous totals</p><h2>LGA recognition</h2>{aggregates.data?.length ? <ol className="aggregate-list">{aggregates.data.map((row) => <li key={row.lga}><span>{row.lga}</span><strong>{row.count}</strong></li>)}</ol> : <p className="quiet-copy">No reporter has opted into an anonymous LGA total.</p>}</section><aside><p className="eyebrow">Ledger reconciliation</p><div className={`reconcile-mark ${reconciliation.data?.reconciled ? 'reconcile-mark--ok' : ''}`}><strong>{reconciliation.data?.reconciled ? 'Reconciled' : 'Checking'}</strong><span>{reconciliation.data?.awardCount ?? 0} awards · {reconciliation.data?.notificationCount ?? 0} private notices</span></div><ul className="scope-guards"><li><span>Service hours</span><strong>Disabled in code</strong></li><li><span>Monetary value</span><strong>Disabled in code</strong></li><li><span>Public names</span><strong>Not collected</strong></li></ul></aside></div>
  </section>
}
