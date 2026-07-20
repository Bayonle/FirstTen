const readiness = [
  ['Channel', 'Telegram test adapter pending'],
  ['Privacy', 'In-memory redaction pending'],
  ['Triage', 'GPT evaluation pending'],
]

export function OperationsHome() {
  return (
    <section className="operations-home" aria-labelledby="operations-heading">
      <header className="workspace-heading">
        <div>
          <p className="eyebrow">Live incident queue</p>
          <h1 id="operations-heading">No active incidents</h1>
          <p className="workspace-heading__support">
            The controlled environment is waiting for authenticated channel events.
          </p>
        </div>
        <div className="readiness-stamp" aria-label="Foundation status ready">
          <span>Foundation</span>
          <strong>Ready</strong>
        </div>
      </header>

      <div className="empty-workspace">
        <div className="empty-workspace__signal" aria-hidden="true">
          <span />
          <span />
          <span />
        </div>
        <div className="empty-workspace__copy">
          <p className="eyebrow">Intake state</p>
          <h2>Listening, with no report accepted yet.</h2>
          <p>
            New reports will appear here with elapsed time, location confidence,
            privacy state, and the next required dispatcher action.
          </p>
        </div>
      </div>

      <section className="readiness-list" aria-labelledby="readiness-heading">
        <div className="readiness-list__heading">
          <p className="eyebrow">Build sequence</p>
          <h2 id="readiness-heading">Next operational gates</h2>
        </div>
        <ol>
          {readiness.map(([label, detail], index) => (
            <li key={label}>
              <span className="readiness-list__number">0{index + 1}</span>
              <span className="readiness-list__label">{label}</span>
              <span className="readiness-list__detail">{detail}</span>
            </li>
          ))}
        </ol>
      </section>
    </section>
  )
}
