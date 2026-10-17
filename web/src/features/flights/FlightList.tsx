import { ChevronLeft, ChevronRight, Search } from 'lucide-react'
import { useState } from 'react'
import type { FlightSummary } from '../../api/types'
import { StatusChip } from '../../components/StatusChip'
import { useFlights } from '../../hooks/useFlights'
import { formatCompact, formatNumber, formatUtc, formatUtcShort } from '../../lib/format'

const PAGE_SIZE = 30

interface FlightListProps {
  selectedId: string | null
  onSelect: (id: string) => void
}

export function FlightList({ selectedId, onSelect }: FlightListProps) {
  const [page, setPage] = useState(1)
  const [tail, setTail] = useState('')
  const flights = useFlights({ page, pageSize: PAGE_SIZE, tail: tail.trim() || undefined })

  const total = flights.data?.total ?? 0
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <section className="panel flights" aria-labelledby="flights-title">
      <div className="panel__header">
        <h2 id="flights-title">Flights</h2>
        {flights.data && <span className="muted mono">{formatNumber(total)}</span>}
      </div>
      <div className="input-with-icon">
        <Search size={14} aria-hidden />
        <input
          className="input"
          type="search"
          placeholder="Filter by tail"
          aria-label="Filter flights by tail"
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
      {flights.data && <FlightItems flights={flights.data.items} selectedId={selectedId} onSelect={onSelect} />}

      {total > PAGE_SIZE && (
        <nav className="pager" aria-label="Flight pages">
          <button type="button" className="btn btn--ghost btn--icon btn--compact" aria-label="Previous page" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            <ChevronLeft size={16} aria-hidden />
          </button>
          <span>
            Page {page} of {pageCount}
          </span>
          <button type="button" className="btn btn--ghost btn--icon btn--compact" aria-label="Next page" disabled={page >= pageCount} onClick={() => setPage((p) => p + 1)}>
            <ChevronRight size={16} aria-hidden />
          </button>
        </nav>
      )}
    </section>
  )
}

interface FlightItemsProps {
  flights: readonly FlightSummary[]
  selectedId: string | null
  onSelect: (id: string) => void
}

export function FlightItems({ flights, selectedId, onSelect }: FlightItemsProps) {
  if (flights.length === 0) return <p className="muted">No flights yet. Import a recording to get started.</p>

  return (
    <ul className="flights__list">
      {flights.map((f) => (
        <li key={f.id}>
          <button type="button" className="flight-item" aria-current={f.id === selectedId} onClick={() => onSelect(f.id)}>
            <span className="flight-item__title" title={`${f.tail} · ${f.missionId}`}>
              {f.tail} <span className="muted">· {f.missionId}</span>
            </span>
            <StatusChip status={f.status} />
            <span className="flight-item__meta">
              <span title={formatUtc(f.startUtc)}>{formatUtcShort(f.startUtc)}</span>
              <span className="mono" title={`${formatNumber(f.eventCount)} events`}>
                {formatCompact(f.eventCount)} ev{f.errorCount > 0 ? ` · ${formatNumber(f.errorCount)} prob` : ''}
              </span>
            </span>
          </button>
        </li>
      ))}
    </ul>
  )
}
