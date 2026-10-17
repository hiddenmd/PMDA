import type { SeriesParam } from '../../api/types'

export type GraphKind = 'parameter' | 'bearing' | 'profile' | 'rate' | 'scatter'

export const GRAPH_KINDS: { kind: GraphKind; label: string }[] = [
  { kind: 'parameter', label: 'Parameter vs time' },
  { kind: 'bearing', label: 'Bearing vs time' },
  { kind: 'profile', label: 'Flight profile' },
  { kind: 'rate', label: 'Event rate' },
  { kind: 'scatter', label: 'Parameter vs parameter' },
]

export const PARAMS: { param: SeriesParam; label: string; unit: string }[] = [
  { param: 'Frequency', label: 'Frequency', unit: 'MHz' },
  { param: 'Pri', label: 'PRI', unit: 'µs' },
  { param: 'PulseWidth', label: 'Pulse width', unit: 'µs' },
  { param: 'Amplitude', label: 'Amplitude', unit: 'dB' },
  { param: 'Bearing', label: 'Bearing', unit: '°' },
]

export const paramLabel = (p: SeriesParam) => {
  const info = PARAMS.find((x) => x.param === p)!
  return `${info.label} (${info.unit})`
}

/** Settings of one graph panel, stored in the panel's parameters (and so in saved layouts). */
export interface GraphSettings {
  kind: GraphKind
  param: SeriesParam
  x: SeriesParam
  y: SeriesParam
}

export const DEFAULT_GRAPH: GraphSettings = { kind: 'bearing', param: 'Frequency', x: 'Frequency', y: 'Pri' }

export function graphTitle(s: GraphSettings): string {
  switch (s.kind) {
    case 'parameter':
      return `${PARAMS.find((p) => p.param === s.param)!.label} vs time`
    case 'scatter':
      return `${PARAMS.find((p) => p.param === s.y)!.label} vs ${PARAMS.find((p) => p.param === s.x)!.label}`
    default:
      return GRAPH_KINDS.find((k) => k.kind === s.kind)!.label
  }
}

export const isTimeKind = (k: GraphKind) => k !== 'scatter'

/**
 * Puts per-emitter points on one shared, sorted time axis (what uPlot's default mode needs);
 * missing points are null so no line or point is drawn there.
 */
export function alignOnTime(series: readonly { t: readonly number[]; v: readonly number[] }[]): { x: number[]; ys: (number | null)[][] } {
  const times = new Set<number>()
  for (const s of series) for (const t of s.t) times.add(t)
  const x = [...times].sort((a, b) => a - b)
  const index = new Map(x.map((t, i) => [t, i]))
  const ys = series.map((s) => {
    const column = new Array<number | null>(x.length).fill(null)
    s.t.forEach((t, i) => (column[index.get(t)!] = s.v[i]))
    return column
  })
  return { x, ys }
}

/** Series colour slots (CSS variables); cycle through them by series index. */
export const SERIES_COLORS = [
  '--color-series-emitter',
  '--color-series-navigation',
  '--color-series-4',
  '--color-series-5',
  '--color-series-6',
  '--color-series-countermeasure',
]
