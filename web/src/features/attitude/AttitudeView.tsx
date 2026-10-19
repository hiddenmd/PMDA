import { useEffect, useRef, useState } from 'react'
import type { FlightTrack } from '../../api/types'
import { RotateCcw } from '../../components/icons'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { drawAdi, drawHeadingTape, type AdiColors } from './adiDraw'
import { drawAircraft, type ModelColors } from './aircraft3d'
import { attitudeAt, type Attitude, type AttitudeSeries } from './attitude'
import type { ChaseScene } from './scene3d/ChaseScene'
import type { CameraPreset } from './scene3d/frame'

/** Instrument colours are the same in both themes, like the real thing; the frame around them follows the theme. */
const INSTRUMENT = { sky: '#3c82c4', skyHigh: '#1f5a94', ground: '#6b4423', groundLow: '#4a2f17', line: '#f4f4f5', symbol: '#fbbf24' }
const MODEL: Omit<ModelColors, 'grid'> = { body: '#b4bcc8', wing: '#8f99a6', canopy: '#3d6286', rotor: '#a3acb8' }
const READOUT_EVERY_MS = 100
const PRESETS: { id: CameraPreset; label: string }[] = [
  { id: 'chase', label: 'Chase' },
  { id: 'side', label: 'Side' },
  { id: 'top', label: 'Top' },
]
/** Degrees of orbit per pixel dragged. */
const ORBIT_PER_PX = 0.4

export type AttitudeReadout = Partial<Record<keyof Attitude, HTMLElement | null>>

interface AttitudeViewProps {
  actor: PlaybackActor
  series: AttitudeSeries
  track: FlightTrack
  platform: 'FixedWing' | 'Helicopter'
  readout: React.RefObject<AttitudeReadout>
}

const sign = (v: number, digits: number) => {
  const text = Math.abs(v).toFixed(digits)
  return Number(text) === 0 ? text : `${v > 0 ? '+' : '−'}${text}`
}
const FORMAT: Record<keyof Attitude, (v: number) => string> = {
  pitch: (v) => `${sign(v, 1)}°`,
  roll: (v) => (Math.abs(v) < 0.5 ? '0°' : `${Math.abs(v).toFixed(1)}° ${v > 0 ? 'R' : 'L'}`),
  heading: (v) => `${Math.round(v) % 360}`.padStart(3, '0') + '°',
  rollRate: (v) => `${sign(v, 1)}°/s`,
  pitchRate: (v) => `${sign(v, 1)}°/s`,
  turnRate: (v) => `${sign(v, 1)}°/s`,
  verticalSpeed: (v) => `${sign(Math.round((v * 196.85) / 10) * 10, 0)} ft/min`,
  groundSpeed: (v) => `${Math.round(v * 1.943844)} kt`,
  loadFactor: (v) => `${v.toFixed(2)} g`,
}

/**
 * The aircraft in a 3D chase view (three.js, loaded on first use) with a glass attitude indicator and
 * heading tape over it, all following the playhead in one animation-frame loop (no React re-render
 * during playback). Where WebGL is unavailable the aircraft is drawn in 2D as before.
 */
export function AttitudeView({ actor, series, track, platform, readout }: AttitudeViewProps) {
  const stageRef = useRef<HTMLDivElement>(null)
  const sceneCanvasRef = useRef<HTMLCanvasElement>(null)
  const adiRef = useRef<HTMLCanvasElement>(null)
  const tapeRef = useRef<HTMLCanvasElement>(null)
  const sceneRef = useRef<ChaseScene | null>(null)
  const [preset, setPreset] = useState<CameraPreset>('chase')
  const presetRef = useRef<CameraPreset>('chase')
  const kickRef = useRef<() => void>(() => {})

  useEffect(() => {
    presetRef.current = preset
    sceneRef.current?.setPreset(preset)
    kickRef.current()
  }, [preset])

  useEffect(() => {
    const sceneCanvas = sceneCanvasRef.current!
    const adi = adiRef.current!
    const tape = tapeRef.current!
    const adiCtx = adi.getContext('2d')
    const tapeCtx = tape.getContext('2d')
    if (!adiCtx || !tapeCtx) return
    const reducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
    let disposed = false
    let fallbackCtx: CanvasRenderingContext2D | null = null
    let visible = true
    let dirty = true
    let handle = 0
    let lastPosition = -1
    let lastReadout = Number.NEGATIVE_INFINITY
    let readoutStale = false
    const size = new Map<HTMLCanvasElement, { w: number; h: number }>()

    const token = (name: string) => getComputedStyle(stageRef.current!).getPropertyValue(name).trim()
    const sceneColors = () => ({ background: token('--color-background'), grid: token('--color-border-strong'), gridMinor: token('--color-border'), accent: token('--color-primary') })

    const fit = (canvas: HTMLCanvasElement, ctx: CanvasRenderingContext2D | null) => {
      const dpr = window.devicePixelRatio || 1
      const rect = canvas.getBoundingClientRect()
      size.set(canvas, { w: rect.width, h: rect.height })
      if (canvas === sceneCanvas && sceneRef.current) {
        sceneRef.current.resize(rect.width, rect.height, Math.min(dpr, 2))
        return
      }
      canvas.width = Math.max(1, Math.round(rect.width * dpr))
      canvas.height = Math.max(1, Math.round(rect.height * dpr))
      ctx?.setTransform(dpr, 0, 0, dpr, 0, 0)
    }
    const resize = () => {
      fit(adi, adiCtx)
      fit(tape, tapeCtx)
      fit(sceneCanvas, fallbackCtx)
      kick()
    }

    function kick() {
      dirty = true
      if (!handle && visible && !document.hidden) handle = requestAnimationFrame(frame)
    }
    kickRef.current = kick

    // One loop while the view is on screen, as before: reading the playhead is cheap, and the 3D scene
    // only re-renders when its frame, camera damping or rotors need it.
    function frame(now: number) {
      handle = visible && !document.hidden ? requestAnimationFrame(frame) : 0
      const { positionMs } = actor.getSnapshot().context
      const a = attitudeAt(series, positionMs)
      if (a && (positionMs !== lastPosition || dirty)) {
        lastPosition = positionMs
        readoutStale = true
        paintInstruments(a)
        if (sceneRef.current) sceneRef.current.setFrame({ attitude: a, track, ms: positionMs })
        else if (fallbackCtx) paintFallback(a)
      }
      dirty = false
      sceneRef.current?.render(now)
      if (readoutStale && now - lastReadout >= READOUT_EVERY_MS) {
        lastReadout = now
        readoutStale = false
        const r = readout.current
        if (a) for (const key of Object.keys(FORMAT) as (keyof Attitude)[]) if (r[key]) r[key]!.textContent = FORMAT[key](a[key])
      }
    }

    function paintInstruments(a: Attitude) {
      const colors: AdiColors = { ...INSTRUMENT, text: token('--color-foreground'), muted: token('--color-muted-foreground'), bezel: token('--color-border-strong'), panel: token('--color-card'), accent: token('--color-primary') }
      const s = size.get(adi) ?? { w: 0, h: 0 }
      adiCtx!.clearRect(0, 0, s.w, s.h)
      const r = Math.max(10, Math.min(s.w, s.h) / 2 - 4)
      drawAdi(adiCtx!, s.w / 2, s.h / 2, r, a.pitch, a.roll, colors)
      const t = size.get(tape) ?? { w: 0, h: 0 }
      tapeCtx!.clearRect(0, 0, t.w, t.h)
      drawHeadingTape(tapeCtx!, 0, 0, t.w, t.h, a.heading, colors)
    }

    function paintFallback(a: Attitude) {
      const m = size.get(sceneCanvas) ?? { w: 0, h: 0 }
      fallbackCtx!.clearRect(0, 0, m.w, m.h)
      drawAircraft(fallbackCtx!, m.w / 2, m.h * 0.5, Math.min(m.w, m.h * 1.2), platform, a.pitch, a.roll, { ...MODEL, grid: token('--color-border') })
    }

    void import('./scene3d/ChaseScene')
      .then(({ createChaseScene }) => createChaseScene(sceneCanvas, platform, reducedMotion))
      .catch(() => null)
      .then((scene) => {
        if (disposed) return scene?.dispose()
        if (scene) {
          sceneRef.current = scene
          scene.setColors(sceneColors())
          scene.setPreset(presetRef.current)
        } else {
          fallbackCtx = sceneCanvas.getContext('2d')
        }
        resize()
      })

    resize()
    const resizeObserver = new ResizeObserver(resize)
    ;[adi, tape, sceneCanvas].forEach((c) => resizeObserver.observe(c))
    const visibility = new IntersectionObserver(([entry]) => {
      visible = entry?.isIntersecting ?? true
      if (visible) kick()
    })
    visibility.observe(stageRef.current!)
    const onPageVisibility = () => !document.hidden && kick()
    document.addEventListener('visibilitychange', onPageVisibility)
    const themeObserver = new MutationObserver(() => {
      sceneRef.current?.setColors(sceneColors())
      kick()
    })
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })

    // Drag to orbit, double-click to reset.
    let drag: { x: number; y: number } | null = null
    const down = (e: PointerEvent) => {
      drag = { x: e.clientX, y: e.clientY }
      sceneCanvas.setPointerCapture?.(e.pointerId)
    }
    const move = (e: PointerEvent) => {
      if (!drag || !sceneRef.current) return
      sceneRef.current.orbitBy(-(e.clientX - drag.x) * ORBIT_PER_PX, (e.clientY - drag.y) * ORBIT_PER_PX)
      drag = { x: e.clientX, y: e.clientY }
      kick()
    }
    const up = () => (drag = null)
    const reset = () => {
      sceneRef.current?.resetView()
      kick()
    }
    sceneCanvas.addEventListener('pointerdown', down)
    sceneCanvas.addEventListener('pointermove', move)
    sceneCanvas.addEventListener('pointerup', up)
    sceneCanvas.addEventListener('pointercancel', up)
    sceneCanvas.addEventListener('dblclick', reset)

    return () => {
      disposed = true
      cancelAnimationFrame(handle)
      resizeObserver.disconnect()
      visibility.disconnect()
      themeObserver.disconnect()
      document.removeEventListener('visibilitychange', onPageVisibility)
      sceneCanvas.removeEventListener('pointerdown', down)
      sceneCanvas.removeEventListener('pointermove', move)
      sceneCanvas.removeEventListener('pointerup', up)
      sceneCanvas.removeEventListener('pointercancel', up)
      sceneCanvas.removeEventListener('dblclick', reset)
      sceneRef.current?.dispose()
      sceneRef.current = null
      kickRef.current = () => {}
    }
  }, [actor, series, track, platform, readout])

  return (
    <div className="attitude-view" ref={stageRef}>
      <canvas ref={sceneCanvasRef} className="attitude-view__scene" role="img" aria-label="Aircraft seen from behind, pitched and banked as at the playhead, with its recent flight path" />
      <canvas ref={tapeRef} className="attitude-view__tape" role="img" aria-label="Heading tape at the playhead" />
      <div className="attitude-view__adi-card">
        <canvas ref={adiRef} className="attitude-view__adi" role="img" aria-label="Attitude indicator: pitch and bank at the playhead" />
      </div>
      <div className="attitude-view__toolbar">
        <div className="segmented" role="group" aria-label="Camera view">
          {PRESETS.map((p) => (
            <button key={p.id} type="button" aria-pressed={preset === p.id} onClick={() => setPreset(p.id)}>
              {p.label}
            </button>
          ))}
        </div>
        <button
          type="button"
          className="btn btn--ghost btn--icon btn--compact"
          aria-label="Reset view"
          title="Reset view (or double-click the view)"
          onClick={() => {
            sceneRef.current?.resetView()
            kickRef.current()
          }}
        >
          <RotateCcw size={16} aria-hidden />
        </button>
      </div>
    </div>
  )
}
