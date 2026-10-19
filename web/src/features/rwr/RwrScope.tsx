import { useEffect, useRef } from 'react'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { stepForSpeed, viewAt } from '../playback/playbackView'
import { drawRwr } from './rwrDraw'
import { drawTactical, type TacticalTokens } from './rwrTactical'
import { buildThreats, dispensedBy, type ResolvedProfile } from './symbology'

/** Cockpit mirrors the customer's display; Tactical draws the same threats in the HUD style. */
export type RwrLook = 'cockpit' | 'tactical'

const css = (el: Element, name: string) => getComputedStyle(el).getPropertyValue(name).trim()
const tacticalTokens = (el: Element): TacticalTokens => ({
  background: css(el, '--color-background'),
  foreground: css(el, '--color-foreground'),
  muted: css(el, '--color-muted-foreground'),
  border: css(el, '--color-border'),
  borderStrong: css(el, '--color-border-strong'),
  primary: css(el, '--color-primary'),
  critical: css(el, '--color-alert-critical'),
  warning: css(el, '--color-alert-warning'),
})

interface RwrScopeProps {
  actor: PlaybackActor
  profile: ResolvedProfile
  look: RwrLook
  /** The flight's countermeasure releases, sorted by time (for the remaining counter). */
  releases: readonly { t: number; kind: number; count: number }[]
  label: string
}

/**
 * The RWR display. Draws in its own requestAnimationFrame loop straight from the playback actor,
 * so 60 fps playback never re-renders React. Repaints when the playhead, data, profile or size
 * change, and continuously while a new threat is blinking.
 */
export function RwrScope({ actor, profile, look, releases, label }: RwrScopeProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const profileRef = useRef(profile)
  const releasesRef = useRef(releases)
  const lookRef = useRef(look)
  useEffect(() => {
    profileRef.current = profile
    releasesRef.current = releases
    lookRef.current = look
  })

  useEffect(() => {
    const canvas = canvasRef.current!
    const ctx = canvas.getContext('2d')
    if (!ctx) return
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)')
    let size = { w: 0, h: 0 } // CSS pixels, for the minimum-size check
    let pixels = { w: 0, h: 0 } // device pixels, what we draw in
    let dirty = true

    // Draw in device pixels, sized to exactly what the screen shows. Rounding the CSS size first left the backing
    // store a fraction smaller than its box (and fractional at 125/150% scaling), so the browser resampled and blurred it.
    const resize = (entry?: ResizeObserverEntry) => {
      const rect = canvas.getBoundingClientRect()
      const dpr = window.devicePixelRatio || 1
      const device = entry?.devicePixelContentBoxSize?.[0]
      size = { w: rect.width, h: rect.height }
      pixels = {
        w: Math.max(1, device ? device.inlineSize : Math.round(rect.width * dpr)),
        h: Math.max(1, device ? device.blockSize : Math.round(rect.height * dpr)),
      }
      canvas.width = pixels.w
      canvas.height = pixels.h
      ctx.setTransform(1, 0, 0, 1, 0, 0)
      dirty = true
    }
    resize()
    const observer = new ResizeObserver(([entry]) => resize(entry))
    try {
      observer.observe(canvas, { box: 'device-pixel-content-box' }) // also fires on zoom and monitor scale changes
    } catch {
      observer.observe(canvas) // Safari
    }

    // The Tactical look takes its colours from the theme: repaint when it changes.
    let tokens: TacticalTokens | null = null
    const themeObserver = new MutationObserver(() => {
      tokens = null
      dirty = true
    })
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })

    let last = { position: -1, frames: null as unknown, profile: null as unknown, look: null as RwrLook | null, blinking: false }
    let handle = requestAnimationFrame(function draw(now) {
      const { positionMs, frames, speed } = actor.getSnapshot().context
      const currentProfile = profileRef.current
      const currentLook = lookRef.current
      if (size.w >= 120 && size.h >= 120) {
        const changed = dirty || positionMs !== last.position || frames !== last.frames || currentProfile !== last.profile || currentLook !== last.look
        if (changed || last.blinking) {
          const view = viewAt(frames, positionMs, stepForSpeed(speed))
          const threats = buildThreats(view.emitters, currentProfile)
          const load = currentProfile.display.countermeasures
          const used = load ? dispensedBy(releasesRef.current, positionMs) : null
          const frame = {
            profile: currentProfile,
            threats,
            remaining: load && used ? { chaff: load.chaff - used.chaff, flares: load.flares - used.flares } : null,
            persistenceMs: view.persistenceMs,
            now,
            still: reducedMotion.matches,
          }
          if (currentLook === 'tactical') drawTactical(ctx, pixels.w, pixels.h, frame, (tokens ??= tacticalTokens(canvas)))
          else drawRwr(ctx, pixels.w, pixels.h, frame)
          last = { position: positionMs, frames, profile: currentProfile, look: currentLook, blinking: threats.some((t) => t.isNew) && !reducedMotion.matches }
          dirty = false
        }
      }
      handle = requestAnimationFrame(draw)
    })

    return () => {
      cancelAnimationFrame(handle)
      observer.disconnect()
      themeObserver.disconnect()
    }
  }, [actor])

  return <canvas ref={canvasRef} className="rwr-canvas" role="img" aria-label={label} />
}
