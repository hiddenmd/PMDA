import { useQuery } from '@tanstack/react-query'
import { useSelector } from '@xstate/react'
import { useEffect, useMemo } from 'react'
import { getOverview } from '../../api/playback'
import { transportStatus, type PlaybackContext, type PlaybackEvent } from '../../machines/playbackMachine'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { SPEEDS, stepForSpeed, viewAt, type Speed } from './playbackView'

/** Text panels refresh about 10 times per real second; canvases draw themselves every frame. */
const UI_REFRESH_MS = 100
export const OVERVIEW_BUCKETS = 600

const quantum = (c: PlaybackContext) => Math.floor(c.positionMs / (UI_REFRESH_MS * c.speed))
const sameForUi = (a: PlaybackContext, b: PlaybackContext) =>
  a.frames === b.frames && a.speed === b.speed && a.error === b.error && quantum(a) === quantum(b)

/** Playback state for text panels, re-rendering about 10 times per second. */
export function usePlaybackState(actor: PlaybackActor) {
  const status = useSelector(actor, transportStatus)
  const context = useSelector(actor, (s) => s.context, sameForUi)
  const view = useMemo(() => viewAt(context.frames, context.positionMs, stepForSpeed(context.speed)), [context])

  const commands = useMemo(
    () => ({
      toggle: () => actor.send({ type: 'TOGGLE' }),
      seek: (positionMs: number) => actor.send({ type: 'SEEK', positionMs }),
      step: (direction: -1 | 1) => actor.send({ type: 'STEP', direction }),
      setSpeed: (speed: Speed) => actor.send({ type: 'SET_SPEED', speed }),
    }),
    [actor],
  )

  return { status, positionMs: context.positionMs, speed: context.speed, error: context.error, view, ...commands }
}

export function useFlightOverview(flightId: string) {
  return useQuery({
    queryKey: ['flights', 'overview', flightId, OVERVIEW_BUCKETS],
    queryFn: ({ signal }) => getOverview(flightId, OVERVIEW_BUCKETS, signal),
    staleTime: Infinity, // a completed flight never changes
  })
}

/**
 * Space play/pause, ←/→ step, [ and ] slower/faster. Ignored while typing or on controls that use
 * the same keys themselves (buttons use Space, the scrubber and selects use arrows).
 */
export function usePlaybackShortcuts(send: (e: PlaybackEvent) => void, currentSpeed: () => Speed) {
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      // AltGr arrives as Ctrl+Alt; many European layouts need it to type [ and ].
      const altGr = e.ctrlKey && e.altKey
      if (((e.altKey || e.ctrlKey) && !altGr) || e.metaKey || e.defaultPrevented) return
      const target = e.target as HTMLElement
      const tag = target.tagName
      const typing = tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable || (tag === 'INPUT' && (target as HTMLInputElement).type !== 'range')
      if (typing) return

      const speedIndex = SPEEDS.indexOf(currentSpeed())
      // Match the physical bracket keys too, whatever character the layout puts on them.
      const key = e.code === 'BracketLeft' ? '[' : e.code === 'BracketRight' ? ']' : e.key
      if (altGr && key !== '[' && key !== ']') return
      switch (key) {
        case ' ':
          if (tag === 'BUTTON') return
          send({ type: 'TOGGLE' })
          break
        case 'ArrowLeft':
        case 'ArrowRight':
          if (tag === 'INPUT') return
          send({ type: 'STEP', direction: key === 'ArrowLeft' ? -1 : 1 })
          break
        case '[':
          send({ type: 'SET_SPEED', speed: SPEEDS[Math.max(0, speedIndex - 1)] })
          break
        case ']':
          send({ type: 'SET_SPEED', speed: SPEEDS[Math.min(SPEEDS.length - 1, speedIndex + 1)] })
          break
        default:
          return
      }
      e.preventDefault()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [send, currentSpeed])
}
