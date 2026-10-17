import type { EmitterHit, PlaybackFrame, Position } from '../../api/types'

export const SPEEDS = [1, 10, 60, 600] as const
export type Speed = (typeof SPEEDS)[number]

/** About 30 frames per real second at any speed. */
export const stepForSpeed = (speed: number) => Math.max(10, Math.round((speed * 1000) / 30))

/** How long an emitter stays on the scope after its last detection, in flight time. */
export const persistenceFor = (stepMs: number) => Math.max(3_000, 4 * stepMs)

export interface ActiveEmitter extends EmitterHit {
  /** Flight time since this emitter was last seen. */
  ageMs: number
}

export interface CountermeasureEntry {
  offsetMs: number
  kind: number
  count: number
}

export interface PlaybackView {
  emitters: ActiveEmitter[]
  countermeasures: CountermeasureEntry[]
  position: Position | null
  persistenceMs: number
}

const MAX_LOG = 8
const MAX_SCAN = 5_000

/** Index of the last frame whose offset is <= positionMs, or -1. Frames are sorted by offset. */
export function lastFrameAtOrBefore(frames: readonly PlaybackFrame[], positionMs: number): number {
  let lo = 0
  let hi = frames.length - 1
  let found = -1
  while (lo <= hi) {
    const mid = (lo + hi) >> 1
    if (frames[mid].offsetMs <= positionMs) {
      found = mid
      lo = mid + 1
    } else {
      hi = mid - 1
    }
  }
  return found
}

/** What the debrief shows at a playhead position: active emitters (strongest first), recent countermeasures, last position. */
export function viewAt(frames: readonly PlaybackFrame[], positionMs: number, stepMs: number): PlaybackView {
  const persistenceMs = persistenceFor(stepMs)
  const end = lastFrameAtOrBefore(frames, positionMs)

  const latest = new Map<number, ActiveEmitter>()
  for (let i = end; i >= 0 && frames[i].offsetMs > positionMs - persistenceMs; i--) {
    for (const hit of frames[i].emitters) {
      if (!latest.has(hit.emitterId)) latest.set(hit.emitterId, { ...hit, ageMs: positionMs - frames[i].offsetMs })
    }
  }

  const countermeasures: CountermeasureEntry[] = []
  let position: Position | null = null
  for (let i = end; i >= 0 && end - i < MAX_SCAN && (countermeasures.length < MAX_LOG || position === null); i--) {
    const frame = frames[i]
    position ??= frame.position ?? null
    for (const cm of frame.countermeasures) {
      if (countermeasures.length < MAX_LOG) countermeasures.push({ offsetMs: frame.offsetMs, kind: cm.kind, count: cm.count })
    }
  }

  const emitters = [...latest.values()].sort((a, b) => b.amplitudeDb - a.amplitudeDb || a.emitterId - b.emitterId)
  return { emitters, countermeasures, position, persistenceMs }
}
