import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { incidentQueries } from '../lib/api'

type IncidentChange = { incidentId: string; version: number; category: string }

export function LiveUpdates() {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<'connecting' | 'live' | 'offline'>('connecting')

  useEffect(() => {
    let disposed = false
    let retryTimer: ReturnType<typeof setTimeout> | undefined
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/operations')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connection.on('incidentChanged', (change: IncidentChange) => {
      if (disposed) return
      void queryClient.invalidateQueries({ queryKey: incidentQueries.all(), exact: true })
      void queryClient.invalidateQueries({ queryKey: incidentQueries.detail(change.incidentId) })
      void queryClient.invalidateQueries({ queryKey: incidentQueries.timeline(change.incidentId) })
    })
    connection.onreconnecting(() => { if (!disposed) setStatus('connecting') })
    connection.onreconnected(() => { if (!disposed) setStatus('live') })
    connection.onclose(() => { if (!disposed) setStatus('offline') })
    const start = () => {
      if (disposed) return
      setStatus('connecting')
      connection.start()
        .then(() => { if (!disposed) setStatus('live') })
        .catch(() => {
          if (disposed) return
          setStatus('offline')
          retryTimer = setTimeout(start, 5_000)
        })
    }
    start()
    return () => {
      disposed = true
      if (retryTimer) clearTimeout(retryTimer)
      void connection.stop()
    }
  }, [queryClient])

  return (
    <span className={`connection-state connection-state--${status}`} role="status">
      <span aria-hidden="true" />
      {status === 'live' ? 'Live updates' : status === 'connecting' ? 'Reconnecting' : 'REST mode'}
    </span>
  )
}
