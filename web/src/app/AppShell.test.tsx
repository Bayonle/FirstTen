import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AppShell } from './AppShell'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...props }: React.PropsWithChildren<{ to: string }>) => <a href={to} {...props}>{children}</a>,
  useRouterState: () => '/',
}))

vi.mock('./LiveUpdates', () => ({ LiveUpdates: () => <span>Live updates</span> }))

describe('AppShell', () => {
  it('orients operators to the pilot and its product modules', () => {
    render(
      <AppShell>
        <h1>Test workspace</h1>
      </AppShell>,
    )

    expect(screen.getByLabelText('First10 operations')).toBeInTheDocument()
    expect(screen.getByText('Berger–Mowe')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Product modules' })).toBeInTheDocument()
    expect(screen.getByRole('main')).toHaveTextContent('Test workspace')
  })
})
