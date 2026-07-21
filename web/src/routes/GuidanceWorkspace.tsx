import { useQuery } from '@tanstack/react-query'
import { api } from '../lib/api'

type Template = { id: string; templateKey: string; purpose: string; category: string; severityBand: string; policyVersion: string; trigger: string; eligibilityContext: string; enabledAtUtc?: string; assets: Array<{ language: string; exactText: string; textSha256: string; voiceAssetKey: string }> }

export function GuidanceWorkspace() {
  const query = useQuery({ queryKey: ['guidance-templates'], queryFn: () => api<Template[]>('/api/guidance/templates') })
  return <section className="secondary-workspace"><header className="workspace-heading"><div><p className="eyebrow">Clinical policy surface</p><h1>Approved words, pinned voices.</h1><p className="workspace-heading__support">Dispatchers select policy triggers; only clinical approvers enable immutable three-language bundles.</p></div><div className="queue-count"><strong>{String(query.data?.filter((x) => x.enabledAtUtc).length ?? 0).padStart(2, '0')}</strong><span>enabled</span></div></header>
    <div className="policy-list">{query.isLoading && <p>Loading approved assets…</p>}{query.data?.map((template) => <article key={template.id}><header><div><p className="eyebrow">{template.purpose} · {template.severityBand}</p><h2>{template.templateKey}</h2></div><span className={template.enabledAtUtc ? 'policy-state policy-state--enabled' : 'policy-state'}>{template.enabledAtUtc ? 'Enabled' : 'Draft'}</span></header><dl><div><dt>Trigger</dt><dd>{template.trigger}</dd></div><div><dt>Category</dt><dd>{template.category}</dd></div><div><dt>Policy</dt><dd>{template.policyVersion}</dd></div></dl><p>{template.eligibilityContext}</p><div className="language-assets">{template.assets.map((asset) => <details key={asset.language}><summary><strong>{asset.language.replace(/([a-z])([A-Z])/g, '$1 $2')}</strong><span>{asset.voiceAssetKey}</span></summary><p>{asset.exactText}</p><code>{asset.textSha256.slice(0, 16)}…</code></details>)}</div></article>)}</div>
  </section>
}
