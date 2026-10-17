import { describe, expect, it } from 'vitest'
import type { EmitterHit, PlaybackFrame } from '../../api/types'
import { lastFrameAtOrBefore, persistenceFor, stepForSpeed, viewAt } from './playbackView'

const hit = (emitterId: number, amplitudeDb = -50): EmitterHit => ({
  emitterId,
  hits: 1,
  frequencyMHz: 9000,
  priUs: 1000,
  pulseWidthUs: 1,
  bearingDeg: 45,
  amplitudeDb,
})

const frame = (offsetMs: number, parts: Partial<PlaybackFrame> = {}): PlaybackFrame => ({
  offsetMs,
  emitters: [],
  countermeasures: [],
  position: null,
  ...parts,
})

describe('playback view', () => {
  it('chooses about 30 frames per real second', () => {
    expect([1, 10, 60, 600].map(stepForSpeed)).toEqual([33, 333, 2000, 20000])
    expect(persistenceFor(33)).toBe(3000)
    expect(persistenceFor(20000)).toBe(80000)
  })

  it('finds the last frame at or before a position', () => {
    const frames = [frame(0), frame(100), frame(200)]
    expect(lastFrameAtOrBefore(frames, -1)).toBe(-1)
    expect(lastFrameAtOrBefore(frames, 0)).toBe(0)
    expect(lastFrameAtOrBefore(frames, 150)).toBe(1)
    expect(lastFrameAtOrBefore(frames, 9999)).toBe(2)
  })

  it('shows emitters seen within the persistence window, latest values, strongest first', () => {
    const frames = [
      frame(0, { emitters: [hit(1, -80)] }), // too old at 5000
      frame(2500, { emitters: [hit(2, -70), hit(3, -40)] }),
      frame(4000, { emitters: [hit(2, -30)] }),
      frame(6000, { emitters: [hit(4)] }), // in the future
    ]

    const view = viewAt(frames, 5000, 33)

    expect(view.emitters.map((e) => [e.emitterId, e.amplitudeDb, e.ageMs])).toEqual([
      [2, -30, 1000],
      [3, -40, 2500],
    ])
  })

  it('logs recent countermeasures newest first and the latest position', () => {
    const frames = [
      frame(0, { position: { lat: 1, lon: 1, altM: 100 } }),
      frame(100, { countermeasures: [{ kind: 1, count: 3 }] }),
      frame(200, { countermeasures: [{ kind: 2, count: 2 }], position: { lat: 2, lon: 2, altM: 200 } }),
      frame(300, { countermeasures: [{ kind: 1, count: 9 }] }),
    ]

    const view = viewAt(frames, 250, 33)

    expect(view.countermeasures).toEqual([
      { offsetMs: 200, kind: 2, count: 2 },
      { offsetMs: 100, kind: 1, count: 3 },
    ])
    expect(view.position).toEqual({ lat: 2, lon: 2, altM: 200 })
  })

  it('is empty before the first frame', () => {
    expect(viewAt([frame(100, { emitters: [hit(1)] })], 50, 33)).toMatchObject({ emitters: [], countermeasures: [], position: null })
  })
})
