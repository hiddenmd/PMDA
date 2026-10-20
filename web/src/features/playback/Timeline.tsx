import { useEffect, useMemo, useRef } from 'react'
import type { Alert, EventType, FlightOverview } from '../../api/types'
import { formatElapsed, formatUtcClock } from '../../lib/format'
import { ALERTS } from '../alerts/alerts'
import { timeTicks } from './timeAxis'

const SERIES: { type: EventType; label: string; color: string }[] = [
  { type: 'Navigation', label: 'Navigation', color: '--color-series-navigation' },
  { type: 'Emitter', label: 'Emitters', color: '--color-series-emitter' },
  { type: 'Countermeasure', label: 'Countermeasures', color: '--color-series-countermeasure' },
]

/** Critical alerts as full-height red lines, warnings as short amber ticks; new-threat tones are too many to mark. */
const MARKS = {
  critical: { label: 'Missile alerts', color: '--color-alert-critical' },
  warning: { label: 'Lock / countermeasure alerts', color: '--color-alert-warning' },
} as const

interface TimelineProps {
  overview: FlightOverview | undefined
  alerts: readonly Alert[] | undefined
  positionMs: number
  durationMs: number
  startUtc: string
  onSeek: (positionMs: number) => void
}

/** Event-density strip with alert marks, a native range input on top as the scrubber, and a UTC time axis. */
export function Timeline({ overview, alerts, positionMs, durationMs, startUtc, onSeek }: TimelineProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const startEpoch = Date.parse(startUtc)

  useEffect(() => {
    const canvas = canvasRef.current
    const ctx = canvas?.getContext('2d')
    if (!canvas || !ctx || !overview) return

    const draw = () => {
      const rect = canvas.getBoundingClientRect()
      const dpr = window.devicePixelRatio || 1
      canvas.width = Math.max(1, Math.round(rect.width * dpr))
      canvas.height = Math.max(1, Math.round(rect.height * dpr))
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
      ctx.clearRect(0, 0, rect.width, rect.height)

      const css = getComputedStyle(canvas)
      const totals = new Array<number>(overview.buckets).fill(0)
      for (const s of overview.series) s.counts.forEach((n, i) => (totals[i] += n))
      // Square root keeps quiet stretches visible next to dense ones.
      const max = Math.sqrt(Math.max(1, ...totals))
      const barWidth = rect.width / overview.buckets

      for (let i = 0; i < overview.buckets; i++) {
        let y = rect.height
        const height = (Math.sqrt(totals[i]) / max) * (rect.height - 2)
        for (const s of SERIES) {
          const count = overview.series.find((x) => x.eventType === s.type)?.counts[i] ?? 0
          if (count === 0 || totals[i] === 0) continue
          const h = (count / totals[i]) * height
          ctx.fillStyle = css.getPropertyValue(s.color).trim()
          ctx.fillRect(i * barWidth, y - h, Math.max(1, barWidth - 0.5), h)
          y -= h
        }
      }

      // Warnings first so missile lines draw on top of them.
      const span = Math.max(1, durationMs)
      for (const severity of ['warning', 'critical'] as const) {
        ctx.fillStyle = css.getPropertyValue(MARKS[severity].color).trim()
        for (const a of alerts ?? []) {
          if (ALERTS[a.kind].severity !== severity) continue
          const x = Math.round((a.offsetMs / span) * rect.width)
          if (severity === 'critical') {
            ctx.fillRect(x - 1, 0, 2, rect.height)
            ctx.beginPath() // a small flag on top so a single line still catches the eye
            ctx.moveTo(x - 4, 0)
            ctx.lineTo(x + 4, 0)
            ctx.lineTo(x, 5)
            ctx.fill()
          } else {
            ctx.fillRect(x - 0.5, 0, 1.5, rect.height * 0.4)
          }
        }
      }
    }

    draw()
    const observer = new ResizeObserver(draw)
    observer.observe(canvas)
    const themeObserver = new MutationObserver(draw)
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
    return () => {
      observer.disconnect()
      themeObserver.disconnect()
    }
  }, [overview, alerts, durationMs])

  const ticks = useMemo(() => timeTicks(startEpoch, durationMs, 10), [startEpoch, durationMs])
  const clock = formatUtcClock(new Date(startEpoch + positionMs))
  const at = `${(Math.min(Math.max(positionMs / Math.max(1, durationMs), 0), 1) * 100).toFixed(3)}%`

  return (
    <div className="transport-timeline">
      <div className="timeline">
        <canvas ref={canvasRef} aria-hidden />
        {/* The played part is dimmed and the playhead drawn on top, so the position reads even over dense data. */}
        <div className="timeline__played" style={{ width: at }} aria-hidden />
        <div className="timeline__playhead" style={{ left: at }} aria-hidden />
        <input
          type="range"
          aria-label="Playback position"
          min={0}
          max={durationMs}
          step={100}
          value={Math.round(positionMs)}
          aria-valuetext={`${formatElapsed(positionMs)}, ${clock}`}
          onChange={(e) => onSeek(Number(e.target.value))}
        />
      </div>
      <div className="timeline__axis" aria-hidden>
        <span className="timeline__edge" title="Flight start (UTC)">
          {formatUtcClock(new Date(startEpoch))}
        </span>
        {ticks.map((t) => (
          <span key={t.offsetMs} className="timeline__tick" style={{ left: `${(t.offsetMs / durationMs) * 100}%` }}>
            {t.label}
          </span>
        ))}
        <span className="timeline__edge timeline__edge--end" title="Flight end (UTC)">
          {formatUtcClock(new Date(startEpoch + durationMs))}
        </span>
      </div>
    </div>
  )
}

/** What the strip's colours and marks mean; compact enough to sit in the transport row. */
export function TimelineLegend({ alerts }: { alerts: readonly Alert[] | undefined }) {
  const severities = useMemo(() => new Set((alerts ?? []).map((a) => ALERTS[a.kind].severity)), [alerts])
  return (
    <div className="timeline__legend" aria-hidden>
      {SERIES.map((s) => (
        <span key={s.type}>
          <span className="legend-swatch" style={{ background: `var(${s.color})` }} />
          {s.label}
        </span>
      ))}
      {(['critical', 'warning'] as const)
        .filter((s) => severities.has(s))
        .map((s) => (
          <span key={s}>
            <span className="legend-swatch legend-swatch--line" style={{ background: `var(${MARKS[s].color})` }} />
            {MARKS[s].label}
          </span>
        ))}
    </div>
  )
}
