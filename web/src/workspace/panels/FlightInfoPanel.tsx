import { StatusChip } from '../../components/StatusChip'
import { useFlightDiagnostics } from '../../hooks/useFlights'
import { formatCompact, formatDuration, formatNumber, formatUtc } from '../../lib/format'
import { playbackDurationMs, useWorkspace } from '../workspaceContext'
import { EmptyPanel } from './EmptyPanel'

export function FlightInfoPanel() {
  const { flight, flightLoading } = useWorkspace()
  if (flightLoading) return <div className="empty">Loading flight…</div>
  if (!flight) return <EmptyPanel />

  const durationMs = playbackDurationMs(flight)
  return (
    <div className="panel-body">
      <div className="flight-title">
        <h2>
          {flight.tail} · {flight.missionId}
        </h2>
        <StatusChip status={flight.status} />
      </div>
      <div className="meta-list">
        <span>
          {flight.customerId} format v{flight.formatVersion}
        </span>
        <span>Start {formatUtc(flight.startUtc)}</span>
        <span className="mono">{flight.sourceName}</span>
        <span>Imported {formatUtc(flight.importedUtc)}</span>
        {flight.failureReason && <span>{flight.failureReason}</span>}
      </div>

      <div className="kpis">
        <Kpi label="Events" value={formatCompact(flight.eventCount)} title={`${formatNumber(flight.eventCount)} events`} />
        <Kpi label="Duration" value={durationMs > 0 ? formatDuration(durationMs) : '—'} />
        <Kpi label="Parse problems" value={formatNumber(flight.errorCount)} />
      </div>

      <Diagnostics flightId={flight.id} />
    </div>
  )
}

function Kpi({ label, value, title }: { label: string; value: string; title?: string }) {
  return (
    <div className="kpi" title={title}>
      <div className="kpi__label">{label}</div>
      <div className="kpi__value">{value}</div>
    </div>
  )
}

function Diagnostics({ flightId }: { flightId: string }) {
  const diagnostics = useFlightDiagnostics(flightId)
  return (
    <section aria-labelledby="problems-title">
      <h3 id="problems-title">Parse problems</h3>
      {diagnostics.isPending && <p className="muted small">Loading…</p>}
      {diagnostics.data?.length === 0 && <p className="muted small">None. Every record was read cleanly.</p>}
      {diagnostics.data && diagnostics.data.length > 0 && (
        <div className="table-wrap" style={{ maxHeight: 280 }}>
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
    </section>
  )
}
