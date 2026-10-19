import { Vector3 } from 'three'
import { describe, expect, it } from 'vitest'
import type { FlightTrack } from '../../../api/types'
import { attitudeQuaternion, cameraOffset, damp, orbitPitchRange, toLocal, trailPoints } from './frame'

const forward = (q: ReturnType<typeof attitudeQuaternion>) => new Vector3(0, 0, -1).applyQuaternion(q)
const rightWing = (q: ReturnType<typeof attitudeQuaternion>) => new Vector3(1, 0, 0).applyQuaternion(q)

describe('attitudeQuaternion', () => {
  it('is the identity for level flight heading north', () => {
    const q = attitudeQuaternion(0, 0, 0)
    expect(q.x).toBeCloseTo(0)
    expect(q.y).toBeCloseTo(0)
    expect(q.z).toBeCloseTo(0)
    expect(Math.abs(q.w)).toBeCloseTo(1)
  })

  it('points the nose east at heading 90', () => {
    const f = forward(attitudeQuaternion(0, 0, 90))
    expect(f.x).toBeCloseTo(1)
    expect(f.z).toBeCloseTo(0)
  })

  it('raises the nose for positive pitch', () => {
    expect(forward(attitudeQuaternion(10, 0, 0)).y).toBeCloseTo(Math.sin((10 * Math.PI) / 180))
  })

  it('lowers the right wing for positive roll', () => {
    expect(rightWing(attitudeQuaternion(0, 30, 0)).y).toBeCloseTo(-0.5)
  })
})

describe('toLocal', () => {
  it('maps north to −z, east to +x and altitude to +y', () => {
    const o = { lat: 45, lon: 10, altM: 1000 }
    const [nx, , nz] = toLocal(o, { lat: 45.001, lon: 10, altM: 1000 })
    expect(nx).toBeCloseTo(0)
    expect(nz).toBeCloseTo(-111.19, 1)
    const [ex, , ez] = toLocal(o, { lat: 45, lon: 10.001, altM: 1000 })
    expect(ex).toBeCloseTo(78.62, 1)
    expect(ez).toBeCloseTo(0)
    expect(toLocal(o, { lat: 45, lon: 10, altM: 1250 })[1]).toBe(250)
  })
})

describe('trailPoints', () => {
  const track: FlightTrack = {
    t: [0, 30_000, 60_000, 90_000, 120_000],
    lat: [0, 0.001, 0.002, 0.003, 0.004],
    lon: [0, 0, 0, 0, 0],
    altM: [100, 100, 100, 100, 100],
  }

  it('covers the last 60 s up to the playhead, newest last, relative to the playhead', () => {
    const p = trailPoints(track, 100_000)
    // samples at 60 000 and 90 000 plus the interpolated playhead point
    expect(p.length).toBe(9)
    expect(p[p.length - 3]).toBeCloseTo(0)
    expect(p[p.length - 1]).toBeCloseTo(0)
    expect(p[2]).toBeGreaterThan(0) // older points lie south (+z) of the playhead when flying north
  })

  it('caps the number of points', () => {
    const many: FlightTrack = { t: [], lat: [], lon: [], altM: [] }
    for (let i = 0; i <= 600; i++) {
      many.t.push(i * 100)
      many.lat.push(i * 1e-5)
      many.lon.push(0)
      many.altM.push(0)
    }
    expect(trailPoints(many, 60_000, 60_000, 240).length / 3).toBeLessThanOrEqual(240)
  })

  it('is empty without samples', () => {
    expect(trailPoints({ t: [], lat: [], lon: [], altM: [] }, 0).length).toBe(0)
  })
})

describe('cameraOffset', () => {
  const none = { yaw: 0, pitch: 0 }

  it('chases from behind-right-above at heading 0', () => {
    const o = cameraOffset('chase', 0, none)
    expect(o.x).toBeCloseTo(16, 1)
    expect(o.y).toBeCloseTo(7.4, 1)
    expect(o.z).toBeCloseTo(39, 1)
  })

  it('turns with the heading', () => {
    const o = cameraOffset('side', 90, none)
    expect(o.x).toBeCloseTo(0, 5)
    expect(o.z).toBeCloseTo(50, 5) // right of an east-bound aircraft is south (+z)
  })

  it('looks down from above, north-up, regardless of heading', () => {
    const o = cameraOffset('top', 123, none)
    expect(o.y).toBeGreaterThan(59)
    expect(o.x).toBeCloseTo(0, 0)
  })

  it('clamps orbit elevation', () => {
    expect(cameraOffset('chase', 0, { yaw: 0, pitch: -90 }).y).toBeCloseTo(-Math.hypot(16, 7.4, 39) * Math.sin((10 * Math.PI) / 180), 1)
  })
})

describe('orbitPitchRange', () => {
  it('keeps the effective elevation within [-10, 89] per preset', () => {
    expect(orbitPitchRange('top')).toEqual([-99, 0])
    expect(orbitPitchRange('side')).toEqual([-10, 89])
  })

  it('clamps the upper elevation at 89 degrees', () => {
    const d = Math.hypot(16, 7.4, 39)
    expect(cameraOffset('chase', 0, { yaw: 0, pitch: 200 }).y).toBeCloseTo(d * Math.sin((89 * Math.PI) / 180), 1)
  })
})

describe('damp', () => {
  it('is 0 for no time and approaches 1', () => {
    expect(damp(0.05, 0)).toBe(0)
    expect(damp(0.05, 1)).toBeCloseTo(0.95)
  })
})
