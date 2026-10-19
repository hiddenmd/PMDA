import { Radar } from '../../components/icons'
import type { ReactNode } from 'react'

export function EmptyPanel({ title = 'No flight selected', children }: { title?: string; children?: ReactNode }) {
  return (
    <div className="empty">
      <Radar size={36} aria-hidden />
      <strong>{title}</strong>
      <span>{children ?? 'Pick a flight in the Flights panel.'}</span>
    </div>
  )
}

/** Wording for panels that need playback data for the selected flight. */
export function NoPlayback({ loading }: { loading: boolean }) {
  if (loading) return <div className="empty">Loading flight…</div>
  return <EmptyPanel />
}
