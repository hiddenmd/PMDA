import { useQuery } from '@tanstack/react-query'
import { useActorRef, useSelector } from '@xstate/react'
import { useCallback, useEffect, useMemo } from 'react'
import { getOverview } from '../../api/playback'
import { playbackMachine, transportStatus, type PlaybackContext, type PlaybackEvent } from '../../machines/playbackMachine'
import { SPEEDS, stepForSpeed, viewAt, type Speed } from './playbackView'

/** Text panels refresh about 10 times per real second; the scope draws itself every frame. */
const UI_REFRESH_MS = 100
const OVERVIEW_BUCKETS = 600

const quantum = (c: PlaybackContext) => Math.floor(c.positionMs / (UI_REFRESH_MS * c.speed))
const sameForUi = (a: PlaybackContext, b: PlaybackContext) =>
  a.frames === b.frames && a.speed === b.speed && a.error === b.error && quantum(a) === quantum(b)

export function usePlayback(flightId: string, durationMs: number) {
  const actor = useActorRef(playbackMachine, { input: { flightId, durationMs } })
  const status = useSelector(actor, transportStatus)
  const context = useSelector(actor, (s) => s.context, sameForUi)
  const view = useMemo(() => viewAt(context.frames, context.positionMs, stepForSpeed(context.speed)), [context])

  const overview = useQuery({
    queryKey: ['flights', 'overview', flightId, OVERVIEW_BUCKETS],
    queryFn: ({ signal }) => getOverview(flightId, OVERVIEW_BUCKETS, signal),
    staleTime: Infinity, // a completed flight never changes
  })

  const commands = useMemo(
    () => ({
      toggle: () => actor.send({ type: 'TOGGLE' }),
      seek: (positionMs: number) => actor.send({ type: 'SEEK', positionMs }),
      step: (direction: -1 | 1) => actor.send({ type: 'STEP', direction }),
      setSpeed: (speed: Speed) => actor.send({ type: 'SET_SPEED', speed }),
    }),
    [actor],
  )

  const currentSpeed = useCallback(() => actor.getSnapshot().context.speed, [actor])
  usePlaybackShortcuts(actor.send, currentSpeed)

  return { actor, status, positionMs: context.positionMs, speed: context.speed, error: context.error, view, overview: overview.data, ...commands }
}

/**
 * Space play/pause, ←/→ step, [ and ] slower/faster. Ignored while typing or on controls that use
 * the same keys themselves (buttons use Space, the scrubber and selects use arrows).
 */
function usePlaybackShortcuts(send: (e: PlaybackEvent) => void, currentSpeed: () => Speed) {
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.altKey || e.ctrlKey || e.metaKey || e.defaultPrevented) return
      const target = e.target as HTMLElement
      const tag = target.tagName
      const typing = tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable || (tag === 'INPUT' && (target as HTMLInputElement).type !== 'range')
      if (typing) return

      const speedIndex = SPEEDS.indexOf(currentSpeed())
      switch (e.key) {
        case ' ':
          if (tag === 'BUTTON') return
          send({ type: 'TOGGLE' })
          break
        case 'ArrowLeft':
        case 'ArrowRight':
          if (tag === 'INPUT') return
          send({ type: 'STEP', direction: e.key === 'ArrowLeft' ? -1 : 1 })
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
