import { useFlight, useFlightDiagnostics } from '../../hooks/useFlights'
import { formatNumber, formatUtc } from '../../lib/format'

export function FlightDetails({ flightId }: { flightId: string | null }) {
  const flight = useFlight(flightId)
  const diagnostics = useFlightDiagnostics(flightId)

  if (flightId === null) {
    return (
      <section className="panel panel--empty">
        <p className="muted">Select a flight to see its details.</p>
      </section>
    )
  }

  return (
    <section className="panel" aria-labelledby="flight-title">
      {flight.isPending && <p className="muted">Loading flight…</p>}
      {flight.isError && (
        <p className="notice notice--error" role="alert">
          Could not load the flight: {flight.error.message}
        </p>
      )}
      {flight.data && (
        <>
          <h2 id="flight-title">
            {flight.data.tail} · {flight.data.missionId}
          </h2>
          <dl className="facts">
            <dt>Status</dt>
            <dd>
              <span className={`status status--${flight.data.status.toLowerCase()}`}>{flight.data.status}</span>
              {flight.data.failureReason && <span className="muted"> — {flight.data.failureReason}</span>}
            </dd>
            <dt>Customer</dt>
            <dd>
              {flight.data.customerId} (format v{flight.data.formatVersion})
            </dd>
            <dt>Flight start</dt>
            <dd>{formatUtc(flight.data.startUtc)}</dd>
            <dt>Events</dt>
            <dd>
              {formatNumber(flight.data.eventCount)} from {formatUtc(flight.data.firstEventUtc)} to {formatUtc(flight.data.lastEventUtc)}
            </dd>
            <dt>Source file</dt>
            <dd>{flight.data.sourceName}</dd>
            <dt>Imported</dt>
            <dd>{formatUtc(flight.data.importedUtc)}</dd>
          </dl>
        </>
      )}

      <h3>Parse problems</h3>
      {diagnostics.isPending && <p className="muted">Loading…</p>}
      {diagnostics.data?.length === 0 && <p className="muted">None — every record was read cleanly.</p>}
      {diagnostics.data && diagnostics.data.length > 0 && (
        <table className="table table--compact">
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
                <td className="num mono">{formatNumber(d.fileOffset)}</td>
                <td>{d.message}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
