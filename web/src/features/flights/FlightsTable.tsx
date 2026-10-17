import type { FlightSummary } from '../../api/types'
import { formatNumber, formatUtc } from '../../lib/format'

interface FlightsTableProps {
  flights: readonly FlightSummary[]
  selectedId: string | null
  onSelect: (id: string) => void
}

export function FlightsTable({ flights, selectedId, onSelect }: FlightsTableProps) {
  if (flights.length === 0) return <p className="muted">No flights yet. Import a recording to get started.</p>

  return (
    <div className="table-wrap">
      <table className="table">
        <thead>
          <tr>
            <th scope="col">Tail</th>
            <th scope="col">Mission</th>
            <th scope="col">Customer</th>
            <th scope="col">Start</th>
            <th scope="col" className="num">
              Events
            </th>
            <th scope="col" className="num">
              Problems
            </th>
            <th scope="col">Status</th>
          </tr>
        </thead>
        <tbody>
          {flights.map((f) => (
            <tr
              key={f.id}
              className={f.id === selectedId ? 'is-selected' : undefined}
              aria-selected={f.id === selectedId}
              onClick={() => onSelect(f.id)}
            >
              <td>
                {/* A real button keeps rows reachable by keyboard; its click bubbles to the row. */}
                <button type="button" className="link">
                  {f.tail}
                </button>
              </td>
              <td>{f.missionId}</td>
              <td>{f.customerId}</td>
              <td>{formatUtc(f.startUtc)}</td>
              <td className="num">{formatNumber(f.eventCount)}</td>
              <td className="num">{formatNumber(f.errorCount)}</td>
              <td>
                <span className={`status status--${f.status.toLowerCase()}`}>{f.status}</span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
