export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message)
  }
}

let antiforgeryToken: string | null = null

async function getAntiforgeryToken() {
  if (antiforgeryToken) return antiforgeryToken
  const response = await fetch('/api/auth/antiforgery', { credentials: 'same-origin' })
  if (!response.ok) throw new ApiError(response.status, 'Could not establish a secure session.')
  const payload = (await response.json()) as { token: string }
  antiforgeryToken = payload.token
  return payload.token
}

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const method = init?.method?.toUpperCase() ?? 'GET'
  const mutating = !['GET', 'HEAD', 'OPTIONS'].includes(method)
  const headers = new Headers(init?.headers)
  headers.set('Accept', 'application/json')
  if (init?.body) headers.set('Content-Type', 'application/json')
  if (mutating) headers.set('X-CSRF-TOKEN', await getAntiforgeryToken())

  const response = await fetch(path, {
    ...init,
    headers,
    credentials: 'same-origin',
  })
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as
      | { title?: string; detail?: string; error?: string }
      | null
    throw new ApiError(
      response.status,
      problem?.detail ?? problem?.error ?? problem?.title ?? `Request failed (${response.status}).`,
    )
  }
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export type IncidentSummary = {
  id: string
  verificationStatus: string
  version: number
  createdAtUtc: string
  reviewDueAtUtc: string
  sourceCount: number
  unresolvedConflictCount: number
  dispatchStatus: string
  dispatchVersion: number
}

export type IncidentDetail = IncidentSummary & {
  verifiedAtUtc?: string
  rejectedAtUtc?: string
  rejectionReason?: string
  dispatch: null | { status: string; version: number; updatedAtUtc: string; lastReopenReason?: string }
  sources: Array<{
    reportId: string
    occurredAtUtc: string
    receivedAtUtc: string
    incidentType: string
    severity: string
    casualtyMinimum?: number
    casualtyMaximum?: number
    victimState: string
    sceneState: string
    locationDescription?: string
    direction: string
    latitude?: number
    longitude?: number
    locationConfidence?: number
    evidenceReferences: string[]
  }>
  conflicts: Array<{
    id: string
    field: string
    leftReportId: string
    rightReportId: string
    leftValue: string
    rightValue: string
    isResolved: boolean
    selectedReportId?: string
    resolvedAtUtc?: string
  }>
  observations: Array<{
    id: string
    sourceReportId: string
    occurredAtUtc: string
    receivedAtUtc: string
    victimState: string
    sceneState: string
    locationDescription?: string
    direction: string
    evidenceReference: string
  }>
  guidance: Array<{
    id: string
    purpose: string
    trigger: string
    language: string
    status: string
    createdAtUtc: string
    deadlineAtUtc: string
    failureCode?: string
  }>
}

export type TimelineEvent = {
  id: string
  type: string
  source: string
  occurredAtUtc: string
  receivedAtUtc: string
  latitude?: number
  longitude?: number
  payloadJson: string
}

export const incidentQueries = {
  all: () => ['incidents'] as const,
  detail: (id: string) => ['incidents', id] as const,
  timeline: (id: string) => ['incidents', id, 'timeline'] as const,
}

export function formatClock(value: string) {
  return new Intl.DateTimeFormat('en-NG', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).format(new Date(value))
}

export function shortId(value: string) {
  return value.slice(0, 8).toUpperCase()
}
