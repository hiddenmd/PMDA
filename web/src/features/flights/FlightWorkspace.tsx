import { ChevronRight, Plane, Radar } from 'lucide-react'
import type { FlightDetail } from '../../api/types'
import { StatusChip } from '../../components/StatusChip'
import { useFlight, useFlightDiagnostics } from '../../hooks/useFlights'
import { formatCompact, formatDuration, formatNumber, formatUtc } from '../../lib/format'
import { PlaybackPanel } from '../playback/PlaybackPanel'

export function FlightWorkspace({ flightId }: { flightId: string | null }) {
  const flight = useFlight(flightId)

  if (flightId === null) {
    return (
      <div className="empty-state">
        <Radar size={56} aria-hidden />
        <div>
          <h2>No flight selected</h2>
          <p>Import a recording or pick a flight on the left to start a debrief.</p>
        </div>
      </div>
    )
  }
  if (flight.isPending) return <p className="muted">Loading flight…</p>
  if (flight.isError)
    return (
      <p className="notice notice--error" role="alert">
        Could not load the flight: {flight.error.message}
      </p>
    )

  const f = flight.data
  const durationMs = playbackDurationMs(f)
  return (
    <div className="workspace-stack">
      <header>
        <div className="flight-header">
          <Plane size={20} aria-hidden />
          <h2>
            {f.tail} · {f.missionId}
          </h2>
          <StatusChip status={f.status} />
        </div>
        <div className="flight-header__meta">
          <span>
            {f.customerId} format v{f.formatVersion}
          </span>
          <span>Start {formatUtc(f.startUtc)}</span>
          <span className="mono">{f.sourceName}</span>
          {f.failureReason && <span>{f.failureReason}</span>}
        </div>
      </header>

      <div className="kpis">
        <Kpi label="Events" value={formatCompact(f.eventCount)} title={`${formatNumber(f.eventCount)} events`} />
        <Kpi label="Duration" value={durationMs > 0 ? formatDuration(durationMs) : '—'} />
        <Kpi label="Parse problems" value={formatNumber(f.errorCount)} />
        <Kpi label="Imported" value={formatUtc(f.importedUtc).slice(0, 11)} title={formatUtc(f.importedUtc)} />
      </div>

      {f.status === 'Completed' && durationMs > 0 ? (
        <PlaybackPanel key={f.id} flightId={f.id} startUtc={f.startUtc} durationMs={durationMs} />
      ) : (
        <p className="notice">Playback is available for completed flights with events.</p>
      )}

      <Diagnostics flightId={f.id} count={f.errorCount} />
    </div>
  )
}

/** Playback runs from the recorded start to the last event. */
function playbackDurationMs(f: FlightDetail): number {
  return f.lastEventUtc ? Math.max(0, Date.parse(f.lastEventUtc) - Date.parse(f.startUtc)) : 0
}

function Kpi({ label, value, title }: { label: string; value: string; title?: string }) {
  return (
    <div className="kpi" title={title}>
      <div className="kpi__label">{label}</div>
      <div className="kpi__value">{value}</div>
    </div>
  )
}

function Diagnostics({ flightId, count }: { flightId: string; count: number }) {
  const diagnostics = useFlightDiagnostics(flightId)

  return (
    <details className="panel collapsible">
      <summary>
        <ChevronRight size={16} aria-hidden />
        <h2>Parse problems</h2>
        <span className="muted mono">{formatNumber(count)}</span>
      </summary>
      {diagnostics.isPending && <p className="muted">Loading…</p>}
      {diagnostics.data?.length === 0 && <p className="muted">None. Every record was read cleanly.</p>}
      {diagnostics.data && diagnostics.data.length > 0 && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th scope="col" className="num">
                  File offset
                </th>
                <th scope="col">Problem</th>
              </tr>
            </thead>
            <tbody>
              {diagnostics.data.map((d, i) => (
                <tr key={`${d.fileOffset}-${i}`}>
                  <td className="num">{formatNumber(d.fileOffset)}</td>
                  <td style={{ whiteSpace: 'normal' }}>{d.message}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </details>
  )
}
