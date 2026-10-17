import { useEffect, useRef } from 'react'
import type { EventType, FlightOverview } from '../../api/types'
import { formatElapsed, formatUtcClock } from '../../lib/format'

const SERIES: { type: EventType; label: string; color: string }[] = [
  { type: 'Navigation', label: 'Navigation', color: '--color-series-navigation' },
  { type: 'Emitter', label: 'Emitters', color: '--color-series-emitter' },
  { type: 'Countermeasure', label: 'Countermeasures', color: '--color-series-countermeasure' },
]

interface TimelineProps {
  overview: FlightOverview | undefined
  positionMs: number
  durationMs: number
  startUtc: string
  onSeek: (positionMs: number) => void
}

/** Event-density strip with a native range input on top as the scrubber (keyboard and screen-reader friendly). */
export function Timeline({ overview, positionMs, durationMs, startUtc, onSeek }: TimelineProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    const ctx = canvas?.getContext('2d')
    if (!canvas || !ctx || !overview) return

    const draw = () => {
      const rect = canvas.getBoundingClientRect()
      const dpr = window.devicePixelRatio || 1
      canvas.width = Math.max(1, Math.floor(rect.width * dpr))
      canvas.height = Math.max(1, Math.floor(rect.height * dpr))
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
  }, [overview])

  const clock = formatUtcClock(new Date(Date.parse(startUtc) + positionMs))
  return (
    <div className="transport-timeline">
      <div className="timeline">
        <canvas ref={canvasRef} aria-hidden />
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
      <div className="timeline__legend" aria-hidden>
        {SERIES.map((s) => (
          <span key={s.type}>
            <span className="legend-swatch" style={{ background: `var(${s.color})` }} />
            {s.label}
          </span>
        ))}
      </div>
    </div>
  )
}
