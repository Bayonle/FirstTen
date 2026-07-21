import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from './api'

afterEach(() => vi.unstubAllGlobals())

describe('operational API client', () => {
  it('adds antiforgery protection to commands', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ token: 'csrf-test' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ version: 2 }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)

    await api('/api/incidents/one/notes', { method: 'POST', body: JSON.stringify({ text: 'note' }) })

    expect(fetch).toHaveBeenCalledTimes(2)
    const command = fetch.mock.calls[1]?.[1] as RequestInit
    expect(new Headers(command.headers).get('X-CSRF-TOKEN')).toBe('csrf-test')
    expect(command.credentials).toBe('same-origin')
  })

  it('surfaces stable API problems without exposing response internals', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ title: 'Version conflict' }), { status: 409 }),
    ))

    await expect(api('/api/incidents/one')).rejects.toEqual(
      new ApiError(409, 'Version conflict'),
    )
  })
})
