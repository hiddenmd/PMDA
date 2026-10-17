import { useState } from 'react'
import { useFlights } from '../../hooks/useFlights'
import { FlightsTable } from './FlightsTable'

const PAGE_SIZE = 25

interface FlightsSectionProps {
  selectedId: string | null
  onSelect: (id: string) => void
}

export function FlightsSection({ selectedId, onSelect }: FlightsSectionProps) {
  const [page, setPage] = useState(1)
  const [tail, setTail] = useState('')
  const flights = useFlights({ page, pageSize: PAGE_SIZE, tail: tail.trim() || undefined })

  const total = flights.data?.total ?? 0
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <section className="panel" aria-labelledby="flights-title">
      <div className="panel__header">
        <h2 id="flights-title">Flights</h2>
        <input
          type="search"
          placeholder="Filter by tail"
          aria-label="Filter by tail"
          value={tail}
          onChange={(e) => {
            setTail(e.target.value)
            setPage(1)
          }}
        />
      </div>

      {flights.isPending && <p className="muted">Loading flights…</p>}
      {flights.isError && (
        <p className="notice notice--error" role="alert">
          Could not load flights: {flights.error.message}
        </p>
      )}
      {flights.data && <FlightsTable flights={flights.data.items} selectedId={selectedId} onSelect={onSelect} />}

      {total > PAGE_SIZE && (
        <nav className="pager" aria-label="Flight pages">
          <button type="button" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            Previous
          </button>
          <span>
            Page {page} of {pageCount}
          </span>
          <button type="button" disabled={page >= pageCount} onClick={() => setPage((p) => p + 1)}>
            Next
          </button>
        </nav>
      )}
    </section>
  )
}
