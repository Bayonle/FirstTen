import type { PropsWithChildren } from 'react'

const navigation = [
  { label: 'Incidents', status: 'Active' },
  { label: 'Dispatch', status: 'Queued' },
  { label: 'Guidance', status: 'Approved' },
  { label: 'Operations', status: 'Ready' },
]

export function AppShell({ children }: PropsWithChildren) {
  return (
    <div className="app-frame">
      <a className="skip-link" href="#main-content">
        Skip to operations
      </a>

      <header className="topbar">
        <div className="wordmark" aria-label="First10 operations">
          <span className="wordmark__index">10</span>
          <span className="wordmark__name">FirstTen</span>
        </div>
        <div className="pilot-context">
          <span className="pilot-context__label">Pilot corridor</span>
          <span>Berger–Mowe</span>
        </div>
        <div className="environment-badge" aria-label="Controlled test environment">
          <span aria-hidden="true" />
          Controlled test
        </div>
      </header>

      <div className="workspace-grid">
        <nav className="module-nav" aria-label="Product modules">
          <p className="module-nav__eyebrow">Operations</p>
          <ul>
            {navigation.map((item, index) => (
              <li key={item.label}>
                <button
                  className={index === 0 ? 'module-link module-link--active' : 'module-link'}
                  type="button"
                  aria-current={index === 0 ? 'page' : undefined}
                >
                  <span>{item.label}</span>
                  <small>{item.status}</small>
                </button>
              </li>
            ))}
          </ul>
          <div className="module-nav__footer">
            <span className="signal-dot" aria-hidden="true" />
            <span>API + worker connected</span>
          </div>
        </nav>

        <main id="main-content" className="main-workspace">
          {children}
        </main>
      </div>
    </div>
  )
}
