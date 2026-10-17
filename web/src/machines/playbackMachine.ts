import { assign, fromCallback, fromPromise, setup, type SnapshotFrom } from 'xstate'
import { getFrames } from '../api/playback'
import type { PlaybackFrame, PlaybackFrames } from '../api/types'
import { persistenceFor, stepForSpeed, type Speed } from '../features/playback/playbackView'

/**
 * Debrief playback of one flight. Two parallel regions:
 *
 * transport:  paused ⇄ playing ⇄ buffering, → ended
 *   `playing` runs a frame clock (TICK with real elapsed ms); the playhead advances by elapsed × speed.
 *   When the playhead leaves the loaded frames it waits in `buffering`; at the end it stops in `ended`.
 *
 * loader:     idle → fetching → idle | failed
 *   Keeps frames loaded around the playhead: a fresh chunk after a seek or speed change (`replace`),
 *   the next chunk when the playhead gets near the end of what is loaded (`append`). A request that a
 *   seek or speed change made pointless is dropped (leaving `fetching` aborts it).
 *
 * Positions are ms from the flight start. All I/O is in the actors; tests replace them.
 */

export const CHUNK_STEPS = 300
export const PREFETCH_STEPS = 150
export const RETRY_DELAY_MS = 2_000
/**
 * Upper bound for one tick. Playback follows real time even when frames are slow (a throttled or
 * busy page still renders ~1 fps); only longer gaps, such as a debugger pause, are cut short.
 */
export const MAX_TICK_MS = 1_000

interface FramesRange {
  fromMs: number
  toMs: number
  stepMs: number
}

interface FramesRequest extends FramesRange {
  mode: 'replace' | 'append'
}

export interface PlaybackContext {
  flightId: string
  durationMs: number
  positionMs: number
  speed: Speed
  frames: PlaybackFrame[]
  loaded: FramesRange | null
  request: FramesRequest | null
  error: string | null
}

export type PlaybackEvent =
  | { type: 'PLAY' }
  | { type: 'PAUSE' }
  | { type: 'TOGGLE' }
  | { type: 'SEEK'; positionMs: number }
  | { type: 'SET_SPEED'; speed: Speed }
  | { type: 'STEP'; direction: -1 | 1 }
  | { type: 'TICK'; deltaMs: number }

export interface PlaybackInput {
  flightId: string
  durationMs: number
  speed?: Speed
}

/**
 * requestAnimationFrame clock; stops when `playing` is left. Hidden pages get no frames at all, so
 * the clock restarts its reference time when the page becomes visible: no jump after a tab switch.
 */
export const clockActor = fromCallback<PlaybackEvent>(({ sendBack }) => {
  let last = performance.now()
  const onVisibility = () => {
    if (document.visibilityState === 'visible') last = performance.now()
  }
  document.addEventListener('visibilitychange', onVisibility)
  let handle = requestAnimationFrame(function tick(now) {
    sendBack({ type: 'TICK', deltaMs: Math.min(MAX_TICK_MS, Math.max(0, now - last)) })
    last = now
    handle = requestAnimationFrame(tick)
  })
  return () => {
    cancelAnimationFrame(handle)
    document.removeEventListener('visibilitychange', onVisibility)
  }
})

/**
 * Loads one chunk. The signal aborts when the request is no longer wanted (the loader left
 * `fetching`, or the actor stopped, e.g. React StrictMode's remount): that is not a failure, so an
 * aborted request settles never instead of reporting an error.
 */
export const fetchFramesActor = fromPromise<PlaybackFrames, { flightId: string; request: FramesRequest }>(({ input, signal }) =>
  getFrames(input.flightId, { fromMs: input.request.fromMs, toMs: input.request.toMs, stepMs: input.request.stepMs }, signal).catch(
    (error: unknown) => {
      if (signal.aborted) return new Promise<never>(() => undefined)
      throw error
    },
  ),
)

const step = (c: PlaybackContext) => stepForSpeed(c.speed)
const clamp = (c: PlaybackContext, ms: number) => Math.min(c.durationMs, Math.max(0, ms))
const atEnd = (c: PlaybackContext) => c.positionMs >= c.durationMs
const loadedHere = (c: PlaybackContext) =>
  c.loaded !== null && c.loaded.stepMs === step(c) && c.positionMs >= c.loaded.fromMs && c.positionMs < c.loaded.toMs

export const playbackMachine = setup({
  types: {
    context: {} as PlaybackContext,
    events: {} as PlaybackEvent,
    input: {} as PlaybackInput,
  },
  actors: {
    clock: clockActor,
    fetchFrames: fetchFramesActor,
  },
  guards: {
    atEnd: ({ context }) => atEnd(context),
    notAtEnd: ({ context }) => !atEnd(context),
    covered: ({ context }) => atEnd(context) || loadedHere(context),
    notCovered: ({ context }) => !atEnd(context) && !loadedHere(context),
    needsReload: ({ context }) => !atEnd(context) && !loadedHere(context),
    needsMore: ({ context }) =>
      loadedHere(context) &&
      context.loaded!.toMs < context.durationMs &&
      context.loaded!.toMs - context.positionMs < PREFETCH_STEPS * step(context),
    requestStale: ({ context }) => {
      const r = context.request
      if (!r) return true
      if (r.stepMs !== step(context)) return true
      if (r.mode === 'replace') return context.positionMs < r.fromMs || context.positionMs >= r.toMs
      return context.loaded?.toMs !== r.fromMs || context.loaded.stepMs !== r.stepMs
    },
  },
  actions: {
    rewind: assign({ positionMs: 0 }),
    advance: assign({
      positionMs: ({ context, event }) => (event.type === 'TICK' ? clamp(context, context.positionMs + event.deltaMs * context.speed) : context.positionMs),
    }),
    planReload: assign({
      request: ({ context }) => {
        const s = step(context)
        // Start a little before the playhead so emitters seen just before a seek still show.
        const fromMs = Math.max(0, Math.floor((context.positionMs - persistenceFor(s)) / s) * s)
        const toMs = Math.max(fromMs + CHUNK_STEPS * s, Math.floor(context.positionMs / s) * s + s)
        return { fromMs, toMs, stepMs: s, mode: 'replace' as const }
      },
    }),
    planAppend: assign({
      request: ({ context }) => {
        const s = step(context)
        const fromMs = context.loaded!.toMs
        return { fromMs, toMs: fromMs + CHUNK_STEPS * s, stepMs: s, mode: 'append' as const }
      },
    }),
  },
}).createMachine({
  id: 'playback',
  type: 'parallel',
  context: ({ input }) => ({
    flightId: input.flightId,
    durationMs: Math.max(0, input.durationMs),
    positionMs: 0,
    speed: input.speed ?? 1,
    frames: [],
    loaded: null,
    request: null,
    error: null,
  }),
  on: {
    SEEK: { actions: assign({ positionMs: ({ context, event }) => clamp(context, event.positionMs) }) },
    STEP: {
      actions: assign({
        positionMs: ({ context, event }) => clamp(context, context.positionMs + event.direction * 10_000 * Math.max(1, context.speed / 10)),
      }),
    },
    SET_SPEED: { actions: assign({ speed: ({ event }) => event.speed }) },
  },
  states: {
    transport: {
      initial: 'paused',
      states: {
        paused: {
          on: {
            PLAY: [{ guard: 'atEnd', target: 'playing', actions: 'rewind' }, { target: 'playing' }],
            TOGGLE: [{ guard: 'atEnd', target: 'playing', actions: 'rewind' }, { target: 'playing' }],
          },
        },
        playing: {
          invoke: { src: 'clock' },
          always: [
            { guard: 'atEnd', target: 'ended' },
            { guard: 'notCovered', target: 'buffering' },
          ],
          on: {
            TICK: { actions: 'advance' },
            PAUSE: 'paused',
            TOGGLE: 'paused',
          },
        },
        buffering: {
          always: { guard: 'covered', target: 'playing' },
          on: { PAUSE: 'paused', TOGGLE: 'paused' },
        },
        ended: {
          always: { guard: 'notAtEnd', target: 'paused' },
          on: {
            PLAY: { target: 'playing', actions: 'rewind' },
            TOGGLE: { target: 'playing', actions: 'rewind' },
          },
        },
      },
    },
    loader: {
      initial: 'idle',
      states: {
        idle: {
          always: [
            { guard: 'needsReload', target: 'fetching', actions: 'planReload' },
            { guard: 'needsMore', target: 'fetching', actions: 'planAppend' },
          ],
        },
        fetching: {
          invoke: {
            src: 'fetchFrames',
            input: ({ context }) => ({ flightId: context.flightId, request: context.request! }),
            onDone: {
              target: 'idle',
              actions: assign(({ context, event }) => {
                const r = context.request!
                const frames = event.output.frames
                if (r.mode === 'replace' || !context.loaded) {
                  return { frames, loaded: { fromMs: r.fromMs, toMs: r.toMs, stepMs: r.stepMs }, request: null, error: null }
                }
                // Append, dropping frames well behind the playhead so memory stays bounded.
                const keepFromMs = context.positionMs - persistenceFor(r.stepMs) - CHUNK_STEPS * r.stepMs
                const kept = context.frames.filter((f) => f.offsetMs >= keepFromMs)
                return {
                  frames: kept.concat(frames),
                  loaded: { fromMs: Math.max(context.loaded.fromMs, keepFromMs), toMs: r.toMs, stepMs: r.stepMs },
                  request: null,
                  error: null,
                }
              }),
            },
            onError: { target: 'failed', actions: assign({ request: null, error: 'Could not load playback data. Retrying…' }) },
          },
          // Evaluated after every event: a seek or speed change can make this request pointless.
          always: { guard: 'requestStale', target: 'idle', actions: assign({ request: null }) },
        },
        failed: {
          after: { [RETRY_DELAY_MS]: { target: 'idle', actions: assign({ error: null }) } },
          on: { SEEK: { target: 'idle', actions: assign({ error: null }) } },
        },
      },
    },
  },
})

export type PlaybackSnapshot = SnapshotFrom<typeof playbackMachine>
export type TransportStatus = 'paused' | 'playing' | 'buffering' | 'ended'

export function transportStatus(snapshot: PlaybackSnapshot): TransportStatus {
  const value = snapshot.value as { transport: TransportStatus }
  return value.transport
}
