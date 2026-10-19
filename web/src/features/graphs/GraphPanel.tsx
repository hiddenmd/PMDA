import { keepPreviousData, useQuery } from '@tanstack/react-query'
import type { IDockviewPanelProps } from 'dockview-react'
import { RotateCcw } from '../../components/icons'
import { useCallback, useEffect, useMemo, useState } from 'react'
import type uPlot from 'uplot'
import { getParameterSeries, getProfileSeries, getRateSeries, getScatterSeries } from '../../api/dataViews'
import type { EventType, ParameterSeries, ProfileSeries, RateSeries, ScatterSeries, SeriesParam } from '../../api/types'
import { formatElapsed, formatNumber } from '../../lib/format'
import { NoPlayback } from '../../workspace/panels/EmptyPanel'
import { useWorkspace, type PlaybackActor } from '../../workspace/workspaceContext'
import { alignOnTime, DEFAULT_GRAPH, GRAPH_KINDS, graphTitle, isTimeKind, paramLabel, PARAMS, SERIES_COLORS, type GraphSettings } from './graphKinds'
import { UPlotChart } from './UPlotChart'

const BUCKETS = 1_200

type GraphData =
  | { kind: 'parameter' | 'bearing'; data: ParameterSeries }
  | { kind: 'scatter'; data: ScatterSeries }
  | { kind: 'profile'; data: ProfileSeries }
  | { kind: 'rate'; data: RateSeries }

interface Window {
  fromMs: number
  toMs: number
}

export function GraphPanel(props: IDockviewPanelProps<Partial<GraphSettings>>) {
  const { playback, flight, flightLoading } = useWorkspace()
  const [settings, setSettings] = useState<GraphSettings>(() => ({ ...DEFAULT_GRAPH, ...props.params }))

  const change = (next: GraphSettings) => {
    setSettings(next)
    props.api.updateParameters(next) // saved with the layout
    props.api.setTitle(graphTitle(next))
  }

  if (!playback || !flight) return <NoPlayback loading={flightLoading} />
  return <Graph key={flight.id} flightId={flight.id} actor={playback.actor} settings={settings} onChange={change} />
}

interface GraphProps {
  flightId: string
  actor: PlaybackActor
  settings: GraphSettings
  onChange: (s: GraphSettings) => void
}

function Graph({ flightId, actor, settings, onChange }: GraphProps) {
  const [window, setWindow] = useState<Window | null>(null)
  const [hidden, setHidden] = useState<ReadonlySet<string>>(new Set())
  const theme = useThemeVersion()

  const query = useQuery({
    queryKey: ['flights', 'series', flightId, settings.kind, settings.param, settings.x, settings.y, window],
    queryFn: async ({ signal }): Promise<GraphData> => {
      const w = { fromMs: window?.fromMs, toMs: window?.toMs, buckets: BUCKETS }
      switch (settings.kind) {
        case 'parameter':
          return { kind: 'parameter', data: await getParameterSeries(flightId, { ...w, param: settings.param }, signal) }
        case 'bearing':
          return { kind: 'bearing', data: await getParameterSeries(flightId, { ...w, param: 'Bearing' }, signal) }
        case 'scatter':
          return { kind: 'scatter', data: await getScatterSeries(flightId, { ...w, x: settings.x, y: settings.y }, signal) }
        case 'profile':
          return { kind: 'profile', data: await getProfileSeries(flightId, w, signal) }
        case 'rate':
          return { kind: 'rate', data: await getRateSeries(flightId, { ...w, buckets: 300 }, signal) }
      }
    },
    placeholderData: keepPreviousData,
    staleTime: Infinity,
  })

  const seek = useCallback((ms: number) => actor.send({ type: 'SEEK', positionMs: ms }), [actor])
  const built = useMemo(() => (query.data ? build(query.data, hidden, theme) : null), [query.data, hidden, theme])

  const onCreate = useCallback(
    (u: uPlot) => {
      if (!isTimeKind(settings.kind)) return
      return attachTimeInteractions(u, actor, seek, (w) => setWindow(w))
    },
    [settings.kind, actor, seek],
  )

  const toggle = (key: string) =>
    setHidden((h) => {
      const next = new Set(h)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  const setKind = (kind: GraphSettings['kind']) => {
    setWindow(null)
    setHidden(new Set())
    onChange({ ...settings, kind })
  }

  return (
    <div className="graph-panel">
      <div className="panel-toolbar">
        <label className="visually-hidden" htmlFor={`kind-${flightId}`}>
          Graph type
        </label>
        <select id={`kind-${flightId}`} className="select select--compact" value={settings.kind} onChange={(e) => setKind(e.target.value as GraphSettings['kind'])}>
          {GRAPH_KINDS.map((k) => (
            <option key={k.kind} value={k.kind}>
              {k.label}
            </option>
          ))}
        </select>
        {settings.kind === 'parameter' && (
          <ParamSelect label="Parameter" value={settings.param} exclude="Bearing" onChange={(param) => onChange({ ...settings, param })} />
        )}
        {settings.kind === 'scatter' && (
          <>
            <ParamSelect label="X axis" value={settings.x} onChange={(x) => onChange({ ...settings, x })} />
            <span className="muted small">vs</span>
            <ParamSelect label="Y axis" value={settings.y} onChange={(y) => onChange({ ...settings, y })} />
          </>
        )}
        <span className="panel-toolbar__spacer" />
        {window && (
          <button type="button" className="btn btn--ghost btn--compact" onClick={() => setWindow(null)}>
            <RotateCcw size={14} aria-hidden /> Whole flight
          </button>
        )}
        <span className="muted small" aria-live="polite">
          {query.isFetching ? 'Loading…' : isTimeKind(settings.kind) ? 'Drag to zoom · click to seek' : 'Drag to zoom'}
        </span>
      </div>

      {built && built.legend.length > 0 && (
        <div className="graph-legend" role="group" aria-label="Series (click to show or hide)">
          {built.legend.map((l) => (
            <button key={l.key} type="button" className="graph-legend__item" aria-pressed={!hidden.has(l.key)} onClick={() => toggle(l.key)} title={l.title}>
              <span className="legend-swatch" style={{ background: l.color }} aria-hidden />
              {l.label}
            </button>
          ))}
        </div>
      )}

      <div className="graph-area">
        {query.isPending && <div className="empty">Loading graph…</div>}
        {query.isError && <div className="empty">Could not load the graph: {query.error.message}</div>}
        {built && built.empty && <div className="empty">No data for this graph in this flight.</div>}
        {built && !built.empty && <UPlotChart options={built.options} data={built.data} onCreate={onCreate} label={`${graphTitle(settings)} graph`} />}
      </div>
    </div>
  )
}

function ParamSelect({ label, value, exclude, onChange }: { label: string; value: SeriesParam; exclude?: SeriesParam; onChange: (p: SeriesParam) => void }) {
  return (
    <label className="field" style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
      <span className="visually-hidden">{label}</span>
      <select className="select select--compact" value={value} aria-label={label} onChange={(e) => onChange(e.target.value as SeriesParam)}>
        {PARAMS.filter((p) => p.param !== exclude).map((p) => (
          <option key={p.param} value={p.param}>
            {p.label}
          </option>
        ))}
      </select>
    </label>
  )
}

/* ---------- chart building ---------- */

interface Built {
  options: (width: number, height: number) => uPlot.Options
  data: uPlot.AlignedData
  legend: { key: string; label: string; color: string; title: string }[]
  empty: boolean
}

const EVENT_TYPE_COLORS: Partial<Record<EventType, string>> = {
  Emitter: '--color-series-emitter',
  Navigation: '--color-series-navigation',
  Countermeasure: '--color-series-countermeasure',
}

function build(g: GraphData, hidden: ReadonlySet<string>, _theme: number): Built {
  const css = getComputedStyle(document.documentElement)
  const v = (name: string) => css.getPropertyValue(name).trim()
  const colorAt = (i: number) => v(SERIES_COLORS[i % SERIES_COLORS.length])
  const axisBase = {
    stroke: v('--color-muted-foreground'),
    grid: { stroke: v('--color-border'), width: 1 },
    ticks: { stroke: v('--color-border'), width: 1 },
    font: `${css.getPropertyValue('--fs-xs').trim() || '11px'} 'IBM Plex Mono', monospace`,
  }
  const timeAxis: uPlot.Axis = { ...axisBase, space: 90, values: (_u, splits) => splits.map((ms) => formatElapsed(ms)) }
  const yAxis = (label: string): uPlot.Axis => ({ ...axisBase, label, labelFont: `${css.getPropertyValue('--fs-xs').trim() || '11px'} 'IBM Plex Sans', sans-serif`, size: 60 })
  const base = (width: number, height: number): Pick<uPlot.Options, 'width' | 'height' | 'legend' | 'padding'> => ({
    width,
    height,
    legend: { show: false },
    padding: [8, 12, 0, 0],
  })
  const cursor: uPlot.Cursor = { drag: { x: true, y: false, setScale: false }, points: { show: false } }

  switch (g.kind) {
    case 'parameter':
    case 'bearing': {
      const series = g.data.series
      const { x, ys } = alignOnTime(series)
      const legend = series.map((s, i) => ({ key: String(s.emitterId), label: `#${s.emitterId}`, color: colorAt(i), title: `Emitter ${s.emitterId}: ${s.t.length} points` }))
      const yLabel = g.kind === 'bearing' ? paramLabel('Bearing') : paramLabel(g.data.param)
      return {
        empty: x.length === 0,
        legend,
        data: [x, ...ys] as uPlot.AlignedData,
        options: (width, height) => ({
          ...base(width, height),
          cursor,
          scales: { x: { time: false }, y: g.kind === 'bearing' ? { range: [0, 360] } : {} },
          axes: [timeAxis, { ...yAxis(yLabel), ...(g.kind === 'bearing' ? { splits: () => [0, 90, 180, 270, 360] } : {}) }],
          series: [
            {},
            ...series.map((s, i) => ({
              label: `Emitter ${s.emitterId}`,
              show: !hidden.has(String(s.emitterId)),
              stroke: colorAt(i),
              width: 0,
              paths: () => null,
              points: { show: true, size: 4, width: 0, fill: colorAt(i), stroke: colorAt(i) },
            })),
          ],
        }),
      }
    }

    case 'scatter': {
      const series = g.data.series
      const legend = series.map((s, i) => ({ key: String(s.emitterId), label: `#${s.emitterId}`, color: colorAt(i), title: `Emitter ${s.emitterId}` }))
      return {
        empty: series.length === 0,
        legend,
        data: [null, ...series.map((s) => [s.x, s.y])] as unknown as uPlot.AlignedData,
        options: (width, height) => ({
          ...base(width, height),
          mode: 2,
          cursor: { drag: { x: true, y: true }, points: { show: false } },
          scales: { x: { time: false } },
          axes: [{ ...axisBase, label: paramLabel(g.data.x), labelFont: axisBase.font, values: (_u, s) => s.map((n) => formatNumber(n)) }, yAxis(paramLabel(g.data.y))],
          series: [
            {},
            ...series.map((s, i) => ({
              label: `Emitter ${s.emitterId}`,
              show: !hidden.has(String(s.emitterId)),
              stroke: colorAt(i),
              fill: colorAt(i),
              facets: [
                { scale: 'x', auto: true },
                { scale: 'y', auto: true },
              ],
              paths: squares(4),
            })),
          ],
        }),
      }
    }

    case 'profile': {
      const { t, altM, countermeasures } = g.data
      const nav = v('--color-series-navigation')
      const cm = v('--color-series-countermeasure')
      const showCm = !hidden.has('cm')
      return {
        empty: t.length === 0 && countermeasures.length === 0,
        legend: [
          { key: 'alt', label: 'Altitude', color: nav, title: 'Average altitude per bucket' },
          { key: 'cm', label: 'Countermeasures (C chaff · F flare)', color: cm, title: 'Releases' },
        ],
        data: [t, altM] as uPlot.AlignedData,
        options: (width, height) => ({
          ...base(width, height),
          cursor,
          scales: { x: { time: false, range: [g.data.window.fromMs, g.data.window.toMs] } },
          axes: [timeAxis, yAxis('Altitude (m)')],
          series: [{}, { label: 'Altitude', show: !hidden.has('alt'), stroke: nav, width: 1.5, points: { show: false } }],
          hooks: {
            draw: [
              (u) => {
                if (!showCm) return
                const ctx = u.ctx
                const dpr = window.devicePixelRatio || 1
                ctx.save()
                ctx.strokeStyle = cm
                ctx.fillStyle = cm
                ctx.lineWidth = dpr
                ctx.font = `${11 * dpr}px 'IBM Plex Mono', monospace`
                ctx.textAlign = 'center'
                // Releases are frequent: short ticks along the top, labels only where there is room.
                const top = u.bbox.top
                let lastLabel = -Infinity
                for (const mark of countermeasures) {
                  const x = u.valToPos(mark.t, 'x', true)
                  if (x < u.bbox.left || x > u.bbox.left + u.bbox.width) continue
                  ctx.globalAlpha = 0.85
                  ctx.beginPath()
                  ctx.moveTo(x, top + 14 * dpr)
                  ctx.lineTo(x, top + 26 * dpr)
                  ctx.stroke()
                  if (x - lastLabel >= 14 * dpr) {
                    ctx.globalAlpha = 1
                    ctx.fillText(mark.kind === 2 ? 'F' : 'C', x, top + 10 * dpr)
                    lastLabel = x
                  }
                }
                ctx.restore()
              },
            ],
          },
        }),
      }
    }

    case 'rate': {
      const { t, series } = g.data
      const legend = series.map((s, i) => ({
        key: s.eventType,
        label: s.eventType,
        color: v(EVENT_TYPE_COLORS[s.eventType] ?? SERIES_COLORS[i]),
        title: `${s.eventType} events per bucket`,
      }))
      return {
        empty: t.length === 0,
        legend,
        data: [t, ...series.map((s) => s.counts)] as uPlot.AlignedData,
        options: (width, height) => ({
          ...base(width, height),
          cursor,
          scales: { x: { time: false } },
          axes: [timeAxis, yAxis(`Events per ${formatElapsed(g.data.window.bucketMs).replace('T+', '')}`)],
          series: [
            {},
            ...series.map((s, i) => ({
              label: s.eventType,
              show: !hidden.has(s.eventType),
              stroke: legend[i].color,
              width: 1.5,
              points: { show: false },
            })),
          ],
        }),
      }
    }
  }
}

/** Scatter points as small squares (uPlot mode 2 path builder). */
function squares(size: number): uPlot.Series.PathBuilder {
  return (u, seriesIdx) => {
    const d = u.data[seriesIdx] as unknown as [number[], number[]]
    const [xs, ys] = d
    const s = size * (window.devicePixelRatio || 1)
    const path = new Path2D()
    for (let i = 0; i < xs.length; i++) {
      const x = u.valToPos(xs[i], 'x', true)
      const y = u.valToPos(ys[i], 'y', true)
      path.rect(x - s / 2, y - s / 2, s, s)
    }
    return { stroke: undefined, fill: path, clip: undefined }
  }
}

/**
 * Time graphs: drag selects a window (re-fetched at higher resolution), a click seeks playback,
 * and a playhead line follows the playback position.
 */
function attachTimeInteractions(u: uPlot, actor: PlaybackActor, seek: (ms: number) => void, zoom: (w: Window) => void) {
  const over = u.over
  const line = document.createElement('div')
  line.className = 'graph-playhead'
  over.appendChild(line)

  let downX = 0
  const onDown = (e: MouseEvent) => (downX = e.clientX)
  const onClick = (e: MouseEvent) => {
    if (Math.abs(e.clientX - downX) > 3) return // that was a drag
    seek(Math.max(0, u.posToVal(e.offsetX, 'x')))
  }
  over.addEventListener('mousedown', onDown)
  over.addEventListener('click', onClick)

  u.hooks.setSelect = [
    ...(u.hooks.setSelect ?? []),
    (chart) => {
      const { left, width } = chart.select
      if (width > 4) zoom({ fromMs: Math.max(0, Math.floor(chart.posToVal(left, 'x'))), toMs: Math.ceil(chart.posToVal(left + width, 'x')) })
      chart.setSelect({ left: 0, top: 0, width: 0, height: 0 }, false)
    },
  ]

  let last = -1
  let handle = requestAnimationFrame(function draw() {
    const pos = actor.getSnapshot().context.positionMs
    if (pos !== last) {
      const x = u.valToPos(pos, 'x')
      line.style.display = x >= 0 && x <= over.clientWidth ? 'block' : 'none'
      line.style.transform = `translateX(${x}px)`
      last = pos
    }
    handle = requestAnimationFrame(draw)
  })

  return () => {
    cancelAnimationFrame(handle)
    over.removeEventListener('mousedown', onDown)
    over.removeEventListener('click', onClick)
    line.remove()
  }
}

/** Bumps when the theme changes, so charts re-read their colours. */
function useThemeVersion(): number {
  const [version, setVersion] = useState(0)
  useEffect(() => {
    const observer = new MutationObserver(() => setVersion((v) => v + 1))
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'data-ui-size'] })
    return () => observer.disconnect()
  }, [])
  return version
}
