import { useEffect, useRef } from 'react'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { stepForSpeed, viewAt } from '../playback/playbackView'
import { drawRwr } from './rwrDraw'
import { buildThreats, dispensedBy, type ResolvedProfile } from './symbology'

interface RwrScopeProps {
  actor: PlaybackActor
  profile: ResolvedProfile
  /** The flight's countermeasure releases, sorted by time (for the remaining counter). */
  releases: readonly { t: number; kind: number; count: number }[]
  label: string
}

/**
 * The RWR display. Draws in its own requestAnimationFrame loop straight from the playback actor,
 * so 60 fps playback never re-renders React. Repaints when the playhead, data, profile or size
 * change, and continuously while a new threat is blinking.
 */
export function RwrScope({ actor, profile, releases, label }: RwrScopeProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const profileRef = useRef(profile)
  const releasesRef = useRef(releases)
  useEffect(() => {
    profileRef.current = profile
    releasesRef.current = releases
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

    let last = { position: -1, frames: null as unknown, profile: null as unknown, blinking: false }
    let handle = requestAnimationFrame(function draw(now) {
      const { positionMs, frames, speed } = actor.getSnapshot().context
      const currentProfile = profileRef.current
      if (size.w >= 120 && size.h >= 120) {
        const changed = dirty || positionMs !== last.position || frames !== last.frames || currentProfile !== last.profile
        if (changed || last.blinking) {
          const view = viewAt(frames, positionMs, stepForSpeed(speed))
          const threats = buildThreats(view.emitters, currentProfile)
          const load = currentProfile.display.countermeasures
          const used = load ? dispensedBy(releasesRef.current, positionMs) : null
          drawRwr(ctx, pixels.w, pixels.h, {
            profile: currentProfile,
            threats,
            remaining: load && used ? { chaff: load.chaff - used.chaff, flares: load.flares - used.flares } : null,
            persistenceMs: view.persistenceMs,
            now,
            still: reducedMotion.matches,
          })
          last = { position: positionMs, frames, profile: currentProfile, blinking: threats.some((t) => t.isNew) && !reducedMotion.matches }
          dirty = false
        }
      }
      handle = requestAnimationFrame(draw)
    })

    return () => {
      cancelAnimationFrame(handle)
      observer.disconnect()
    }
  }, [actor])

  return <canvas ref={canvasRef} className="rwr-canvas" role="img" aria-label={label} />
}
