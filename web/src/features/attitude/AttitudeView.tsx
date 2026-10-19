import { useEffect, useRef } from 'react'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { drawAdi, drawHeadingTape, type AdiColors } from './adiDraw'
import { drawAircraft, type ModelColors } from './aircraft3d'
import { attitudeAt, type Attitude, type AttitudeSeries } from './attitude'

/** Instrument colours are the same in both themes, like the real thing; the frame around them follows the theme. */
const INSTRUMENT = { sky: '#3c82c4', skyHigh: '#1f5a94', ground: '#6b4423', groundLow: '#4a2f17', line: '#f4f4f5', symbol: '#fbbf24' }
const MODEL: Omit<ModelColors, 'grid'> = { body: '#b4bcc8', wing: '#8f99a6', canopy: '#3d6286', rotor: '#a3acb8' }
const READOUT_EVERY_MS = 100

export type AttitudeReadout = Partial<Record<keyof Attitude, HTMLElement | null>>

interface AttitudeViewProps {
  actor: PlaybackActor
  series: AttitudeSeries
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
 * Attitude indicator with heading tape, and a chase view of the aircraft, both following the
 * playhead in their own animation-frame loop (no React re-render during playback).
 */
export function AttitudeView({ actor, series, platform, readout }: AttitudeViewProps) {
  const adiRef = useRef<HTMLCanvasElement>(null)
  const modelRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvases = [adiRef.current!, modelRef.current!]
    const contexts = canvases.map((c) => c.getContext('2d'))
    if (contexts.some((c) => !c)) return
    const [adiCtx, modelCtx] = contexts as CanvasRenderingContext2D[]
    let sizes = canvases.map(() => ({ w: 0, h: 0 }))
    let dirty = true

    const resize = () => {
      const dpr = window.devicePixelRatio || 1
      sizes = canvases.map((canvas, i) => {
        const rect = canvas.getBoundingClientRect()
        canvas.width = Math.max(1, Math.round(rect.width * dpr))
        canvas.height = Math.max(1, Math.round(rect.height * dpr))
        contexts[i]!.setTransform(canvas.width / Math.max(1, rect.width), 0, 0, canvas.height / Math.max(1, rect.height), 0, 0)
        return { w: rect.width, h: rect.height }
      })
      dirty = true
    }
    resize()
    const observer = new ResizeObserver(resize)
    canvases.forEach((c) => observer.observe(c))
    const themeObserver = new MutationObserver(() => (dirty = true))
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })

    let lastPosition = -1
    let lastReadout = Number.NEGATIVE_INFINITY
    let readoutStale = false
    let handle = requestAnimationFrame(function draw(now) {
      handle = requestAnimationFrame(draw)
      const { positionMs } = actor.getSnapshot().context
      if (positionMs !== lastPosition || dirty) {
        lastPosition = positionMs
        dirty = false
        readoutStale = true
        const a = attitudeAt(series, positionMs)
        if (a) paint(a)
      }
      if (readoutStale && now - lastReadout >= READOUT_EVERY_MS) {
        lastReadout = now
        readoutStale = false
        const a = attitudeAt(series, positionMs)
        const r = readout.current
        if (a) for (const key of Object.keys(FORMAT) as (keyof Attitude)[]) if (r[key]) r[key]!.textContent = FORMAT[key](a[key])
      }
    })

    function paint(a: Attitude) {
      const css = getComputedStyle(canvases[0])
      const token = (name: string) => css.getPropertyValue(name).trim()
      const colors: AdiColors = { ...INSTRUMENT, text: token('--color-foreground'), muted: token('--color-muted-foreground'), bezel: token('--color-border-strong'), accent: token('--color-primary') }

      const { w, h } = sizes[0]
      adiCtx.clearRect(0, 0, w, h)
      const tape = Math.max(22, Math.min(34, h * 0.1))
      const r = Math.max(10, Math.min(w, h - tape - 12) / 2 - 6)
      const cy = 6 + r
      drawAdi(adiCtx, w / 2, cy, r, a.pitch, a.roll, colors)
      drawHeadingTape(adiCtx, w / 2 - r, cy + r + 8, r * 2, tape, a.heading, colors)

      const m = sizes[1]
      modelCtx.clearRect(0, 0, m.w, m.h)
      drawAircraft(modelCtx, m.w / 2, m.h * 0.45, Math.min(m.w, m.h * 1.2), platform, a.pitch, a.roll, { ...MODEL, grid: token('--color-border') })
    }

    return () => {
      cancelAnimationFrame(handle)
      observer.disconnect()
      themeObserver.disconnect()
    }
  }, [actor, series, platform, readout])

  return (
    <div className="attitude-view">
      <canvas ref={adiRef} className="attitude-view__adi" role="img" aria-label="Attitude indicator: pitch, bank and heading at the playhead" />
      <canvas ref={modelRef} className="attitude-view__model" role="img" aria-label="Aircraft seen from behind, pitched and banked as at the playhead" />
    </div>
  )
}
