import type { EventRow, EventType } from '../../api/types'
import { ALERTS, alertKindOf } from '../alerts/alerts'

export interface Column {
  key: string
  label: string
  width: string
  numeric?: boolean
  value: (row: EventRow) => string
}

const fixed = (v: number | null | undefined, digits: number) => (v === null || v === undefined ? '—' : v.toFixed(digits))
const pad3 = (v: number | null | undefined) => (v === null || v === undefined ? '—' : v.toFixed(0).padStart(3, '0'))

const seq: Column = { key: 'seq', label: 'Seq', width: '84px', numeric: true, value: (r) => String(r.seq) }

export interface MessageTable {
  type: EventType
  label: string
  columns: Column[]
}

/** One tab per message type. Customer-specific types add entries here. */
export const MESSAGE_TABLES: MessageTable[] = [
  {
    type: 'Emitter',
    label: 'Emitter',
    columns: [
      seq,
      { key: 'emitter', label: 'Emitter', width: '72px', numeric: true, value: (r) => String(r.emitterId ?? '—') },
      { key: 'freq', label: 'Freq MHz', width: '92px', numeric: true, value: (r) => fixed(r.frequencyMHz, 1) },
      { key: 'pri', label: 'PRI µs', width: '84px', numeric: true, value: (r) => fixed(r.priUs, 1) },
      { key: 'pw', label: 'PW µs', width: '72px', numeric: true, value: (r) => fixed(r.pulseWidthUs, 2) },
      { key: 'brg', label: 'Brg °', width: '64px', numeric: true, value: (r) => pad3(r.bearingDeg) },
      { key: 'amp', label: 'Amp dB', width: '76px', numeric: true, value: (r) => fixed(r.amplitudeDb, 1) },
    ],
  },
  {
    type: 'Navigation',
    label: 'Navigation',
    columns: [
      seq,
      { key: 'lat', label: 'Latitude', width: '100px', numeric: true, value: (r) => fixed(r.lat, 5) },
      { key: 'lon', label: 'Longitude', width: '100px', numeric: true, value: (r) => fixed(r.lon, 5) },
      { key: 'alt', label: 'Alt m', width: '76px', numeric: true, value: (r) => fixed(r.altM, 0) },
    ],
  },
  {
    type: 'Countermeasure',
    label: 'Countermeasures',
    columns: [
      seq,
      { key: 'kind', label: 'Kind', width: '90px', value: (r) => (r.subType === 1 ? 'Chaff' : r.subType === 2 ? 'Flare' : `Kind ${r.subType ?? '?'}`) },
      { key: 'count', label: 'Count', width: '64px', numeric: true, value: (r) => String(r.count ?? 1) },
    ],
  },
  {
    type: 'Warning',
    label: 'Audio alerts',
    columns: [
      seq,
      { key: 'alert', label: 'Alert', width: '140px', value: (r) => ALERTS[alertKindOf(r.subType)].label },
      { key: 'emitter', label: 'Emitter', width: '72px', numeric: true, value: (r) => String(r.emitterId ?? '—') },
      { key: 'brg', label: 'Brg °', width: '64px', numeric: true, value: (r) => pad3(r.bearingDeg) },
    ],
  },
]

/** Index of the last row at or before the playhead, or -1. Rows are in time order. */
export function currentRowIndex(rows: readonly EventRow[], positionMs: number): number {
  let lo = 0
  let hi = rows.length - 1
  let found = -1
  while (lo <= hi) {
    const mid = (lo + hi) >> 1
    if (rows[mid].offsetMs <= positionMs) {
      found = mid
      lo = mid + 1
    } else {
      hi = mid - 1
    }
  }
  return found
}

/**
 * Start of the loaded window around the playhead. Snapped to quarter-window steps so the window
 * only moves (and reloads) every quarter window, not on every frame.
 */
export function windowStart(positionMs: number, windowMs: number): number {
  const step = windowMs / 4
  return Math.max(0, Math.floor((positionMs - windowMs / 2) / step) * step)
}
