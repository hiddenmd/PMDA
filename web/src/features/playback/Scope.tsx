import { useEffect, useRef } from 'react'
import type { ActorRefFrom } from 'xstate'
import type { playbackMachine } from '../../machines/playbackMachine'
import { stepForSpeed, viewAt, type ActiveEmitter } from './playbackView'
import { bandOf, scopePoint, type BandShape } from './scopeGeometry'

type PlaybackActor = ActorRefFrom<typeof playbackMachine>

const LABEL_MARGIN = 18
/** Like an RWR, only the strongest threats get an ID label; the rest stay readable as symbols. */
const MAX_LABELS = 12
const MIN_SIZE = LABEL_MARGIN * 2 + 40

interface Palette {
  ring: string
  label: string
  symbol: string
  text: string
  background: string
}

/**
 * RWR scope on a canvas. It draws in its own requestAnimationFrame loop straight from the actor
 * snapshot, so playback never re-renders React at 60 fps; it skips frames when nothing changed.
 */
export function Scope({ actor, label }: { actor: PlaybackActor; label: string }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = canvasRef.current!
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)')
    let palette = readPalette(canvas)
    let size = 0
    let dirty = true
    let lastPosition = -1
    let lastFrames: unknown = null

    const resize = () => {
      const rect = canvas.getBoundingClientRect()
      const dpr = window.devicePixelRatio || 1
      size = Math.max(1, Math.floor(rect.width))
      canvas.width = size * dpr
      canvas.height = size * dpr
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
      palette = readPalette(canvas)
      dirty = true
    }
    resize() // measure now: the first animation frame can come before the observer's first callback
    const observer = new ResizeObserver(resize)
    observer.observe(canvas)
    // Theme switches change the CSS variables the palette comes from.
    const themeObserver = new MutationObserver(() => {
      palette = readPalette(canvas)
      dirty = true
    })
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })

    let handle = requestAnimationFrame(function draw() {
      const { positionMs, frames, speed } = actor.getSnapshot().context
      // Too small to hold a scope (not laid out yet, or hidden): wait for a resize.
      if (size >= MIN_SIZE && (dirty || positionMs !== lastPosition || frames !== lastFrames)) {
        const view = viewAt(frames, positionMs, stepForSpeed(speed))
        paint(ctx, size, palette, view.emitters, view.persistenceMs, reducedMotion.matches)
        lastPosition = positionMs
        lastFrames = frames
        dirty = false
      }
      handle = requestAnimationFrame(draw)
    })

    return () => {
      cancelAnimationFrame(handle)
      observer.disconnect()
      themeObserver.disconnect()
    }
  }, [actor])

  return (
    <div className="scope">
      <canvas ref={canvasRef} role="img" aria-label={label} />
    </div>
  )
}

function readPalette(el: Element): Palette {
  const css = getComputedStyle(el)
  const v = (name: string) => css.getPropertyValue(name).trim()
  return {
    ring: v('--color-border'),
    label: v('--color-muted-foreground'),
    symbol: v('--color-accent'),
    text: v('--color-foreground'),
    background: v('--color-muted'),
  }
}

function paint(ctx: CanvasRenderingContext2D, size: number, p: Palette, emitters: ActiveEmitter[], persistenceMs: number, still: boolean) {
  const c = size / 2
  const radius = c - LABEL_MARGIN
  ctx.clearRect(0, 0, size, size)

  // Face
  ctx.fillStyle = p.background
  ctx.beginPath()
  ctx.arc(c, c, radius, 0, Math.PI * 2)
  ctx.fill()

  ctx.strokeStyle = p.ring
  ctx.lineWidth = 1
  for (const f of [0.25, 0.5, 0.75, 1]) {
    ctx.beginPath()
    ctx.arc(c, c, radius * f, 0, Math.PI * 2)
    ctx.stroke()
  }
  for (let deg = 0; deg < 360; deg += 30) {
    const t = (deg * Math.PI) / 180
    const inner = deg % 90 === 0 ? 0 : radius * 0.92
    ctx.beginPath()
    ctx.moveTo(c + inner * Math.sin(t), c - inner * Math.cos(t))
    ctx.lineTo(c + radius * Math.sin(t), c - radius * Math.cos(t))
    ctx.stroke()
  }

  ctx.fillStyle = p.label
  ctx.font = '11px "Fira Code", monospace'
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  for (const [deg, text] of [
    [0, '0'],
    [90, '90'],
    [180, '180'],
    [270, '270'],
  ] as const) {
    const t = (deg * Math.PI) / 180
    ctx.fillText(text, c + (radius + 10) * Math.sin(t), c - (radius + 10) * Math.cos(t))
  }

  // Aircraft at the centre
  ctx.strokeStyle = p.text
  ctx.lineWidth = 1.5
  ctx.beginPath()
  ctx.moveTo(c, c - 7)
  ctx.lineTo(c, c + 6)
  ctx.moveTo(c - 6, c)
  ctx.lineTo(c + 6, c)
  ctx.stroke()

  // Emitters: oldest first so the freshest draw on top; symbols fade with age unless motion is reduced.
  // `emitters` arrives strongest first, which decides who gets a label.
  const labelled = new Set(emitters.slice(0, MAX_LABELS).map((e) => e.emitterId))
  ctx.font = '600 11px "Fira Code", monospace'
  for (const e of [...emitters].sort((a, b) => b.ageMs - a.ageMs)) {
    const { x, y } = scopePoint(e.bearingDeg, e.amplitudeDb, c, c, radius)
    ctx.globalAlpha = still ? 1 : Math.max(0.25, 1 - e.ageMs / persistenceMs)
    ctx.strokeStyle = p.symbol
    ctx.fillStyle = p.symbol
    ctx.lineWidth = 2
    drawSymbol(ctx, bandOf(e.frequencyMHz).shape, x, y, 7)
    if (labelled.has(e.emitterId)) {
      ctx.fillStyle = p.text
      ctx.fillText(String(e.emitterId), x, y - 14)
    }
  }
  ctx.globalAlpha = 1
}

function drawSymbol(ctx: CanvasRenderingContext2D, shape: BandShape, x: number, y: number, r: number) {
  ctx.beginPath()
  switch (shape) {
    case 'triangle':
      ctx.moveTo(x, y - r)
      ctx.lineTo(x + r, y + r * 0.8)
      ctx.lineTo(x - r, y + r * 0.8)
      ctx.closePath()
      break
    case 'square':
      ctx.rect(x - r * 0.8, y - r * 0.8, r * 1.6, r * 1.6)
      break
    case 'diamond':
      ctx.moveTo(x, y - r)
      ctx.lineTo(x + r, y)
      ctx.lineTo(x, y + r)
      ctx.lineTo(x - r, y)
      ctx.closePath()
      break
    case 'circle':
      ctx.arc(x, y, r * 0.85, 0, Math.PI * 2)
      break
  }
  ctx.stroke()
}
