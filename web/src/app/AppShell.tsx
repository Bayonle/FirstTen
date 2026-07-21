import { Link, useRouterState } from '@tanstack/react-router'
import type { PropsWithChildren } from 'react'
import { LiveUpdates } from './LiveUpdates'

const navigation = [
  { label: 'Incidents', to: '/', hint: 'Live queue' },
  { label: 'Guidance', to: '/guidance', hint: 'Clinical' },
  { label: 'Recognition', to: '/recognition', hint: 'Private' },
  { label: 'Operations', to: '/operations', hint: 'Control' },
] as const

export function AppShell({ children }: PropsWithChildren) {
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  return (
    <div className="app-frame">
      <a className="skip-link" href="#main-content">Skip to operations</a>

      <header className="topbar">
        <Link className="wordmark" to="/" aria-label="First10 operations">
          <span className="wordmark__index">10</span>
          <span className="wordmark__name">FirstTen</span>
        </Link>
        <div className="pilot-context">
          <span className="pilot-context__label">Pilot corridor</span>
          <span>Berger–Mowe</span>
        </div>
        <div className="topbar__status">
          <LiveUpdates />
          <span className="environment-badge"><span aria-hidden="true" />Controlled test</span>
        </div>
      </header>

      <div className="workspace-grid">
        <nav className="module-nav" aria-label="Product modules">
          <p className="module-nav__eyebrow">Operations</p>
          <ul>
            {navigation.map((item) => {
              const active = item.to === '/' ? pathname === '/' || pathname.startsWith('/incidents/') : pathname.startsWith(item.to)
              return (
                <li key={item.label}>
                  <Link className={active ? 'module-link module-link--active' : 'module-link'} to={item.to} aria-current={active ? 'page' : undefined}>
                    <span>{item.label}</span><small>{item.hint}</small>
                  </Link>
                </li>
              )
            })}
          </ul>
          <div className="module-nav__footer">
            <span className="signal-dot" aria-hidden="true" />
            <span>API + worker topology<br />PostgreSQL outbox</span>
          </div>
        </nav>

        <main id="main-content" className="main-workspace">{children}</main>
      </div>
    </div>
  )
}
