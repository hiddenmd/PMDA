import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ChevronLeft, ChevronRight, MoreHorizontal, Search, SlidersHorizontal, Trash2, X } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { deleteAllFlights, deleteFlights } from '../../api/flights'
import type { FlightSummary } from '../../api/types'
import { Dialog } from '../../components/Dialog'
import { Menu, MenuItem } from '../../components/Menu'
import { StatusChip } from '../../components/StatusChip'
import { flightKeys, useCustomers, useFlights } from '../../hooks/useFlights'
import { formatCompact, formatNumber, formatUtc, formatUtcShort } from '../../lib/format'
import { activeFilterCount, EMPTY_FILTERS, filtersFromUrl, filtersToUrl, SORT_LABELS, toQuery, type FlightFilters } from './flightFilters'

const PAGE_SIZE = 50
const DEBOUNCE_MS = 300

interface FlightListProps {
  selectedId: string | null
  onSelect: (id: string) => void
  /** Admins may delete flights. */
  canManage?: boolean
}

export function FlightList({ selectedId, onSelect, canManage = false }: FlightListProps) {
  const [filters, setFilters] = useState<FlightFilters>(() => filtersFromUrl(window.location.search))
  const [query, setQuery] = useState(filters)
  const [page, setPage] = useState(1)
  const [showFilters, setShowFilters] = useState(() => activeFilterCount(filters) > 0)
  const [checked, setChecked] = useState<Set<string>>(new Set())
  const [confirm, setConfirm] = useState<'selected' | 'all' | null>(null)

  // Typing is debounced; the applied filters also go into the URL so the view can be shared.
  useEffect(() => {
    const timer = setTimeout(() => {
      setQuery(filters)
      setPage(1)
      window.history.replaceState(null, '', filtersToUrl(filters, window.location.href))
    }, DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [filters])

  const flights = useFlights(useMemo(() => toQuery(query, page, PAGE_SIZE), [query, page]))
  const total = flights.data?.total ?? 0
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const active = activeFilterCount(filters)
  const update = <K extends keyof FlightFilters>(key: K, value: FlightFilters[K]) => setFilters((f) => ({ ...f, [key]: value }))

  return (
    <section className="flights" aria-labelledby="flights-title">
      <div className="section-title">
        <h3 id="flights-title">
          Flights {flights.data && <span className="muted mono">({formatNumber(total)})</span>}
        </h3>
        <div style={{ display: 'flex', gap: 2 }}>
          <button
            type="button"
            className="btn btn--ghost btn--compact"
            aria-expanded={showFilters}
            aria-controls="flight-filters"
            onClick={() => setShowFilters((s) => !s)}
          >
            <SlidersHorizontal size={14} aria-hidden /> Filters{active > 0 && <span className="chip">{active}</span>}
          </button>
          {canManage && (
            <Menu label="Flight actions" triggerClassName="btn btn--ghost btn--icon btn--compact" trigger={<MoreHorizontal size={16} aria-hidden />}>
              {(close) => (
                <MenuItem
                  danger
                  icon={<Trash2 size={14} aria-hidden />}
                  onSelect={() => {
                    close()
                    setConfirm('all')
                  }}
                >
                  Delete all flights…
                </MenuItem>
              )}
            </Menu>
          )}
        </div>
      </div>

      <div className="input-with-icon">
        <Search size={14} aria-hidden />
        <input className="input input--compact" type="search" placeholder="Tail" aria-label="Filter by tail" value={filters.tail} onChange={(e) => update('tail', e.target.value)} />
      </div>

      {showFilters && <FilterFields id="flight-filters" filters={filters} update={update} onReset={() => setFilters({ ...EMPTY_FILTERS, sort: filters.sort })} />}

      {canManage && checked.size > 0 && (
        <div className="selection-bar" role="region" aria-label="Selected flights">
          <span>{checked.size} selected</span>
          <span style={{ display: 'flex', gap: 4 }}>
            <button type="button" className="btn btn--ghost btn--compact" onClick={() => setChecked(new Set())}>
              <X size={14} aria-hidden /> Clear
            </button>
            <button type="button" className="btn btn--danger btn--compact" onClick={() => setConfirm('selected')}>
              <Trash2 size={14} aria-hidden /> Delete
            </button>
          </span>
        </div>
      )}

      {flights.isPending && <p className="muted small">Loading flights…</p>}
      {flights.isError && (
        <p className="notice notice--error" role="alert">
          Could not load flights: {flights.error.message}
        </p>
      )}
      {flights.data && (
        <FlightItems
          flights={flights.data.items}
          selectedId={selectedId}
          onSelect={onSelect}
          checked={canManage ? checked : undefined}
          onCheck={(id, on) =>
            setChecked((s) => {
              const next = new Set(s)
              if (on) next.add(id)
              else next.delete(id)
              return next
            })
          }
          filtered={active > 0}
        />
      )}

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

      <DeleteDialog
        mode={confirm}
        ids={[...checked]}
        onClose={() => setConfirm(null)}
        onDeleted={() => {
          setChecked(new Set())
          setConfirm(null)
        }}
      />
    </section>
  )
}

interface FilterFieldsProps {
  id: string
  filters: FlightFilters
  update: <K extends keyof FlightFilters>(key: K, value: FlightFilters[K]) => void
  onReset: () => void
}

function FilterFields({ id, filters, update, onReset }: FilterFieldsProps) {
  const customers = useCustomers()
  return (
    <div id={id} className="filters" role="group" aria-label="Flight filters">
      <label className="field">
        <span>Mission</span>
        <input className="input input--compact" value={filters.mission} onChange={(e) => update('mission', e.target.value)} />
      </label>
      <label className="field">
        <span>Customer</span>
        <select className="select select--compact" value={filters.customerId} onChange={(e) => update('customerId', e.target.value)}>
          <option value="">Any</option>
          {customers.data?.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        <span>Status</span>
        <select className="select select--compact" value={filters.status} onChange={(e) => update('status', e.target.value as FlightFilters['status'])}>
          <option value="">Any</option>
          <option value="Completed">Completed</option>
          <option value="Failed">Failed</option>
          <option value="Importing">Importing</option>
        </select>
      </label>
      <label className="field">
        <span>Parse problems</span>
        <select className="select select--compact" value={filters.problems} onChange={(e) => update('problems', e.target.value as FlightFilters['problems'])}>
          <option value="any">Any</option>
          <option value="with">With problems</option>
          <option value="without">Without problems</option>
        </select>
      </label>
      <label className="field">
        <span>Flown from (UTC)</span>
        <input className="input input--compact" type="date" value={filters.startFrom} onChange={(e) => update('startFrom', e.target.value)} />
      </label>
      <label className="field">
        <span>Flown to (UTC)</span>
        <input className="input input--compact" type="date" value={filters.startTo} onChange={(e) => update('startTo', e.target.value)} />
      </label>
      <label className="field field--wide">
        <span>Sort by</span>
        <select className="select select--compact" value={filters.sort} onChange={(e) => update('sort', e.target.value as FlightFilters['sort'])}>
          {Object.entries(SORT_LABELS).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </label>
      <div className="filters__row field--wide">
        <span className="muted">Text filters match any part of the value.</span>
        <button type="button" className="btn btn--ghost btn--compact" onClick={onReset}>
          Reset filters
        </button>
      </div>
    </div>
  )
}

interface FlightItemsProps {
  flights: readonly FlightSummary[]
  selectedId: string | null
  onSelect: (id: string) => void
  /** When given, rows get a checkbox for bulk actions. */
  checked?: ReadonlySet<string>
  onCheck?: (id: string, checked: boolean) => void
  filtered?: boolean
}

export function FlightItems({ flights, selectedId, onSelect, checked, onCheck, filtered }: FlightItemsProps) {
  if (flights.length === 0)
    return <p className="muted small">{filtered ? 'No flights match the filters.' : 'No flights yet. Import a recording to get started.'}</p>

  return (
    <ul className="flights-list">
      {flights.map((f) => (
        <li key={f.id} className={`flight-row${checked ? '' : ' flight-row--plain'}`}>
          {checked && (
            <input
              type="checkbox"
              className="checkbox"
              aria-label={`Select ${f.tail} ${f.missionId}`}
              checked={checked.has(f.id)}
              onChange={(e) => onCheck?.(f.id, e.target.checked)}
            />
          )}
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

interface DeleteDialogProps {
  mode: 'selected' | 'all' | null
  ids: string[]
  onClose: () => void
  onDeleted: () => void
}

function DeleteDialog({ mode, ids, onClose, onDeleted }: DeleteDialogProps) {
  const queryClient = useQueryClient()
  const [typed, setTyped] = useState('')
  const mutation = useMutation({
    mutationFn: () => (mode === 'all' ? deleteAllFlights() : deleteFlights(ids)),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: flightKeys.all })
      setTyped('')
      onDeleted()
    },
  })
  const all = mode === 'all'
  const ready = !all || typed === 'DELETE'
  const close = () => {
    setTyped('')
    mutation.reset()
    onClose()
  }

  return (
    <Dialog
      open={mode !== null}
      title={all ? 'Delete all flights?' : `Delete ${ids.length} flight${ids.length === 1 ? '' : 's'}?`}
      icon={<AlertTriangle size={18} color="var(--color-destructive)" aria-hidden />}
      onClose={close}
    >
      <p className="small">
        {all
          ? 'Every flight, its events and its parse problems are removed for all users. This cannot be undone.'
          : 'The selected flights, their events and parse problems are removed for all users. This cannot be undone.'}
      </p>
      {all && (
        <label className="field">
          <span>
            Type <strong className="mono">DELETE</strong> to confirm
          </span>
          <input className="input" autoComplete="off" value={typed} onChange={(e) => setTyped(e.target.value)} />
        </label>
      )}
      {mutation.isError && (
        <p className="field-error" role="alert">
          {mutation.error.message}
        </p>
      )}
      <div className="dialog__actions">
        <button type="button" className="btn btn--secondary" onClick={close}>
          Cancel
        </button>
        <button type="button" className="btn btn--danger" disabled={!ready || mutation.isPending} onClick={() => mutation.mutate()}>
          <Trash2 size={14} aria-hidden /> {mutation.isPending ? 'Deleting…' : all ? 'Delete everything' : 'Delete'}
        </button>
      </div>
    </Dialog>
  )
}
