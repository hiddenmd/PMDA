import { describe, expect, it } from 'vitest'
import type { EventRow } from '../../api/types'
import { currentRowIndex, MESSAGE_TABLES, windowStart } from './messageColumns'

const row = (seq: number, offsetMs: number, extra: Partial<EventRow> = {}): EventRow => ({
  seq,
  offsetMs,
  subType: null,
  emitterId: null,
  frequencyMHz: null,
  priUs: null,
  pulseWidthUs: null,
  bearingDeg: null,
  amplitudeDb: null,
  lat: null,
  lon: null,
  altM: null,
  count: null,
  ...extra,
})

describe('message tables', () => {
  it('finds the row at the playhead', () => {
    const rows = [row(1, 100), row(2, 200), row(3, 300)]
    expect(currentRowIndex(rows, 50)).toBe(-1)
    expect(currentRowIndex(rows, 200)).toBe(1)
    expect(currentRowIndex(rows, 250)).toBe(1)
    expect(currentRowIndex(rows, 9999)).toBe(2)
  })

  it('moves the window in quarter steps around the playhead', () => {
    expect(windowStart(0, 30_000)).toBe(0)
    expect(windowStart(60_000, 30_000)).toBe(45_000)
    expect(windowStart(66_000, 30_000)).toBe(45_000) // same window while the playhead moves a little
    expect(windowStart(68_000, 30_000)).toBe(52_500)
  })

  it('formats cells per message type', () => {
    const emitter = MESSAGE_TABLES.find((t) => t.type === 'Emitter')!
    const cells = emitter.columns.map((c) => c.value(row(7, 0, { emitterId: 23, frequencyMHz: 14139.04, bearingDeg: 44.6, amplitudeDb: -35.94 })))
    expect(cells).toEqual(['7', '23', '14139.0', '—', '—', '045', '-35.9'])

    const cm = MESSAGE_TABLES.find((t) => t.type === 'Countermeasure')!
    expect(cm.columns.map((c) => c.value(row(1, 0, { subType: 2, count: 4 })))).toEqual(['1', 'Flare', '4'])

    const alerts = MESSAGE_TABLES.find((t) => t.type === 'Warning')!
    expect(alerts.columns.map((c) => c.value(row(9, 0, { subType: 3, emitterId: 12, bearingDeg: 7 })))).toEqual(['9', 'Missile launch', '12', '007'])
    expect(alerts.columns[1].value(row(9, 0, { subType: 42 }))).toBe('Alert') // a code this build does not know
  })
})
