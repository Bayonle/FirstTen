import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { incidentQueries } from '../lib/api'

type IncidentChange = { incidentId: string; version: number; category: string }

export function LiveUpdates() {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<'connecting' | 'live' | 'offline'>('connecting')

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/operations')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connection.on('incidentChanged', (change: IncidentChange) => {
      void queryClient.invalidateQueries({ queryKey: incidentQueries.all() })
      void queryClient.invalidateQueries({ queryKey: incidentQueries.detail(change.incidentId) })
      void queryClient.invalidateQueries({ queryKey: incidentQueries.timeline(change.incidentId) })
    })
    connection.onreconnecting(() => setStatus('connecting'))
    connection.onreconnected(() => setStatus('live'))
    connection.onclose(() => setStatus('offline'))
    connection.start().then(() => setStatus('live')).catch(() => setStatus('offline'))
    return () => {
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
