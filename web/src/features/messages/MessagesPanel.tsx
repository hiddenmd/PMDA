import { keepPreviousData, useInfiniteQuery } from '@tanstack/react-query'
import { useVirtualizer } from '@tanstack/react-virtual'
import { ChevronLeft, ChevronRight, Crosshair } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { getEvents } from '../../api/dataViews'
import type { EventType } from '../../api/types'
import { formatElapsed, formatNumber } from '../../lib/format'
import { NoPlayback } from '../../workspace/panels/EmptyPanel'
import { useWorkspace, type PlaybackActor } from '../../workspace/workspaceContext'
import { useFlightOverview, usePlaybackState } from '../playback/usePlayback'
import { currentRowIndex, MESSAGE_TABLES, windowStart, type MessageTable } from './messageColumns'

const WINDOWS = [
  { ms: 10_000, label: '10 s' },
  { ms: 30_000, label: '30 s' },
  { ms: 120_000, label: '2 min' },
  { ms: 600_000, label: '10 min' },
]
const PAGE_LIMIT = 2_000

export function MessagesPanel() {
  const { playback, flight, flightLoading } = useWorkspace()
  if (!playback || !flight) return <NoPlayback loading={flightLoading} />
  return <Messages flightId={flight.id} startUtc={playback.startUtc} durationMs={playback.durationMs} actor={playback.actor} />
}

interface MessagesProps {
  flightId: string
  startUtc: string
  durationMs: number
  actor: PlaybackActor
}

function Messages({ flightId, durationMs, actor }: MessagesProps) {
  const [type, setType] = useState<EventType>('Emitter')
  const [windowMs, setWindowMs] = useState(30_000)
  const [follow, setFollow] = useState(true)
  const [manualStart, setManualStart] = useState(0)
  const playback = usePlaybackState(actor)
  const overview = useFlightOverview(flightId)

  const totals = useMemo(() => {
    const t = new Map<EventType, number>()
    overview.data?.series.forEach((s) => t.set(s.eventType, s.counts.reduce((a, b) => a + b, 0)))
    return t
  }, [overview.data])

  const table = MESSAGE_TABLES.find((t) => t.type === type)!
  const from = follow ? windowStart(playback.positionMs, windowMs) : manualStart
  const to = Math.min(from + windowMs, Math.max(from + 1, durationMs + 1))

  const move = (direction: -1 | 1) => {
    setFollow(false)
    setManualStart(Math.max(0, Math.min(durationMs, from + direction * windowMs)))
  }

  return (
    <div className="panel-body panel-body--flush">
      <div className="tabs" role="tablist" aria-label="Message types">
        {MESSAGE_TABLES.map((t) => (
          <button key={t.type} type="button" role="tab" aria-selected={t.type === type} onClick={() => setType(t.type)}>
            {t.label}
            {totals.has(t.type) && <span className="count">{formatNumber(totals.get(t.type)!)}</span>}
          </button>
        ))}
      </div>
      <div className="panel-toolbar">
        <button type="button" className="btn btn--ghost btn--icon btn--compact" aria-label="Earlier window" title="Earlier" onClick={() => move(-1)}>
          <ChevronLeft size={14} aria-hidden />
        </button>
        <span className="mono small" aria-live="polite">
          {formatElapsed(from)} – {formatElapsed(to)}
        </span>
        <button type="button" className="btn btn--ghost btn--icon btn--compact" aria-label="Later window" title="Later" onClick={() => move(1)}>
          <ChevronRight size={14} aria-hidden />
        </button>
        <label className="visually-hidden" htmlFor={`window-${flightId}`}>
          Window length
        </label>
        <select id={`window-${flightId}`} className="select select--compact" value={windowMs} onChange={(e) => setWindowMs(Number(e.target.value))}>
          {WINDOWS.map((w) => (
            <option key={w.ms} value={w.ms}>
              {w.label}
            </option>
          ))}
        </select>
        <span className="panel-toolbar__spacer" />
        <button type="button" className={`btn btn--compact ${follow ? 'btn--primary' : 'btn--secondary'}`} aria-pressed={follow} onClick={() => setFollow((f) => !f)}>
          <Crosshair size={14} aria-hidden /> Follow playhead
        </button>
      </div>
      <EventTable
        key={`${type}-${from}-${windowMs}`}
        flightId={flightId}
        table={table}
        fromMs={from}
        toMs={to}
        positionMs={playback.positionMs}
        follow={follow}
        onSeek={(ms) => playback.seek(ms)}
      />
    </div>
  )
}

interface EventTableProps {
  flightId: string
  table: MessageTable
  fromMs: number
  toMs: number
  positionMs: number
  follow: boolean
  onSeek: (ms: number) => void
}

function EventTable({ flightId, table, fromMs, toMs, positionMs, follow, onSeek }: EventTableProps) {
  const scrollRef = useRef<HTMLDivElement>(null)
  const events = useInfiniteQuery({
    queryKey: ['flights', 'events', flightId, table.type, fromMs, toMs],
    queryFn: ({ pageParam, signal }) => getEvents(flightId, { type: table.type, fromMs, toMs, afterSeq: pageParam, limit: PAGE_LIMIT }, signal),
    initialPageParam: -1,
    getNextPageParam: (last) => (last.hasMore ? (last.nextAfterSeq ?? undefined) : undefined),
    placeholderData: keepPreviousData,
    staleTime: Infinity,
  })
  const rows = useMemo(() => events.data?.pages.flatMap((p) => p.rows) ?? [], [events.data])

  const rowHeight = Math.round(26 * (Number(getComputedStyle(document.documentElement).getPropertyValue('--ui-scale')) || 1))
  // TanStack Virtual returns non-memoizable functions; harmless here (no React Compiler in this app).
  // oxlint-disable-next-line react/incompatible-library
  const virtualizer = useVirtualizer({ count: rows.length, getScrollElement: () => scrollRef.current, estimateSize: () => rowHeight, overscan: 12 })
  const current = currentRowIndex(rows, positionMs)

  useEffect(() => {
    if (follow && current >= 0) virtualizer.scrollToIndex(current, { align: 'center' })
  }, [follow, current, virtualizer])

  const grid = table.columns.map((c) => c.width).join(' ') + ' 104px'

  return (
    <div className="vtable" ref={scrollRef} role="table" aria-label={`${table.label} messages`} aria-rowcount={rows.length + 1}>
      <div className="vtable__head" role="row" style={{ gridTemplateColumns: grid }}>
        {table.columns.map((c) => (
          <div key={c.key} role="columnheader" className={`vtable__cell${c.numeric ? ' num' : ''}`} style={c.numeric ? { textAlign: 'right' } : undefined}>
            {c.label}
          </div>
        ))}
        <div role="columnheader" className="vtable__cell num" style={{ textAlign: 'right' }}>
          Time
        </div>
      </div>

      {events.isPending && <div className="empty">Loading messages…</div>}
      {events.isError && <div className="empty">Could not load messages: {events.error.message}</div>}
      {events.data && rows.length === 0 && <div className="empty">No {table.label.toLowerCase()} messages in this window.</div>}

      <div style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
        {virtualizer.getVirtualItems().map((item) => {
          const row = rows[item.index]
          return (
            <div
              key={row.seq}
              role="row"
              aria-rowindex={item.index + 2}
              className={`vtable__row${item.index === current ? ' is-current' : ''}`}
              style={{ top: item.start, height: item.size, gridTemplateColumns: grid }}
              onClick={() => onSeek(row.offsetMs)}
              title="Click to move the playhead here"
            >
              {table.columns.map((c) => (
                <div key={c.key} role="cell" className={`vtable__cell${c.numeric ? ' num' : ''}`} style={c.numeric ? { textAlign: 'right' } : undefined}>
                  {c.value(row)}
                </div>
              ))}
              <div role="cell" className="vtable__cell num muted" style={{ textAlign: 'right' }}>
                {formatElapsed(row.offsetMs)}
              </div>
            </div>
          )
        })}
      </div>

      {events.hasNextPage && (
        <div style={{ padding: 8, textAlign: 'center' }}>
          <button type="button" className="btn btn--secondary btn--compact" disabled={events.isFetchingNextPage} onClick={() => void events.fetchNextPage()}>
            {events.isFetchingNextPage ? 'Loading…' : `Load more (${formatNumber(rows.length)} shown)`}
          </button>
        </div>
      )}
    </div>
  )
}
